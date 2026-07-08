using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using ShuttleManager.Shared.Interfaces;
using ShuttleManager.Shared.Models;
using ShuttleManager.Shared.Models.Messages;
using ShuttleManager.Shared.Models.Protocol;
using ShuttleManager.Shared.Services.Enums;
using ShuttleManager.Shared.Services.ShuttleClient.Command;
using ShuttleManager.Shared.Services.ShuttleClient.Config;
using ShuttleManager.Shared.Services.ShuttleClient.Helpers;

namespace ShuttleManager.Shared.Services.ShuttleClient.BinaryService;

public class BinaryProtocolHandler : IShuttleProtocolHandler
{
    private const byte PROTOCOL_SYNC_1_V2 = 0xBB;
    private const byte PROTOCOL_SYNC_2_V2 = 0xCC;
    private const int MAX_PAYLOAD_SIZE = 120;

    private readonly ProtocolCallbacks _callbacks;
    private readonly ConcurrentDictionary<byte, TaskCompletionSource<bool>> _ackWaiters;
    private readonly object _lock;

    public ShuttleProtocolType Protocol => ShuttleProtocolType.Binary;

    public BinaryProtocolHandler(
        ProtocolCallbacks callbacks,
        ConcurrentDictionary<byte, TaskCompletionSource<bool>> ackWaiters,
        object hubLock)
    {
        _callbacks = callbacks;
        _ackWaiters = ackWaiters;
        _lock = hubLock;
    }

    // Обработка входящего буфера
    public void ProcessBuffer(ShuttleConnection connection)
    {
        byte[] data = connection.ReceiveBuffer.ToArray();
        int offset = 0;
        bool processedAny = false;

        while (offset < data.Length)
        {
            int syncIndex = -1;
            for (int i = offset; i < data.Length - 1; i++)
            {
                if (data[i] == PROTOCOL_SYNC_1_V2 && data[i + 1] == PROTOCOL_SYNC_2_V2)
                {
                    syncIndex = i;
                    break;
                }
            }

            if (syncIndex == -1)
                break;

            if (data.Length - syncIndex < 6)
            {
                offset = syncIndex;
                break;
            }

            byte msgId = data[syncIndex + 2];
            byte targetId = data[syncIndex + 3];
            byte seq = data[syncIndex + 4];
            byte payloadLength = data[syncIndex + 5];

            if (payloadLength > MAX_PAYLOAD_SIZE)
            {
                offset = syncIndex + 2;
                processedAny = true;
                continue;
            }

            int totalFrameSize = 6 + payloadLength + 2;
            if (data.Length - syncIndex < totalFrameSize)
            {
                offset = syncIndex;
                break;
            }

            ushort receivedCrc = BinaryPrimitives.ReadUInt16LittleEndian(
                data.AsSpan(syncIndex + 6 + payloadLength, 2));
            ushort calculatedCrc = Crc16Ccitt(data.AsSpan(syncIndex, 6 + payloadLength));

            if (receivedCrc == calculatedCrc)
            {
                var payload = data.AsSpan(syncIndex + 6, payloadLength);
                HandleBinaryMessage(connection, (MsgID)msgId, payload, seq);
                offset = syncIndex + totalFrameSize;
                processedAny = true;
            }
            else
            {
                offset = syncIndex + 2;
                processedAny = true;
            }
        }

        if (processedAny)
        {
            connection.ReceiveBuffer.SetLength(0);
            if (offset < data.Length)
                connection.ReceiveBuffer.Write(data, offset, data.Length - offset);
        }
    }

    // Обработка сообщений Binary
    private void HandleBinaryMessage(
        ShuttleConnection connection,
        MsgID msgId,
        ReadOnlySpan<byte> payload,
        byte seq)
    {
        switch (msgId)
        {
            case MsgID.MSG_HEARTBEAT:
                Emit(connection, new TelemetryMessage { Data = MemoryMarshal.Read<TelemetryPacket>(payload) });
                break;

            case MsgID.MSG_SENSORS:
                Emit(connection, new SensorMessage { Data = MemoryMarshal.Read<SensorPacket>(payload) });
                break;

            case MsgID.MSG_STATS:
                Emit(connection, new StatsMessage { Data = MemoryMarshal.Read<StatsPacket>(payload) });
                break;

            case MsgID.MSG_LOG:
                Emit(connection, new RawLogMessage { Level = (LogLevel)payload[0], Text = Encoding.UTF8.GetString(payload.Slice(1)) });
                break;

            case MsgID.MSG_CONFIG_SET:
            case MsgID.MSG_CONFIG_GET:
            case MsgID.MSG_CONFIG_REP:
                Emit(connection, new ConfigMessage { Data = MemoryMarshal.Read<ConfigPacket>(payload) });
                break;

            case MsgID.MSG_CONFIG_SYNC_REP:
                Emit(connection, new FullConfigMessage { Data = MemoryMarshal.Read<FullConfigPacket>(payload) });
                break;

            case MsgID.MSG_ACK:
                Emit(connection, HandleAck(payload));
                break;

            case MsgID.MSG_LINK_HEALTH:
                Emit(connection, new LinkHealthMessage { Data = MemoryMarshal.Read<LinkHealthPacket>(payload) });
                break;

            case MsgID.MSG_REQ_LINK_HEALTH:
                // Request only — no payload, nothing to emit
                break;

            case MsgID.MSG_ACK_TELEM:
                HandleAckTelem(connection, payload);
                break;

            case MsgID.MSG_BMS_EXT:
                Emit(connection, new BmsExtMessage { Data = MemoryMarshal.Read<BmsExtPacket>(payload) });
                break;

            default:
                // Unknown msgID — silently ignore (NЕ создаем новых MsgID!)
                break;
        }
    }

    private void Emit(ShuttleConnection connection, ShuttleMessageBase message)
    {
        _callbacks.OnMessage?.Invoke(connection.IpAddress, message);
    }

    // Обработка ACK_TELEM (compound: AckPacket + TelemetryPacket)
    private void HandleAckTelem(ShuttleConnection connection, ReadOnlySpan<byte> payload)
    {
        var ackTelem = MemoryMarshal.Read<AckTelemPacket>(payload);

        // Process the ACK part
        if (_ackWaiters.TryRemove(ackTelem.Ack.RefSeq, out var tcs))
            tcs.TrySetResult(ackTelem.Ack.Result == AckResult.ACK_OK);

        // Emit both messages
        Emit(connection, new AckMessage { Data = ackTelem.Ack });
        Emit(connection, new TelemetryMessage { Data = ackTelem.Telemetry });
    }

    // Обработка ACK
    private AckMessage HandleAck(ReadOnlySpan<byte> payload)
    {
        var ackData = MemoryMarshal.Read<AckPacket>(payload);
        if (_ackWaiters.TryRemove(ackData.RefSeq, out var tcs))
            tcs.TrySetResult(ackData.Result == 0);

        return new AckMessage { Data = ackData };
    }

    // CRC16 CCITT
    private static ushort Crc16Ccitt(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (byte b in data)
        {
            crc ^= (ushort)(b << 8);
            for (int i = 0; i < 8; i++)
            {
                if ((crc & 0x8000) != 0)
                    crc = (ushort)((crc << 1) ^ 0x1021);
                else
                    crc <<= 1;
            }
        }

        return crc;
    }

    private async Task<bool> SendPacketAsync<TPayload>(
        ShuttleConnection connection,
        MsgID msgId,
        TPayload payload,
        int timeoutMs = 1000)
        where TPayload : struct
    {
        if (connection.Transport == null)
            return false;

        byte seq = connection.NextSeq++;

        int payloadSize = Marshal.SizeOf<TPayload>();

        const int headerSize = 6;
        const int crcSize = 2;

        int frameSize = headerSize + payloadSize + crcSize;

        byte[] frame = new byte[frameSize];

        // Header
        frame[0] = PROTOCOL_SYNC_1_V2;
        frame[1] = PROTOCOL_SYNC_2_V2;
        frame[2] = (byte)msgId;
        frame[3] = ProtocolConstants.TARGET_ID_NONE;
        frame[4] = seq;
        frame[5] = (byte)payloadSize;

        // Payload
        MemoryMarshal.Write(frame.AsSpan(headerSize, payloadSize), in payload);

        // CRC
        ushort crc = Crc16Ccitt(frame.AsSpan(0, headerSize + payloadSize));
        BinaryPrimitives.WriteUInt16LittleEndian(
            frame.AsSpan(headerSize + payloadSize, 2),
            crc);

        try
        {
            var tcs = new TaskCompletionSource<bool>();
            _ackWaiters[seq] = tcs;

            var cts = new CancellationTokenSource(timeoutMs);

            cts.Token.Register(() =>
            {
                if (_ackWaiters.TryRemove(seq, out var pending))
                    pending.TrySetResult(false);
            });

            Debug.WriteLine($"command {frame}");
            await connection.Transport.WriteAsync(frame, CancellationToken.None);
            await connection.Transport.FlushAsync(CancellationToken.None);

            return await tcs.Task;
        }
        catch
        {
            _ackWaiters.TryRemove(seq, out _);
            return false;
        }
    }

    public Task<bool> SendConfigSetAsync(
        ShuttleConnection connection,
        ConfigParamID param,
        int value,
        int timeoutMs = 1000)
    {
        var packet = new ConfigPacket
        {
            ParamID = (byte)param,
            Value = value,
        };

        return SendPacketAsync(connection, MsgID.MSG_CONFIG_SET, packet, timeoutMs);
    }

    public Task<bool> SendCommandAsync(
        ShuttleConnection connection,
        ShuttleCommand cmd,
        int arg1,
        int arg2,
        CancellationToken ct,
        int timeoutMs = 1000)
    {
        var command = BinaryCommandMapper.Map(cmd);

        if (cmd == ShuttleCommand.ManualCommand)
        {
            var type = (CmdType)arg1;

            var packet = new SimpleCmdPacket { CmdType = (byte)type };
            return SendPacketAsync(connection, MsgID.MSG_CMD_SIMPLE, packet, timeoutMs);
        }

        if (arg1 == 0 && arg2 == 0)
        {
            var packet = new SimpleCmdPacket { CmdType = (byte)command };
            return SendPacketAsync(connection, MsgID.MSG_CMD_SIMPLE, packet, timeoutMs);
        }
        else
        {
            var packet = new ParamCmdPacket { CmdType = (byte)command, Arg = arg1 };
            return SendPacketAsync(connection, MsgID.MSG_CMD_WITH_ARG, packet, timeoutMs);
        }
    }

    public Task<bool> SendConfigAsync(ShuttleConnection connection, ShuttleConfigCommand param, int value, int timeoutMs = 1000)
    {
        var cmd = BinaryConfigMapper.Map(param);
        return SendConfigSetAsync(connection, cmd, value, timeoutMs);
    }

    public Task<bool> SendDateTimeAsync(ShuttleConnection connection, DateTime utcTime, int timeoutMs = 1000)
    {
        var packet = new DateTimePacket
        {
            Year = (byte)(utcTime.Year - 2000),
            Month = (byte)utcTime.Month,
            Day = (byte)utcTime.Day,
            Hour = (byte)utcTime.Hour,
            Minute = (byte)utcTime.Minute,
            Second = (byte)utcTime.Second,
        };

        return SendPacketAsync(connection, MsgID.MSG_SET_DATETIME, packet, timeoutMs);
    }

    public Task<bool> SendManualCommandAsync(ShuttleConnection connection, string rawCommand, CancellationToken ct, int timeoutMs = 1000)
    {
        if (Enum.TryParse(rawCommand, true, out CmdType result))
        {
            // Формируем тот же пакет, что и в SendCommandAsync для ManualCommand
            var packet = new SimpleCmdPacket { CmdType = (byte)result };
            return SendPacketAsync(connection, MsgID.MSG_CMD_SIMPLE, packet, timeoutMs);
        }

        return Task.FromResult(false);
    }

    public Task<bool> RequestFullConfigAsync(
    ShuttleConnection connection,
    int timeoutMs = 1000)
    {
        return SendRequestAsync(connection, MsgID.MSG_CONFIG_SYNC_REQ, default(EmptyPacket));
    }

    public struct EmptyPacket
    {
    }

    private async Task<bool> SendRequestAsync<TPayload>(
    ShuttleConnection connection,
    MsgID msgId,
    TPayload payload)
    where TPayload : struct
    {
        if (connection.Transport == null)
            return false;

        byte seq = connection.NextSeq++;

        int payloadSize = Marshal.SizeOf<TPayload>();
        const int headerSize = 6;
        const int crcSize = 2;
        int frameSize = headerSize + payloadSize + crcSize;

        byte[] frame = new byte[frameSize];

        // Header
        frame[0] = PROTOCOL_SYNC_1_V2;
        frame[1] = PROTOCOL_SYNC_2_V2;
        frame[2] = (byte)msgId;
        frame[3] = ProtocolConstants.TARGET_ID_NONE;
        frame[4] = seq;
        frame[5] = (byte)payloadSize;

        // Payload
        MemoryMarshal.Write(frame.AsSpan(headerSize, payloadSize), in payload);

        // CRC
        ushort crc = Crc16Ccitt(frame.AsSpan(0, headerSize + payloadSize));
        BinaryPrimitives.WriteUInt16LittleEndian(
            frame.AsSpan(headerSize + payloadSize, 2),
            crc);

        try
        {
            await connection.Transport.WriteAsync(frame, CancellationToken.None);
            await connection.Transport.FlushAsync(CancellationToken.None);
            return true; // пакет отправлен, не ждём ACK
        }
        catch
        {
            return false;
        }
    }
}