using System.Text;
using ShellCommand.Broker;
using ShellCommand.Core;

namespace ShellCommand.Broker.Tests;

public class BrokerTests
{
    [Fact]
    public void TokensAreSingleUseAndExpire()
    {
        var store = new ActionTokenStore(TimeSpan.Zero);
        var token = store.Issue(new ActionSpec(ActionKind.UserCommand, "test.exe", @"C:\"));
        Assert.False(store.TryConsume(token, out _, out var status));
        Assert.Equal(TokenStatus.TokenExpired, status);
        Assert.False(store.TryConsume(token, out _, out status));
        Assert.Equal(TokenStatus.TokenNotFound, status);
    }

    [Fact]
    public async Task PipeFrameRejectsLargePayload()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => PipeProtocol.WriteAsync(new MemoryStream(), new PipeFrame(1, MessageType.PingRequest, new byte[PipeProtocol.MaxPayload + 1]), default));
    }

    [Fact]
    public async Task PipeFrameRejectsMalformedLengthBeforeAllocation()
    {
        using var stream = new MemoryStream();
        stream.Write("SC11"u8);
        stream.WriteByte((byte)PipeProtocol.Version); stream.WriteByte(0); // version
        stream.WriteByte(1); stream.WriteByte(0); // PingRequest
        stream.Write(new byte[4]);
        stream.Write(BitConverter.GetBytes((uint)(PipeProtocol.MaxPayload + 1)));
        stream.Position = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() => PipeProtocol.ReadAsync(stream, default));
    }

    [Fact]
    public async Task PipeFrameRoundTripsUnicodeString()
    {
        using var stream = new MemoryStream();
        var payload = PipeProtocol.StringPayload("C:\\项目\\A B");
        await PipeProtocol.WriteAsync(stream, new PipeFrame(42, MessageType.ResolveMenuRequest, payload), default);
        stream.Position = 0;
        var frame = await PipeProtocol.ReadAsync(stream, default);
        var offset = 0;
        Assert.Equal("C:\\项目\\A B", PipeProtocol.ReadString(frame.Payload, ref offset));
        Assert.Equal((uint)42, frame.RequestId);
    }

    [Fact]
    public void RuntimeUsesLastKnownGoodAfterInvalidEdit()
    {
        var root = Path.Combine(Path.GetTempPath(), "sc11-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var global = Path.Combine(root, "global.yaml");
            var dir = Path.Combine(root, "folder");
            Directory.CreateDirectory(dir);
            var local = Path.Combine(dir, ".shellcommand.yaml");
            File.WriteAllText(global, "GlobalCommands: []\n");
            File.WriteAllText(local, "- Command: good.exe\n");
            var runtime = new FileConfigRuntime(globalPath: global);
            Assert.Single(runtime.Load(dir).Directory!.Commands);
            File.WriteAllText(local, "- Command: \"unterminated\n");
            var snapshot = runtime.Load(dir, forceRefresh: true);
            Assert.Single(snapshot.Directory!.Commands);
            Assert.Contains(snapshot.Diagnostics, d => d.Code is "YAML_SYNTAX" or "INVALID_ROOT");
        }
        finally { Directory.Delete(root, true); }
    }
}
