using System.Buffers.Binary;
using ShuttleManager.Shared.Models.Messages;
using ShuttleManager.Shared.Models.Protocol;
using ShuttleManager.Shared.Services.Enums;
using ShuttleManager.Shared.Services.ShuttleClient;
using ShuttleManager.Shared.Services.ShuttleClient.BinaryService;
using ShuttleManager.Shared.Services.ShuttleClient.Helpers;

namespace ShuttleManager.Tests;

public class ProtocolResilienceTests
{
    private static BinaryProtocolHandler CreateHandler(Action<string, ShuttleMessageBase>? onMessage = null)
    {
        var callbacks = new ProtocolCallbacks
        {
            OnMessage = onMessage ?? ((_, _) => { }),
        };

        return new BinaryProtocolHandler(callbacks);
    }

    private static ShuttleConnection CreateConnection()
    {
        return new ShuttleConnection
        {
            IpAddress = "192.168.1.131",
            Protocol = ShuttleProtocolType.Binary,
        };
    }

    /// <summary>Собирает кадр с корректной CRC, но payload короче, чем ожидает структура.</summary>
    private static byte[] BuildShortPayloadFrame(MsgID msgId, byte seq, int payloadLength, byte filler = 0x55)
    {
        byte[] frame = new byte[6 + payloadLength + 2];
        frame[0] = ProtocolConstants.PROTOCOL_SYNC_1_V2;
        frame[1] = ProtocolConstants.PROTOCOL_SYNC_2_V2;
        frame[2] = (byte)msgId;
        frame[3] = ProtocolConstants.TARGET_ID_NONE;
        frame[4] = seq;
        frame[5] = (byte)payloadLength;
        Array.Fill(frame, filler, 6, payloadLength);

        ushort crc = BinaryFrameCodec.Crc16Ccitt(frame.AsSpan(0, 6 + payloadLength));
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(6 + payloadLength, 2), crc);
        return frame;
    }

    [Fact]
    public void ShortTelemetryPayload_DoesNotCrashAndEmitsNothing()
    {
        int receivedCount = 0;
        BinaryProtocolHandler handler = CreateHandler((_, _) => receivedCount++);
        ShuttleConnection connection = CreateConnection();

        connection.ReceiveBuffer.Write(BuildShortPayloadFrame(MsgID.MSG_HEARTBEAT, 1, 4));

        handler.ProcessBuffer(connection);

        Assert.Equal(0, receivedCount);
        Assert.Equal(0, connection.ReceiveBuffer.Length);
    }

    [Fact]
    public void ShortAckPayload_DoesNotCrash()
    {
        int receivedCount = 0;
        BinaryProtocolHandler handler = CreateHandler((_, _) => receivedCount++);
        ShuttleConnection connection = CreateConnection();

        connection.ReceiveBuffer.Write(BuildShortPayloadFrame(MsgID.MSG_ACK, 2, 1));

        handler.ProcessBuffer(connection);

        Assert.Equal(0, connection.ReceiveBuffer.Length);
    }

    [Fact]
    public void EmptyLogPayload_DoesNotCrash()
    {
        int receivedCount = 0;
        BinaryProtocolHandler handler = CreateHandler((_, _) => receivedCount++);
        ShuttleConnection connection = CreateConnection();

        connection.ReceiveBuffer.Write(BuildShortPayloadFrame(MsgID.MSG_LOG, 3, 0));

        handler.ProcessBuffer(connection);

        Assert.Equal(0, receivedCount);
        Assert.Equal(0, connection.ReceiveBuffer.Length);
    }

    [Fact]
    public void ShortAckTelemPayload_DoesNotCrash()
    {
        int receivedCount = 0;
        BinaryProtocolHandler handler = CreateHandler((_, _) => receivedCount++);
        ShuttleConnection connection = CreateConnection();

        connection.ReceiveBuffer.Write(BuildShortPayloadFrame(MsgID.MSG_ACK_TELEM, 4, 8));

        handler.ProcessBuffer(connection);

        Assert.Equal(0, receivedCount);
        Assert.Equal(0, connection.ReceiveBuffer.Length);
    }

    [Fact]
    public void ThrowingSubscriber_DoesNotKillParsingOfFollowingFrames()
    {
        var received = new List<ShuttleMessageBase>();
        BinaryProtocolHandler handler = CreateHandler(
            (_, msg) =>
            {
                if (msg is TelemetryMessage { Data.CurrentPosition: 111 })
                {
                    throw new InvalidOperationException("bad subscriber");
                }

                received.Add(msg);
            });
        ShuttleConnection connection = CreateConnection();

        byte[] badFrame = BinaryFrameCodec.BuildFrame(
            MsgID.MSG_HEARTBEAT,
            ProtocolConstants.TARGET_ID_NONE,
            5,
            new TelemetryPacket { CurrentPosition = 111 });
        byte[] goodFrame = BinaryFrameCodec.BuildFrame(
            MsgID.MSG_SENSORS,
            ProtocolConstants.TARGET_ID_NONE,
            6,
            new SensorPacket { DistanceF = 222 });

        connection.ReceiveBuffer.Write(badFrame);
        connection.ReceiveBuffer.Write(goodFrame);

        handler.ProcessBuffer(connection);

        var message = Assert.Single(received);
        Assert.Equal(222, Assert.IsType<SensorMessage>(message).Data.DistanceF);
        Assert.Equal(0, connection.ReceiveBuffer.Length);
    }

    [Fact]
    public void ValidFrameAfterShortPayloadFrame_StillParsed()
    {
        int receivedCount = 0;
        BinaryProtocolHandler handler = CreateHandler((_, _) => receivedCount++);
        ShuttleConnection connection = CreateConnection();

        byte[] shortFrame = BuildShortPayloadFrame(MsgID.MSG_HEARTBEAT, 7, 2);
        byte[] goodFrame = BinaryFrameCodec.BuildFrame(
            MsgID.MSG_HEARTBEAT,
            ProtocolConstants.TARGET_ID_NONE,
            8,
            new TelemetryPacket { CurrentPosition = 333 });

        connection.ReceiveBuffer.Write(shortFrame);
        connection.ReceiveBuffer.Write(goodFrame);

        handler.ProcessBuffer(connection);

        Assert.Equal(1, receivedCount);
        Assert.Equal(0, connection.ReceiveBuffer.Length);
    }
}
