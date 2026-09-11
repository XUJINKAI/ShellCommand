using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;
using ShellCommand.Core;

namespace ShellCommand.Broker;

public enum MessageType : ushort
{
    PingRequest = 1, PingResponse = 2,
    ResolveMenuRequest = 10, ResolveMenuResponse = 11,
    InvokeRequest = 20, InvokeAccepted = 21,
    RefreshRequest = 30, RefreshResponse = 31
}

public readonly record struct PipeFrame(uint RequestId, MessageType Type, byte[] Payload);

public static class PipeProtocol
{
    public const ushort Version = 3;
    public const int HeaderSize = 16;
    public const int MaxPayload = 256 * 1024;
    public const int MaxStringBytes = 32 * 1024;
    private static readonly byte[] Magic = "SC11"u8.ToArray();

    public static string DefaultPipeName()
    {
        var identity = OperatingSystem.IsWindows() ? System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value : Environment.UserName;
        return "ShellCommand11." + (identity ?? "unknown") + "." + (OperatingSystem.IsWindows() ? System.Diagnostics.Process.GetCurrentProcess().SessionId : 0);
    }

    public static async Task<PipeFrame> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[HeaderSize];
        await ReadExactlyAsync(stream, header, cancellationToken).ConfigureAwait(false);
        if (!header.AsSpan(0, 4).SequenceEqual(Magic)) throw new InvalidDataException("Invalid IPC magic.");
        if (BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(4, 2)) != Version) throw new InvalidDataException("Unsupported IPC version.");
        var type = (MessageType)BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(6, 2));
        if (!Enum.IsDefined(type)) throw new InvalidDataException("Unknown IPC message type.");
        var length = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(12, 4));
        if (length > MaxPayload) throw new InvalidDataException("IPC payload exceeds the limit.");
        var payload = new byte[(int)length];
        await ReadExactlyAsync(stream, payload, cancellationToken).ConfigureAwait(false);
        return new(BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8, 4)), type, payload);
    }

    public static async Task WriteAsync(Stream stream, PipeFrame frame, CancellationToken cancellationToken)
    {
        if (frame.Payload.Length > MaxPayload) throw new InvalidDataException("IPC payload exceeds the limit.");
        var header = new byte[HeaderSize];
        Magic.CopyTo(header, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4, 2), Version);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(6, 2), (ushort)frame.Type);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8, 4), frame.RequestId);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12, 4), (uint)frame.Payload.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(frame.Payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static byte[] StringPayload(string value)
    {
        using var stream = new MemoryStream();
        WriteString(stream, value);
        return stream.ToArray();
    }

    public static string ReadString(ReadOnlySpan<byte> payload, ref int offset)
    {
        if (offset > payload.Length - 4) throw new InvalidDataException("Missing string length.");
        var length = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(offset, 4));
        offset += 4;
        if (length > MaxStringBytes || length > (uint)(payload.Length - offset)) throw new InvalidDataException("Invalid string length.");
        var value = new UTF8Encoding(false, true).GetString(payload.Slice(offset, (int)length));
        if (value.Contains('\0')) throw new InvalidDataException("Embedded NUL in string.");
        offset += (int)length;
        return value;
    }

    public static void WriteString(Stream stream, string value)
    {
        var bytes = new UTF8Encoding(false, true).GetBytes(value);
        if (bytes.Length > MaxStringBytes) throw new InvalidDataException("String exceeds the limit.");
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(length, (uint)bytes.Length);
        stream.Write(length);
        stream.Write(bytes);
    }

    private static async Task ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken).ConfigureAwait(false);
            if (read == 0) throw new EndOfStreamException("IPC peer closed early.");
            offset += read;
        }
    }
}

public sealed class PipeServer : IDisposable
{
    private readonly string _pipeName;
    private readonly IBrokerEndpoint _engine;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _connections = new(16, 16);

    public PipeServer(string pipeName, IBrokerEndpoint engine) { _pipeName = pipeName; _engine = engine; }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
        while (!linked.IsCancellationRequested)
        {
            await _connections.WaitAsync(linked.Token).ConfigureAwait(false);
            var pipe = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 64 * 1024, 64 * 1024);
            try
            {
                await pipe.WaitForConnectionAsync(linked.Token).ConfigureAwait(false);
                _ = HandleAsync(pipe, linked.Token);
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested) { pipe.Dispose(); _connections.Release(); }
            catch { pipe.Dispose(); _connections.Release(); }
        }
    }

    private async Task HandleAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        using (pipe)
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            deadline.CancelAfter(TimeSpan.FromSeconds(1));
            cancellationToken = deadline.Token;
            try
            {
                var frame = await PipeProtocol.ReadAsync(pipe, cancellationToken).ConfigureAwait(false);
                var response = await DispatchAsync(frame, cancellationToken).ConfigureAwait(false);
                await PipeProtocol.WriteAsync(pipe, response, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception) { /* malformed clients are isolated to this connection */ }
            finally { _connections.Release(); }
        }
    }

    private async Task<PipeFrame> DispatchAsync(PipeFrame frame, CancellationToken cancellationToken)
    {
        switch (frame.Type)
        {
            case MessageType.PingRequest:
                if (frame.Payload.Length != 0) throw new InvalidDataException("Ping payload must be empty.");
                return new(frame.RequestId, MessageType.PingResponse, PipeProtocol.StringPayload(AppContext.BaseDirectory));
            case MessageType.ResolveMenuRequest:
            {
                var context = DecodeContext(frame.Payload);
                return new(frame.RequestId, MessageType.ResolveMenuResponse, EncodeResolve(_engine.Resolve(context)));
            }
            case MessageType.RefreshRequest:
            {
                var offset = 0;
                var path = PipeProtocol.ReadString(frame.Payload, ref offset);
                if (offset != frame.Payload.Length) throw new InvalidDataException("Trailing refresh payload.");
                _engine.Refresh(path.Length == 0 ? null : path);
                return new(frame.RequestId, MessageType.RefreshResponse, []);
            }
            case MessageType.InvokeRequest:
                if (frame.Payload.Length != 16) throw new InvalidDataException("Invalid token payload.");
                var token = new Guid(frame.Payload);
                var status = await _engine.InvokeAsync(token, cancellationToken).ConfigureAwait(false);
                return new(frame.RequestId, MessageType.InvokeAccepted, new[] { (byte)status });
            default: throw new InvalidDataException("Unsupported request type.");
        }
    }

    public static byte[] ContextPayload(MenuContext context)
    {
        using var stream = new MemoryStream();
        PipeProtocol.WriteString(stream, context.Directory ?? "");
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        if (context.Selection.Count > 256) throw new InvalidDataException("Too many selections.");
        writer.Write((ushort)context.Selection.Count);
        foreach (var item in context.Selection) { writer.Write(item.IsFolder ? (byte)1 : (byte)0); PipeProtocol.WriteString(stream, item.Path); }
        return stream.ToArray();
    }
    public static MenuContext DecodeContext(byte[] payload)
    {
        var offset = 0; var directory = PipeProtocol.ReadString(payload, ref offset);
        if (offset + 2 > payload.Length) throw new InvalidDataException("Missing selection count.");
        var count = BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(offset, 2)); offset += 2;
        if (count > 256) throw new InvalidDataException("Too many selections.");
        var selection = new List<SelectionItem>();
        for (var i = 0; i < count; i++)
        {
            if (offset >= payload.Length || payload[offset] > 1) throw new InvalidDataException("Invalid selection type.");
            var folder = payload[offset++] == 1;
            selection.Add(new(PipeProtocol.ReadString(payload, ref offset), folder));
        }
        if (offset != payload.Length) throw new InvalidDataException("Trailing context payload.");
        return new(directory.Length == 0 ? null : directory, selection);
    }
    public static byte[] EncodeResolve(ResolveResult result)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write((byte)result.Status);
        var total = 0;
        void Items(IReadOnlyList<MenuDto> items, int depth)
        {
            if (depth > 3 || items.Count > 128 || (total += items.Count) > 128) throw new InvalidDataException("Menu exceeds limits.");
            writer.Write((ushort)items.Count);
            foreach (var item in items)
            {
                writer.Write(item.Kind); writer.Write(item.Kind == 1 ? (byte)0 : (byte)1); writer.Write((ushort)0);
                writer.Write(item.Token.ToByteArray());
                PipeProtocol.WriteString(stream, item.Title); PipeProtocol.WriteString(stream, item.IconRef);
                Items(item.Items ?? [], depth + 1);
            }
        }
        Items(result.Items, 0);
        return stream.ToArray();
    }

    public void Dispose() => _stop.Cancel();
}
