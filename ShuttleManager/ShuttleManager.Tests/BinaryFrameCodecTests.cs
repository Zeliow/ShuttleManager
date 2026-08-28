using System.Text;
using ShuttleManager.Shared.Models.Protocol;
using ShuttleManager.Shared.Services.ShuttleClient.BinaryService;

namespace ShuttleManager.Tests;

public class BinaryFrameCodecTests
{
    [Fact]
    public void Crc16Ccitt_KnownVector_MatchesReference()
    {
        byte[] data = Encoding.ASCII.GetBytes("123456789");

        ushort crc = BinaryFrameCodec.Crc16Ccitt(data);

        Assert.Equal(0x29B1, crc);
    }

    [Fact]
    public void BuildFrame_ThenParseFrame_Roundtrip()
    {
        var packet = new TelemetryPacket
        {
            ErrorCode = 0x1234,
            WarningCode = 0x5678,
            CurrentPosition = 100,
            Speed = 42,
            BatteryVoltage_mV = 12500,
            StateFlags = 3,
            ShuttleStatus = ShuttleState.STATE_IDLE,
            BatteryCharge = 88,
            ShuttleNumber = 7,
            PalletCount = 2,
        };

        byte[] frame = BinaryFrameCodec.BuildFrame(MsgID.MSG_HEARTBEAT, ProtocolConstants.TARGET_ID_NONE, 5, in packet);

        Assert.Equal(6 + 16 + 2, frame.Length);
        Assert.Equal(ProtocolConstants.PROTOCOL_SYNC_1_V2, frame[0]);
        Assert.Equal(ProtocolConstants.PROTOCOL_SYNC_2_V2, frame[1]);
        Assert.Equal((byte)MsgID.MSG_HEARTBEAT, frame[2]);
        Assert.Equal(5, frame[4]);
        Assert.Equal(16, frame[5]);

        FrameParseResult result = BinaryFrameCodec.TryParseFrame(frame, 0, out ParsedFrame parsed, out int nextOffset);

        Assert.Equal(FrameParseResult.Ok, result);
        Assert.Equal(6 + 16 + 2, nextOffset);
        Assert.Equal((byte)MsgID.MSG_HEARTBEAT, parsed.MsgId);
        Assert.Equal(5, parsed.Seq);
        Assert.Equal(16, parsed.Payload.Length);

        TelemetryPacket parsedPacket = System.Runtime.InteropServices.MemoryMarshal.Read<TelemetryPacket>(parsed.Payload.Span);
        Assert.Equal(0x1234, parsedPacket.ErrorCode);
        Assert.Equal(12500, parsedPacket.BatteryVoltage_mV);
        Assert.Equal(88, parsedPacket.BatteryCharge);
    }

    [Fact]
    public void TryParseFrame_EmptyBuffer_ReturnsNoSync()
    {
        FrameParseResult result = BinaryFrameCodec.TryParseFrame([], 0, out _, out int nextOffset);

        Assert.Equal(FrameParseResult.NoSync, result);
        Assert.Equal(-1, nextOffset);
    }

    [Fact]
    public void TryParseFrame_GarbageOnly_ReturnsNoSync()
    {
        byte[] data = [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08];

        FrameParseResult result = BinaryFrameCodec.TryParseFrame(data, 0, out _, out _);

        Assert.Equal(FrameParseResult.NoSync, result);
    }

    [Fact]
    public void TryParseFrame_TruncatedHeader_ReturnsIncompleteWithSyncOffset()
    {
        byte[] data = [0x00, ProtocolConstants.PROTOCOL_SYNC_1_V2, ProtocolConstants.PROTOCOL_SYNC_2_V2, 0x01, 0x02];

        FrameParseResult result = BinaryFrameCodec.TryParseFrame(data, 0, out _, out int nextOffset);

        Assert.Equal(FrameParseResult.Incomplete, result);
        Assert.Equal(1, nextOffset);
    }

    [Fact]
    public void TryParseFrame_TruncatedBody_ReturnsIncomplete()
    {
        var packet = new TelemetryPacket { BatteryVoltage_mV = 12000 };
        byte[] frame = BinaryFrameCodec.BuildFrame(MsgID.MSG_HEARTBEAT, ProtocolConstants.TARGET_ID_NONE, 1, in packet);
        byte[] truncated = frame.AsSpan(0, frame.Length - 5).ToArray();

        FrameParseResult result = BinaryFrameCodec.TryParseFrame(truncated, 0, out _, out int nextOffset);

        Assert.Equal(FrameParseResult.Incomplete, result);
        Assert.Equal(0, nextOffset);
    }

    [Fact]
    public void TryParseFrame_BadCrc_ReturnsInvalidAndSkipsTwoBytes()
    {
        var packet = new TelemetryPacket { BatteryVoltage_mV = 12000 };
        byte[] frame = BinaryFrameCodec.BuildFrame(MsgID.MSG_HEARTBEAT, ProtocolConstants.TARGET_ID_NONE, 1, in packet);
        frame[^1] ^= 0xFF;

        FrameParseResult result = BinaryFrameCodec.TryParseFrame(frame, 0, out _, out int nextOffset);

        Assert.Equal(FrameParseResult.Invalid, result);
        Assert.Equal(2, nextOffset);
    }

    [Fact]
    public void TryParseFrame_PayloadLengthTooLarge_ReturnsInvalid()
    {
        byte[] data =
        [
            ProtocolConstants.PROTOCOL_SYNC_1_V2,
            ProtocolConstants.PROTOCOL_SYNC_2_V2,
            0x01,
            0x00,
            0x01,
            121,
            0x00,
            0x00,
            0x00,
            0x00,
            0x00,
            0x00,
        ];

        FrameParseResult result = BinaryFrameCodec.TryParseFrame(data, 0, out _, out int nextOffset);

        Assert.Equal(FrameParseResult.Invalid, result);
        Assert.Equal(2, nextOffset);
    }

    [Fact]
    public void TryParseFrame_GarbageBeforeFrame_FindsFrame()
    {
        var packet = new SensorPacket { DistanceF = 250 };
        byte[] frame = BinaryFrameCodec.BuildFrame(MsgID.MSG_SENSORS, ProtocolConstants.TARGET_ID_NONE, 9, in packet);
        byte[] data = [0xDE, 0xAD, 0xBE, 0xEF, .. frame];

        FrameParseResult result = BinaryFrameCodec.TryParseFrame(data, 0, out ParsedFrame parsed, out int nextOffset);

        Assert.Equal(FrameParseResult.Ok, result);
        Assert.Equal((byte)MsgID.MSG_SENSORS, parsed.MsgId);
        Assert.Equal(9, parsed.Seq);
        Assert.Equal(data.Length, nextOffset);
    }
}
