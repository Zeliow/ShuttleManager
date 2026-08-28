using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ShuttleManager.Shared.Interfaces;
using ShuttleManager.Shared.Models;
using ShuttleManager.Shared.Models.Messages;
using ShuttleManager.Shared.Models.Protocol;
using ShuttleManager.Shared.Services.Enums;
using ShuttleManager.Shared.Services.ShuttleClient.Command;
using ShuttleManager.Shared.Services.ShuttleClient.Config;
using ShuttleManager.Shared.Services.ShuttleClient.Helpers;
using ProtocolLogLevel = ShuttleManager.Shared.Models.Protocol.LogLevel;

namespace ShuttleManager.Shared.Services.ShuttleClient.BinaryService;

public class BinaryProtocolHandler : IShuttleProtocolHandler
{
    private readonly ProtocolCallbacks _callbacks;
    private readonly ILogger<BinaryProtocolHandler> _logger;

    public ShuttleProtocolType Protocol => ShuttleProtocolType.Binary;

    public BinaryProtocolHandler(ProtocolCallbacks callbacks, ILogger<BinaryProtocolHandler>? logger = null)
    {
        _callbacks = callbacks;
        _logger = logger ?? NullLogger<BinaryProtocolHandler>.Instance;
    }

    // Обработка входящего буфера
    public void ProcessBuffer(ShuttleConnection connection)
    {
        byte[] data = connection.ReceiveBuffer.ToArray();
        int offset = 0;
        bool processedAny = false;

        while (offset < data.Length)
        {
            FrameParseResult result = BinaryFrameCodec.TryParseFrame(data, offset, out ParsedFrame frame, out int nextOffset);

            if (result == FrameParseResult.NoSync)
            {
                break;
            }

            if (result == FrameParseResult.Incomplete)
            {
                offset = nextOffset;
                break;
            }

            if (result == FrameParseResult.Ok)
            {
                try
                {
                    HandleBinaryMessage(connection, (MsgID)frame.MsgId, frame.Payload.Span, frame.Seq);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка обработки сообщения {MsgId} от {Ip}", frame.MsgId, connection.IpAddress);
                }
            }

            offset = nextOffset;
            processedAny = true;
        }

        if (processedAny)
        {
            connection.ReceiveBuffer.SetLength(0);
            if (offset < data.Length)
            {
                connection.ReceiveBuffer.Write(data, offset, data.Length - offset);
            }
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
                if (TryReadStruct(payload, out TelemetryPacket telemetry))
                    Emit(connection, new TelemetryMessage { Data = telemetry });
                break;

            case MsgID.MSG_SENSORS:
                if (TryReadStruct(payload, out SensorPacket sensors))
                    Emit(connection, new SensorMessage { Data = sensors });
                break;

            case MsgID.MSG_STATS:
                if (TryReadStruct(payload, out StatsPacket stats))
                    Emit(connection, new StatsMessage { Data = stats });
                break;

            case MsgID.MSG_LOG:
                if (payload.Length > 0)
                    Emit(connection, new RawLogMessage { Level = (ProtocolLogLevel)payload[0], Text = Encoding.UTF8.GetString(payload.Slice(1)) });
                break;

            case MsgID.MSG_CONFIG_SET:
            case MsgID.MSG_CONFIG_GET:
            case MsgID.MSG_CONFIG_REP:
                if (TryReadStruct(payload, out ConfigPacket config))
                    Emit(connection, new ConfigMessage { Data = config });
                break;

            case MsgID.MSG_CONFIG_SYNC_REP:
                if (TryReadStruct(payload, out FullConfigPacket fullConfig))
                    Emit(connection, new FullConfigMessage { Data = fullConfig });
                break;

            case MsgID.MSG_ACK:
                Emit(connection, HandleAck(connection, payload));
                break;

            case MsgID.MSG_LINK_HEALTH:
                if (TryReadStruct(payload, out LinkHealthPacket linkHealth))
                    Emit(connection, new LinkHealthMessage { Data = linkHealth });
                break;

            case MsgID.MSG_REQ_LINK_HEALTH:
                // Request only — no payload, nothing to emit
                break;

            case MsgID.MSG_ACK_TELEM:
                HandleAckTelem(connection, payload);
                break;

            case MsgID.MSG_BMS_EXT:
                if (TryReadStruct(payload, out BmsExtPacket bmsExt))
                    Emit(connection, new BmsExtMessage { Data = bmsExt });
                break;

            default:
                // Unknown msgID — silently ignore (NЕ создаем новых MsgID!)
                break;
        }
    }

    private static bool TryReadStruct<T>(ReadOnlySpan<byte> payload, out T value)
        where T : struct
    {
        if (payload.Length < Marshal.SizeOf<T>())
        {
            value = default;
            return false;
        }

        value = MemoryMarshal.Read<T>(payload);
        return true;
    }

    private void Emit(ShuttleConnection connection, ShuttleMessageBase message)
    {
        _callbacks.OnMessage?.Invoke(connection.IpAddress, message);
    }

    // Обработка ACK_TELEM (compound: AckPacket + TelemetryPacket)
    private void HandleAckTelem(ShuttleConnection connection, ReadOnlySpan<byte> payload)
    {
        if (!TryReadStruct(payload, out AckTelemPacket ackTelem))
            return;

        // Process the ACK part
        if (connection.AckWaiters.TryRemove(ackTelem.Ack.RefSeq, out var tcs))
            tcs.TrySetResult(ackTelem.Ack.Result == AckResult.ACK_OK);

        // Emit both messages
        Emit(connection, new AckMessage { Data = ackTelem.Ack });
        Emit(connection, new TelemetryMessage { Data = ackTelem.Telemetry });
    }

    // Обработка ACK
    private AckMessage HandleAck(ShuttleConnection connection, ReadOnlySpan<byte> payload)
    {
        if (!TryReadStruct(payload, out AckPacket ackData))
            return new AckMessage();

        if (connection.AckWaiters.TryRemove(ackData.RefSeq, out var tcs))
            tcs.TrySetResult(ackData.Result == 0);

        return new AckMessage { Data = ackData };
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

        byte seq = connection.AllocateSeq();

        byte[] frame = BinaryFrameCodec.BuildFrame(msgId, ProtocolConstants.TARGET_ID_NONE, seq, in payload);

        // RunContinuationsAsynchronously: ACK приходит из receive-потока, продолжение не должно блокировать его.
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.AckWaiters[seq] = tcs;

        try
        {
            _logger.LogDebug("Отправка кадра {MsgId} (seq={Seq}, payload={PayloadSize})", msgId, seq, frame.Length - 8);
            await connection.Transport.WriteAsync(frame, CancellationToken.None);
            await connection.Transport.FlushAsync(CancellationToken.None);
        }
        catch
        {
            connection.AckWaiters.TryRemove(seq, out _);
            return false;
        }

        Task completed = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs));
        if (completed != tcs.Task)
        {
            // Таймаут: убираем waiter, чтобы поздний ACK не утёк в словарь навсегда.
            connection.AckWaiters.TryRemove(seq, out _);
            return false;
        }

        return await tcs.Task;
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

        byte seq = connection.AllocateSeq();

        byte[] frame = BinaryFrameCodec.BuildFrame(msgId, ProtocolConstants.TARGET_ID_NONE, seq, in payload);

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