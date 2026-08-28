using ShuttleManager.Shared.Models.Protocol;
using ShuttleManager.Shared.Services.Enums;
using ShuttleManager.Shared.Services.ShuttleClient;
using ShuttleManager.Shared.Services.ShuttleClient.BinaryService;
using ShuttleManager.Shared.Services.ShuttleClient.Helpers;

namespace ShuttleManager.Tests;

public class AckIsolationTests
{
    private static BinaryProtocolHandler CreateHandler()
    {
        var callbacks = new ProtocolCallbacks
        {
            OnMessage = (_, _) => { },
        };

        return new BinaryProtocolHandler(callbacks);
    }

    private static ShuttleConnection CreateConnection(FakeTransport transport, string ip)
    {
        return new ShuttleConnection
        {
            IpAddress = ip,
            Transport = transport,
            Protocol = ShuttleProtocolType.Binary,
        };
    }

    private static byte[] BuildAckFrame(byte refSeq, AckResult result = AckResult.ACK_OK)
    {
        var packet = new AckPacket { RefSeq = refSeq, Result = result };
        return BinaryFrameCodec.BuildFrame(MsgID.MSG_ACK, ProtocolConstants.TARGET_ID_NONE, refSeq, in packet);
    }

    [Fact]
    public async Task AckFromOneConnection_DoesNotCompleteCommandOfAnother()
    {
        BinaryProtocolHandler handler = CreateHandler();
        var transport1 = new FakeTransport();
        var transport2 = new FakeTransport();
        ShuttleConnection conn1 = CreateConnection(transport1, "192.168.1.131");
        ShuttleConnection conn2 = CreateConnection(transport2, "192.168.1.132");

        Task<bool> task1 = handler.SendCommandAsync(conn1, ShuttleCommand.Stop, 0, 0, CancellationToken.None, 10000);
        Task<bool> task2 = handler.SendCommandAsync(conn2, ShuttleCommand.Stop, 0, 0, CancellationToken.None, 10000);

        // Оба соединения выдали одинаковый первый seq = 0 — именно эта ситуация ломала общий словарь ACK.
        Assert.Equal(0, transport1.Written[0][4]);
        Assert.Equal(0, transport2.Written[0][4]);

        // ACK приходит только для conn2.
        conn2.ReceiveBuffer.Write(BuildAckFrame(0));
        handler.ProcessBuffer(conn2);

        Assert.True(await task2);

        // Команда conn1 всё ещё ждёт — коллизии по seq быть не должно.
        Assert.False(task1.IsCompleted);

        // ACK для conn1 завершает и её.
        conn1.ReceiveBuffer.Write(BuildAckFrame(0));
        handler.ProcessBuffer(conn1);

        Assert.True(await task1);
    }

    [Fact]
    public async Task FailPendingAcks_CompletesWaitingCommandWithFalse()
    {
        BinaryProtocolHandler handler = CreateHandler();
        var transport = new FakeTransport();
        ShuttleConnection conn = CreateConnection(transport, "192.168.1.131");

        Task<bool> task = handler.SendCommandAsync(conn, ShuttleCommand.Stop, 0, 0, CancellationToken.None, 10000);

        Assert.Single(conn.AckWaiters);

        conn.FailPendingAcks();

        Assert.False(await task);
        Assert.Empty(conn.AckWaiters);
    }

    [Fact]
    public async Task AckWithRejectedResult_CompletesCommandWithFalse()
    {
        BinaryProtocolHandler handler = CreateHandler();
        var transport = new FakeTransport();
        ShuttleConnection conn = CreateConnection(transport, "192.168.1.131");

        Task<bool> task = handler.SendCommandAsync(conn, ShuttleCommand.Stop, 0, 0, CancellationToken.None, 10000);

        conn.ReceiveBuffer.Write(BuildAckFrame(0, AckResult.ACK_REJECTED));
        handler.ProcessBuffer(conn);

        Assert.False(await task);
    }

    [Fact]
    public async Task AllocateSeq_SkipsSeqsStillAwaitingAck()
    {
        BinaryProtocolHandler handler = CreateHandler();
        var transport = new FakeTransport();
        ShuttleConnection conn = CreateConnection(transport, "192.168.1.131");

        // Ручная эмуляция: seq 1 уже занят ожидающим ACK.
        conn.AckWaiters[1] = new TaskCompletionSource<bool>();

        byte allocated = conn.AllocateSeq();

        Assert.NotEqual((byte)1, allocated);
    }

    [Fact]
    public async Task TwoSequentialCommandsOnSameConnection_GetDifferentSeqs()
    {
        BinaryProtocolHandler handler = CreateHandler();
        var transport = new FakeTransport();
        ShuttleConnection conn = CreateConnection(transport, "192.168.1.131");

        Task<bool> first = handler.SendCommandAsync(conn, ShuttleCommand.Stop, 0, 0, CancellationToken.None, 10000);
        Task<bool> second = handler.SendCommandAsync(conn, ShuttleCommand.Reset, 0, 0, CancellationToken.None, 10000);

        Assert.NotEqual(transport.Written[0][4], transport.Written[1][4]);

        conn.ReceiveBuffer.Write(BuildAckFrame(transport.Written[0][4]));
        conn.ReceiveBuffer.Write(BuildAckFrame(transport.Written[1][4]));
        handler.ProcessBuffer(conn);

        Assert.True(await first);
        Assert.True(await second);
    }
}
