using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;

namespace ShellCommand.Broker;

public enum MessageType : ushort
{
    PingRequest = 1, PingResponse = 2,
    ResolveMenuRequest = 10, ResolveMenuResponse = 11,
    InvokeRequest = 20, InvokeAccepted = 21,
    OpenAppRequest = 30, OpenAppAccepted = 31
}

public readonly record struct PipeFrame(uint RequestId, MessageType Type, byte[] Payload);

public static class PipeProtocol
{
    public const ushort Version = 2;
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
                var offset = 0;
                var path = PipeProtocol.ReadString(frame.Payload, ref offset);
                if (offset != frame.Payload.Length) throw new InvalidDataException("Trailing Resolve payload.");
                return new(frame.RequestId, MessageType.ResolveMenuResponse, EncodeResolve(_engine.Resolve(path)));
            }
            case MessageType.InvokeRequest:
                if (frame.Payload.Length != 16) throw new InvalidDataException("Invalid token payload.");
                var token = new Guid(frame.Payload);
                var status = await _engine.InvokeAsync(token, cancellationToken).ConfigureAwait(false);
                return new(frame.RequestId, MessageType.InvokeAccepted, new[] { (byte)status });
            default: throw new InvalidDataException("Unsupported request type.");
        }
    }

    private static byte[] EncodeResolve(ResolveResult result)
    {
        using var stream = new MemoryStream();
        stream.WriteByte((byte)result.Status);
        Span<byte> count = stackalloc byte[2];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(count, (ushort)Math.Min(result.Items.Count, 100));
        stream.Write(count);
        foreach (var item in result.Items.Take(100))
        {
            stream.WriteByte(item.Kind);
            stream.WriteByte(item.Kind == 0 ? (byte)1 : (byte)0);
            stream.WriteByte(0); stream.WriteByte(0);
            stream.Write(item.Token.ToByteArray());
            PipeProtocol.WriteString(stream, item.Kind == 1 ? string.Empty : item.Title);
            PipeProtocol.WriteString(stream, item.Kind == 1 ? string.Empty : item.IconRef);
        }
        return stream.ToArray();
    }

    public void Dispose() => _stop.Cancel();
}
