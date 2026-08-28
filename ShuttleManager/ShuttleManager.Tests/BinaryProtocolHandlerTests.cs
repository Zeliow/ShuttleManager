using ShuttleManager.Shared.Models.Messages;
using ShuttleManager.Shared.Models.Protocol;
using ShuttleManager.Shared.Services.Enums;
using ShuttleManager.Shared.Services.ShuttleClient;
using ShuttleManager.Shared.Services.ShuttleClient.BinaryService;
using ShuttleManager.Shared.Services.ShuttleClient.Helpers;

namespace ShuttleManager.Tests;

public class BinaryProtocolHandlerTests
{
    private static (BinaryProtocolHandler Handler, List<(string Ip, ShuttleMessageBase Message)> Received) CreateHandler()
    {
        var received = new List<(string Ip, ShuttleMessageBase Message)>();
        var callbacks = new ProtocolCallbacks
        {
            OnMessage = (ip, msg) => received.Add((ip, msg)),
        };

        var handler = new BinaryProtocolHandler(callbacks);

        return (handler, received);
    }

    private static ShuttleConnection CreateConnection()
    {
        return new ShuttleConnection
        {
            IpAddress = "192.168.1.131",
            Protocol = ShuttleProtocolType.Binary,
        };
    }

    private static byte[] BuildTelemetryFrame(byte seq, ushort position)
    {
        var packet = new TelemetryPacket
        {
            CurrentPosition = position,
            BatteryVoltage_mV = 12500,
            BatteryCharge = 90,
            ShuttleStatus = ShuttleState.STATE_IDLE,
        };

        return BinaryFrameCodec.BuildFrame(MsgID.MSG_HEARTBEAT, ProtocolConstants.TARGET_ID_NONE, seq, in packet);
    }

    [Fact]
    public void ProcessBuffer_CompleteFrame_EmitsTelemetryMessage()
    {
        (BinaryProtocolHandler handler, var received) = CreateHandler();
        ShuttleConnection connection = CreateConnection();

        connection.ReceiveBuffer.Write(BuildTelemetryFrame(1, 100));

        handler.ProcessBuffer(connection);

        var message = Assert.Single(received);
        Assert.Equal("192.168.1.131", message.Ip);
        var telemetry = Assert.IsType<TelemetryMessage>(message.Message);
        Assert.Equal(100, telemetry.Data.CurrentPosition);
        Assert.Equal(0, connection.ReceiveBuffer.Length);
    }

    [Fact]
    public void ProcessBuffer_FrameSplitAcrossCalls_EmitsAfterSecondPart()
    {
        (BinaryProtocolHandler handler, var received) = CreateHandler();
        ShuttleConnection connection = CreateConnection();

        byte[] frame = BuildTelemetryFrame(2, 250);
        byte[] first = frame.AsSpan(0, 10).ToArray();
        byte[] second = frame.AsSpan(10).ToArray();

        connection.ReceiveBuffer.Write(first);
        handler.ProcessBuffer(connection);

        Assert.Empty(received);
        Assert.Equal(10, connection.ReceiveBuffer.Length);

        connection.ReceiveBuffer.Write(second);
        handler.ProcessBuffer(connection);

        var message = Assert.Single(received);
        Assert.IsType<TelemetryMessage>(message.Message);
        Assert.Equal(0, connection.ReceiveBuffer.Length);
    }

    [Fact]
    public void ProcessBuffer_TwoFramesInOneBuffer_EmitsBoth()
    {
        (BinaryProtocolHandler handler, var received) = CreateHandler();
        ShuttleConnection connection = CreateConnection();

        byte[] frame1 = BuildTelemetryFrame(3, 111);
        byte[] frame2 = BuildTelemetryFrame(4, 222);
        connection.ReceiveBuffer.Write(frame1);
        connection.ReceiveBuffer.Write(frame2);

        handler.ProcessBuffer(connection);

        Assert.Equal(2, received.Count);
        Assert.Equal(111, Assert.IsType<TelemetryMessage>(received[0].Message).Data.CurrentPosition);
        Assert.Equal(222, Assert.IsType<TelemetryMessage>(received[1].Message).Data.CurrentPosition);
        Assert.Equal(0, connection.ReceiveBuffer.Length);
    }

    [Fact]
    public void ProcessBuffer_CorruptedFrameThenValid_EmitsOnlyValid()
    {
        (BinaryProtocolHandler handler, var received) = CreateHandler();
        ShuttleConnection connection = CreateConnection();

        byte[] badFrame = BuildTelemetryFrame(5, 1);
        badFrame[^1] ^= 0xFF;
        byte[] goodFrame = BuildTelemetryFrame(6, 2);

        connection.ReceiveBuffer.Write(badFrame);
        connection.ReceiveBuffer.Write(goodFrame);

        handler.ProcessBuffer(connection);

        var message = Assert.Single(received);
        Assert.Equal(2, Assert.IsType<TelemetryMessage>(message.Message).Data.CurrentPosition);
        Assert.Equal(0, connection.ReceiveBuffer.Length);
    }

    [Fact]
    public void ProcessBuffer_GarbageBeforeFrame_EmitsFrameAndClearsGarbage()
    {
        (BinaryProtocolHandler handler, var received) = CreateHandler();
        ShuttleConnection connection = CreateConnection();

        byte[] garbage = [0x00, 0x01, 0x02, 0x03];
        byte[] frame = BuildTelemetryFrame(7, 333);

        connection.ReceiveBuffer.Write(garbage);
        connection.ReceiveBuffer.Write(frame);

        handler.ProcessBuffer(connection);

        var message = Assert.Single(received);
        Assert.Equal(333, Assert.IsType<TelemetryMessage>(message.Message).Data.CurrentPosition);
        Assert.Equal(0, connection.ReceiveBuffer.Length);
    }

    [Fact]
    public void ProcessBuffer_UnsupportedMsgId_DoesNotCrash()
    {
        (BinaryProtocolHandler handler, var received) = CreateHandler();
        ShuttleConnection connection = CreateConnection();

        var packet = new SimpleCmdPacket { CmdType = 0x01 };
        byte[] frame = BinaryFrameCodec.BuildFrame((MsgID)0x7F, ProtocolConstants.TARGET_ID_NONE, 8, in packet);

        connection.ReceiveBuffer.Write(frame);

        handler.ProcessBuffer(connection);

        Assert.Empty(received);
        Assert.Equal(0, connection.ReceiveBuffer.Length);
    }
}
