using ShuttleManager.Shared.Models.Protocol;
using ShuttleManager.Shared.Services.Enums;
using ShuttleManager.Shared.Services.ShuttleClient;
using ShuttleManager.Shared.Services.ShuttleClient.BinaryService;
using ShuttleManager.Shared.Services.ShuttleClient.Helpers;

namespace ShuttleManager.Tests;

public class SendPacketTimeoutTests
{
    private static BinaryProtocolHandler CreateHandler()
    {
        var callbacks = new ProtocolCallbacks
        {
            OnMessage = (_, _) => { },
        };

        return new BinaryProtocolHandler(callbacks);
    }

    private static ShuttleConnection CreateConnection(FakeTransport transport)
    {
        return new ShuttleConnection
        {
            IpAddress = "192.168.1.131",
            Transport = transport,
            Protocol = ShuttleProtocolType.Binary,
        };
    }

    private static byte[] BuildAckFrame(byte refSeq)
    {
        var packet = new AckPacket { RefSeq = refSeq, Result = AckResult.ACK_OK };
        return BinaryFrameCodec.BuildFrame(MsgID.MSG_ACK, ProtocolConstants.TARGET_ID_NONE, refSeq, in packet);
    }

    [Fact]
    public async Task NoAckWithinTimeout_ReturnsFalseAndLeavesNoWaiter()
    {
        BinaryProtocolHandler handler = CreateHandler();
        var transport = new FakeTransport();
        ShuttleConnection conn = CreateConnection(transport);

        bool result = await handler.SendCommandAsync(conn, ShuttleCommand.Stop, 0, 0, CancellationToken.None, 50);

        Assert.False(result);
        Assert.Empty(conn.AckWaiters);
        Assert.Single(transport.Written);
    }

    [Fact]
    public async Task LateAckAfterTimeout_IsIgnoredSafely()
    {
        BinaryProtocolHandler handler = CreateHandler();
        var transport = new FakeTransport();
        ShuttleConnection conn = CreateConnection(transport);

        bool result = await handler.SendCommandAsync(conn, ShuttleCommand.Stop, 0, 0, CancellationToken.None, 50);

        Assert.False(result);

        // Поздний ACK на уже истёкший seq — не должен падать и не должен оставлять waiter.
        conn.ReceiveBuffer.Write(BuildAckFrame(transport.Written[0][4]));
        handler.ProcessBuffer(conn);

        Assert.Empty(conn.AckWaiters);
    }

    [Fact]
    public async Task WriteFailure_ReturnsFalseAndLeavesNoWaiter()
    {
        BinaryProtocolHandler handler = CreateHandler();
        var transport = new FakeTransport { ThrowOnWrite = true };
        ShuttleConnection conn = CreateConnection(transport);

        bool result = await handler.SendCommandAsync(conn, ShuttleCommand.Stop, 0, 0, CancellationToken.None, 1000);

        Assert.False(result);
        Assert.Empty(conn.AckWaiters);
    }

    [Fact]
    public async Task AckArrivesWithinTimeout_ReturnsTrue()
    {
        BinaryProtocolHandler handler = CreateHandler();
        var transport = new FakeTransport();
        ShuttleConnection conn = CreateConnection(transport);

        Task<bool> sendTask = handler.SendCommandAsync(conn, ShuttleCommand.Stop, 0, 0, CancellationToken.None, 5000);

        conn.ReceiveBuffer.Write(BuildAckFrame(transport.Written[0][4]));
        handler.ProcessBuffer(conn);

        Assert.True(await sendTask);
        Assert.Empty(conn.AckWaiters);
    }

    [Fact]
    public async Task ZeroTimeout_ReturnsFalseAndLeavesNoWaiter()
    {
        BinaryProtocolHandler handler = CreateHandler();
        var transport = new FakeTransport();
        ShuttleConnection conn = CreateConnection(transport);

        bool result = await handler.SendCommandAsync(conn, ShuttleCommand.Stop, 0, 0, CancellationToken.None, 0);

        Assert.False(result);
        Assert.Empty(conn.AckWaiters);
    }
}
