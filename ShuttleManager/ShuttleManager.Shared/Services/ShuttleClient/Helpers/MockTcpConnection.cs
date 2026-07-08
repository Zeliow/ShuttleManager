using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ShuttleManager.Shared.Models.Messages;
using ShuttleManager.Shared.Models.Protocol;
using ShuttleManager.Shared.Services.ShuttleClient;

namespace ShuttleManager.Shared.Services.ShuttleClient.Helpers;

public class MockTcpConnection : ShuttleManager.Shared.Interfaces.ITcpConnection
{
    private readonly MemoryStream _inputStream = new MemoryStream();
    private readonly CancellationTokenSource _cts = new CancellationTokenSource();
    private Task _backgroundTask;
    private byte _seqCounter = 0;

    // Mock Shuttle State
    private ShuttleState _mockState = ShuttleState.STATE_IDLE;
    private ushort _mockPosition = 0;
    private byte _mockBattery = 100;

    public MockTcpConnection()
    {
        _backgroundTask = Task.Run(SimulationLoopAsync);
    }

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            lock (_inputStream)
            {
                if (_inputStream.Length > 0)
                {
                    _inputStream.Position = 0;
                    int toRead = Math.Min(buffer.Length, (int)_inputStream.Length);
                    int bytesRead = _inputStream.Read(buffer.Span.Slice(0, toRead));

                    // Remove read bytes
                    var remaining = new byte[_inputStream.Length - bytesRead];
                    if (remaining.Length > 0)
                    {
                        _inputStream.Position = bytesRead;
                        _inputStream.Read(remaining, 0, remaining.Length);
                    }

                    _inputStream.SetLength(0);
                    if (remaining.Length > 0)
                    {
                        _inputStream.Write(remaining, 0, remaining.Length);
                    }

                    return bytesRead;
                }
            }

            await Task.Delay(50, ct);
        }
    }

    public ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        // Parse incoming frame and send ACK or response
        var span = data.Span;
        if (span.Length >= 6 && span[0] == 0xBB && span[1] == 0xCC)
        {
            byte msgId = span[2];
            byte targetId = span[3];
            byte seq = span[4];
            byte payloadLen = span[5];

            if (span.Length >= 6 + payloadLen + 2)
            {
                Debug.WriteLine($"[Mock] Received MsgID: {msgId}, Seq: {seq}");

                // Reply with ACK to commands
                if (msgId == (byte)MsgID.MSG_CMD_SIMPLE || msgId == (byte)MsgID.MSG_CMD_WITH_ARG ||
                    msgId == (byte)MsgID.MSG_SET_DATETIME || msgId == (byte)MsgID.MSG_CONFIG_SET)
                {
                    SendAck(seq, AckResult.ACK_OK);
                }
                else if (msgId == (byte)MsgID.MSG_CONFIG_SYNC_REQ)
                {
                    SendFullConfig();
                }

                // Change mock state based on command
                if (msgId == (byte)MsgID.MSG_CMD_SIMPLE)
                {
                    byte cmdType = span[6]; // first byte of payload
                    if (cmdType == (byte)CmdType.CMD_STOP)
                    {
                        _mockState = ShuttleState.STATE_IDLE;
                    }
                    else if (cmdType == (byte)CmdType.CMD_MOVE_RIGHT_MAN)
                    {
                        _mockState = ShuttleState.STATE_MOVE_FWD;
                    }
                    else if (cmdType == (byte)CmdType.CMD_MOVE_LEFT_MAN)
                    {
                        _mockState = ShuttleState.STATE_MOVE_REV;
                    }
                }
            }
        }

        return ValueTask.CompletedTask;
    }

    public Task FlushAsync(CancellationToken ct) => Task.CompletedTask;

    public bool IsConnected => true;

    private async Task SimulationLoopAsync()
    {
        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                // Simulate state changes
                if (_mockState == ShuttleState.STATE_MOVE_FWD)
                {
                    _mockPosition += 10;
                }
                else if (_mockState == ShuttleState.STATE_MOVE_REV && _mockPosition > 0)
                {
                    _mockPosition -= 10;
                }

                // Send Telemetry
                var telemetry = new TelemetryPacket
                {
                    ErrorCode = 0,
                    WaringCode = 0,
                    CurrentPosition = _mockPosition,
                    Speed = (ushort)(_mockState == ShuttleState.STATE_MOVE_FWD || _mockState == ShuttleState.STATE_MOVE_REV ? 500 : 0),
                    BatteryVoltage_mV = 12500,
                    StateFlags = 0,
                    ShuttleStatus = _mockState,
                    BatteryCharge = _mockBattery,
                    ShuttleNumber = 1,
                    PalletCount = 0,
                };
                EnqueuePacket(MsgID.MSG_HEARTBEAT, telemetry);

                // Send Sensors (every other time roughly)
                var sensors = new SensorPacket
                {
                    DistanceF = 1000,
                    DistanceR = 1000,
                    DistancePltF = 2000,
                    DistancePltR = 2000,
                    Angle = 0,
                    LifterCurrent = 10,
                    Temperature_dC = 250, // 25.0C
                    HardwareFlags = 0,
                };
                EnqueuePacket(MsgID.MSG_SENSORS, sensors);

                await Task.Delay(1000, _cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void SendAck(byte refSeq, AckResult result)
    {
        var ack = new AckPacket { RefSeq = refSeq, Result = result };
        EnqueuePacket(MsgID.MSG_ACK, ack);
    }

    private void SendFullConfig()
    {
        var config = new FullConfigPacket
        {
            InterPallet = 100,
            ShuttleLen = 1000,
            MaxSpeed = 1000,
            WaitTime = 5,
            MprOffset = 0,
            ChnlOffset = 0,
            ShuttleNumber = 1,
            MinBatt = 20,
            FifoLifo = 0,
            ReverseMode = 0,
        };
        EnqueuePacket(MsgID.MSG_CONFIG_SYNC_REP, config);
    }

    private void EnqueuePacket<T>(MsgID msgId, T payload)
        where T : struct
    {
        int payloadSize = Marshal.SizeOf<T>();
        byte[] frame = new byte[6 + payloadSize + 2];

        frame[0] = 0xBB;
        frame[1] = 0xCC;
        frame[2] = (byte)msgId;
        frame[3] = 0; // Target ID NONE
        frame[4] = _seqCounter++;
        frame[5] = (byte)payloadSize;

        MemoryMarshal.Write(frame.AsSpan(6, payloadSize), in payload);

        ushort crc = Crc16Ccitt(frame.AsSpan(0, 6 + payloadSize));
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(6 + payloadSize, 2), crc);

        lock (_inputStream)
        {
            _inputStream.Write(frame, 0, frame.Length);
        }
    }

    private static ushort Crc16Ccitt(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (byte b in data)
        {
            crc ^= (ushort)(b << 8);
            for (int i = 0; i < 8; i++)
            {
                if ((crc & 0x8000) != 0)
                {
                    crc = (ushort)((crc << 1) ^ 0x1021);
                }
                else
                {
                    crc <<= 1;
                }
            }
        }

        return crc;
    }

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _backgroundTask.Wait(500);
        }
        catch
        {
        }

        _cts.Dispose();
        _inputStream.Dispose();
    }
}
