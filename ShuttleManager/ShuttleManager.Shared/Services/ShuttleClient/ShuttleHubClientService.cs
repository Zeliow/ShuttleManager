using ShuttleManager.Shared.Interfaces;
using ShuttleManager.Shared.Models;
using ShuttleManager.Shared.Models.Protocol;
using ShuttleManager.Shared.Services.Enums;
using ShuttleManager.Shared.Services.ShuttleClient.BinaryService;
using ShuttleManager.Shared.Services.ShuttleClient.Command;
using ShuttleManager.Shared.Services.ShuttleClient.Config;
using ShuttleManager.Shared.Services.ShuttleClient.Helpers;
using ShuttleManager.Shared.Services.ShuttleClient.LegacyService;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace ShuttleManager.Shared.Services.ShuttleClient;

public class ShuttleConnection
{
    public TcpConnection? Transport { get; set; }
    public CancellationTokenSource? ReceiveCts { get; set; }
    public Task? ReceiveTask { get; set; }
    public string ShuttleId { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public readonly MemoryStream ReceiveBuffer = new();
    public byte NextSeq { get; set; } = 0;
    public ShuttleProtocolType Protocol { get; set; } = ShuttleProtocolType.Unknown;
    public IShuttleProtocolHandler? Handler { get; set; }
}

public class ShuttleHubClientService : IShuttleHubClientService, IDisposable
{
    private readonly ProtocolCallbacks _protocolCallbacks;
    private readonly IShuttleProtocolHandler _binaryHandler;
    private readonly IShuttleProtocolHandler _legacyHandler;

    public ShuttleHubClientService()
    {
        _protocolCallbacks = new ProtocolCallbacks
        {
            OnMessage = (ip, msg) => OnLogReceived(ip, msg)
        };

        _binaryHandler = new BinaryProtocolHandler(_protocolCallbacks, _ackWaiters, _lock);
        _legacyHandler = new LegacyProtocolHandler(_protocolCallbacks);
    }

    // Protocol V2 constants for frame parsing
    private const byte PROTOCOL_SYNC_1_V2 = 0xBB;

    private const byte PROTOCOL_SYNC_2_V2 = 0xCC;
    private const int MAX_PAYLOAD_SIZE = 64; // Maximum reasonable payload size

    public event Action<string, ShuttleMessageBase>? LogReceived;

    public event Action<string, string>? Connected;

    public event Action<string>? Disconnected;

    private readonly Dictionary<string, ShuttleConnection> _connections = [];
    private readonly ConcurrentDictionary<byte, TaskCompletionSource<bool>> _ackWaiters = new();
    private readonly object _lock = new();
    private readonly string[] shuttleNums = ["A1", "B2", "C3", "D4", "E5", "F6", "G7", "H8", "I9", "10", "11", "12", "13", "14", "15", "16", "17", "18", "19", "20", "21", "22", "23", "24", "25", "26", "27", "28", "29", "30", "31", "32"];

    public async Task<List<IPAddress>> ScanNetworkAsync(string baseIp, int startIp, int endIp, int port, int timeoutMs = 1000)
    {
        var foundDevices = new List<IPAddress>();
        var tasks = new List<Task>();

        for (int i = startIp; i <= endIp; i++)
        {
            string ip = $"{baseIp}.{i}";
            var task = Task.Run(async () =>
            {
                try
                {
                    using var client = new TcpClient();
                    var cts = new CancellationTokenSource(timeoutMs);
                    try
                    {
                        await client.ConnectAsync(IPAddress.Parse(ip), port, cts.Token);
                        if (client.Connected)
                        {
                            Debug.WriteLine("Старт TCP контакта для валидной точки входа.");
                            lock (foundDevices) foundDevices.Add(IPAddress.Parse(ip));
                        }
                    }
                    catch (OperationCanceledException) { }
                }
                catch (SocketException) { }
            });
            tasks.Add(task);
        }
        await Task.WhenAll(tasks);
        return foundDevices;
    }

    public List<Shuttle> GetConnectedShuttles()
    {
        lock (_lock)
        {
            var infos = new List<Shuttle>();
            foreach (var kvp in _connections)
            {
                infos.Add(new Shuttle
                {
                    IPAddress = kvp.Value.IpAddress,
                    IsConnected = kvp.Value.Transport?.IsConnected == true,
                });
            }
            return infos;
        }
    }

    public async Task ConnectToShuttleAsync(string ipAddress, int port)
    {
        lock (_lock) { } // Keeping existing lock pattern

        var connection = new ShuttleConnection { IpAddress = ipAddress };

        try
        {
            Debug.WriteLine("Старт TCP контакта для прямого подключнения");

            var tcpClient = new TcpClient();
            await tcpClient.ConnectAsync(ipAddress, port);

            tcpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            tcpClient.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 60);
            tcpClient.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 60);
            tcpClient.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 1);
            connection.Transport = new TcpConnection(tcpClient);

            connection.ShuttleId = shuttleNums[(int.Parse(ipAddress.Remove(0, ipAddress.Length - 3)) - 131)];

            OnConnected(ipAddress, connection.ShuttleId);
        }
        catch { }
    }

    private async Task ReceiveLoopAsync(ShuttleConnection connection, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[1024];
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (connection.Transport == null) break;

                int bytesRead = await connection.Transport.ReadAsync(buffer, cancellationToken);
                if (bytesRead == 0)
                {
                    Debug.WriteLine($"[ShuttleHubClientService] Соединение с {connection.IpAddress} закрыто сервером (0 bytes).");
                    await InternalDisconnectAsync(connection.IpAddress);
                    break;
                }

                connection.ReceiveBuffer.Write(buffer, 0, bytesRead);

                if (connection.Protocol == ShuttleProtocolType.Unknown)
                    DetectProtocol(connection);

                connection.Handler?.ProcessBuffer(connection);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ShuttleHubClientService] Ошибка приёма от {connection.IpAddress}: {ex.Message}");
                await InternalDisconnectAsync(connection.IpAddress);
                break;
            }
        }
        Debug.WriteLine($"[ShuttleHubClientService] ReceiveLoop завершён для {connection.IpAddress}");
    }

    private void DetectProtocol(ShuttleConnection connection)
    {
        var data = connection.ReceiveBuffer.ToArray();

        if (data.Length >= 2 &&
            data[0] == PROTOCOL_SYNC_1_V2 &&
            data[1] == PROTOCOL_SYNC_2_V2)
        {
            connection.Protocol = ShuttleProtocolType.Binary;
            connection.Handler = _binaryHandler;
            Debug.WriteLine($"Binary Protocol detected: {connection.IpAddress}");
            return;
        }

        if (data.Any(b => b == '\n'))
        {
            connection.Protocol = ShuttleProtocolType.Legacy;
            connection.Handler = _legacyHandler;
            Debug.WriteLine($"Legacy protocol detected: {connection.IpAddress}");
        }
    }

    public async Task<bool> SendDateTimeAsync(
        string ipAddress,
        DateTime utcTime,
        int timeoutMs = 1000)
    {
        if (!_connections.TryGetValue(ipAddress, out var connection))
            return false;

        if (connection.Handler == null)
            return false;

        return await connection.Handler.SendDateTimeAsync(connection, utcTime, timeoutMs);
    }

    public async Task<bool> SendCommandAsync(
        string ip,
        ShuttleCommand command,
        int arg1 = 0,
        int arg2 = 0)
    {
        if (!_connections.TryGetValue(ip, out var conn))
            return false;

        if (conn.Handler == null)
            return false;

        return await conn.Handler.SendCommandAsync(conn, command, arg1, arg2, CancellationToken.None);
    }

    public async Task<bool> SendConfigAsync(
        string ip,
        ShuttleConfigCommand param,
        int value)
    {
        if (!_connections.TryGetValue(ip, out var connection))
            return false;

        if (connection.Handler == null)
            return false;

        return await connection.Handler.SendConfigAsync(connection, param, value, 1000);
    }

    public async Task<bool> SendManualCommandAsync(string ip, string rawCommand, int timeoutMs = 1000)
    {
        if (!_connections.TryGetValue(ip, out var conn))
            return false;

        if (conn.Handler == null)
            return false;

        return await conn.Handler.SendManualCommandAsync(conn, rawCommand, CancellationToken.None, 1000);
    }

    public async Task DisconnectFromShuttle(string ipAddress)
    {
        _ = InternalDisconnectAsync(ipAddress);
    }

    private async Task InternalDisconnectAsync(string ipAddress)
    {
        ShuttleConnection? connectionToDispose = null;

        lock (_lock)
        {
            if (_connections.TryGetValue(ipAddress, out var connection))
            {
                connectionToDispose = connection;

                connection.ReceiveCts?.Cancel();
                connection.ReceiveCts?.Dispose();
                connection.ReceiveCts = null;

                connection.Transport?.Dispose();
                connection.Transport = null;

                _connections.Remove(ipAddress);
            }
        }

        if (connectionToDispose != null)
        {
            if (connectionToDispose.ReceiveTask != null)
            {
                try
                {
                    await connectionToDispose.ReceiveTask;
                }
                catch (OperationCanceledException) { }
            }
            OnDisconnected(ipAddress);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var kvp in _connections)
            {
                var conn = kvp.Value;
                conn.ReceiveCts?.Cancel();
                conn.ReceiveCts?.Dispose();

                conn.Transport?.Dispose();
                conn.Transport = null;
            }
            _connections.Clear();
        }
    }

    private void OnConnected(string ip, string id) => Connected?.Invoke(ip, id);

    private void OnDisconnected(string ip) => Disconnected?.Invoke(ip);

    private void OnLogReceived(string ip, ShuttleMessageBase msg) => LogReceived?.Invoke(ip, msg);
}

//public async Task<bool> SendCommandToShuttleAsync(string ipAddress, string command, int timeoutMs = 1000)
//{
//    Debug.WriteLine($"[ShuttleHubClientService] SendCommandToShuttleAsync(string) is deprecated. Use SendBinaryCommandAsync.");
//    if (!_connections.TryGetValue(ipAddress, out var connection))
//        return false;

//    if (connection.Protocol == ShuttleProtocolType.Legacy)
//    {
//        return await SendLegacyCommand(connection, command);
//    }

//    return false;
//}

//private async Task<bool> SendLegacyCommand(ShuttleConnection connection, string command)
//{
//    var data = Encoding.UTF8.GetBytes(command + "\n");
//    Debug.WriteLine($"Command: {command}");

//    await connection.Transport!.WriteAsync(data, CancellationToken.None);
//    await connection.Transport!.FlushAsync(CancellationToken.None);

//    return true;
//}

//private void ProcessBuffer(ShuttleConnection connection)
//{
//    switch (connection.Protocol)
//    {
//        case ShuttleProtocolType.Binary:
//            ProcessBinaryBuffer(connection);
//            break;

//        case ShuttleProtocolType.Legacy:
//            ProcessLegacyBuffer(connection);
//            break;
//    }
//}

//private void ProcessLegacyBuffer(ShuttleConnection connection)
//{
//    var data = connection.ReceiveBuffer.ToArray();

//    int start = 0;

//    while (true)
//    {
//        int newline = Array.IndexOf(data, (byte)'\n', start);

//        if (newline < 0)
//            break;

//        int length = newline - start;

//        var line = Encoding.UTF8.GetString(data, start, length).Trim();

//        HandleLegacyLine(connection, line);

//        start = newline + 1;
//    }

//    connection.ReceiveBuffer.SetLength(0);

//    if (start < data.Length)
//        connection.ReceiveBuffer.Write(data, start, data.Length - start);
//}

//private void HandleLegacyLine(ShuttleConnection connection, string line)
//{
//    if (string.IsNullOrWhiteSpace(line)) return;

//    // Парсим строку в один или несколько сообщений
//    var messages = LegacyParser.Parse(line);

//    // Всегда добавляем raw пакет
//    var raw = new RawLogMessage
//    {
//        Level = LogLevel.LOG_INFO,
//        Text = line
//    };

//    messages.Add(raw);

//    // Отправляем все сообщения через OnLogReceived
//    foreach (var msg in messages)
//    {
//        OnLogReceived(connection.IpAddress, msg);
//    }
//}

//private void ProcessBinaryBuffer(ShuttleConnection connection)
//{
//    byte[] data = connection.ReceiveBuffer.ToArray();
//    int offset = 0;
//    bool processedAny = false;

//    while (offset < data.Length)
//    {
//        // 1. Look for Sync (0xBB 0xCC) - Protocol V2
//        int syncIndex = -1;
//        for (int i = offset; i < data.Length - 1; i++)
//        {
//            if (data[i] == PROTOCOL_SYNC_1_V2 && data[i + 1] == PROTOCOL_SYNC_2_V2)
//            {
//                syncIndex = i;
//                break;
//            }
//        }

//        // Priority: Binary Frame if Sync exists
//        if (syncIndex != -1)
//        {
//            // Check Header Size (6 bytes): Sync1(1), Sync2(1), MsgID(1), TargetID(1), Seq(1), Length(1)
//            if (data.Length - syncIndex < 6)
//            {
//                // Not enough data for header, keep buffer from syncIndex
//                if (syncIndex > offset) processedAny = true;
//                offset = syncIndex;
//                break; // Need more data
//            }

//            // Read Header fields - Protocol V2 format
//            byte msgId = data[syncIndex + 2];
//            byte targetId = data[syncIndex + 3];
//            byte seq = data[syncIndex + 4];
//            byte payloadLength = data[syncIndex + 5];

//            // Safety check: ensure payload length is reasonable to avoid memory allocation attacks
//            if (payloadLength > MAX_PAYLOAD_SIZE)
//            {
//                Debug.WriteLine($"[ShuttleHubClientService] Invalid payload length {payloadLength} from {connection.IpAddress}, discarding sync");
//                offset = syncIndex + 2;
//                processedAny = true;
//                continue;
//            }

//            int totalFrameSize = 6 + payloadLength + 2; // Header + Payload + CRC

//            if (data.Length - syncIndex < totalFrameSize)
//            {
//                // Not enough data for full frame
//                if (syncIndex > offset) processedAny = true;
//                offset = syncIndex;
//                break; // Need more data
//            }

//            // Validate CRC
//            ushort receivedCrc = BinaryPrimitives.ReadUInt16LittleEndian(new ReadOnlySpan<byte>(data, syncIndex + 6 + payloadLength, 2));
//            ushort calculatedCrc = Crc16Ccitt(new ReadOnlySpan<byte>(data, syncIndex, 6 + payloadLength));

//            if (receivedCrc == calculatedCrc)
//            {
//                // Valid Frame
//                var payload = new ReadOnlySpan<byte>(data, syncIndex + 6, payloadLength);

//                HandleBinaryMessage(connection, (MsgID)msgId, payload, seq);

//                offset = syncIndex + totalFrameSize;
//                processedAny = true;
//            }
//            else
//            {
//                // Invalid CRC - skip sync bytes and try finding next sync
//                Debug.WriteLine($"[ShuttleHubClientService] CRC Mismatch from {connection.IpAddress}");
//                offset = syncIndex + 2;
//                processedAny = true;
//            }
//        }
//        else
//        {
//            // No Sync found in remaining data
//            break;
//        }
//    }

//    if (processedAny)
//    {
//        connection.ReceiveBuffer.SetLength(0);
//        if (offset < data.Length)
//        {
//            connection.ReceiveBuffer.Write(data, offset, data.Length - offset);
//        }
//    }
//}

//private void HandleBinaryMessage(
//    ShuttleConnection connection,
//    MsgID msgId,
//    ReadOnlySpan<byte> payload, byte seq)
//{
//    ShuttleMessageBase? message = null;

//    switch (msgId)
//    {
//        case MsgID.MSG_HEARTBEAT:
//            if (payload.Length >= Marshal.SizeOf<TelemetryPacket>())
//            {
//                var packet = MemoryMarshal.Read<TelemetryPacket>(payload);
//                message = new TelemetryMessage { Data = packet };

//                // Update shuttle ID from heartbeat if not already set
//                if (connection.ShuttleId == "-1")
//                {
//                    connection.ShuttleId = Convert.ToString(packet.ShuttleNumber);
//                }
//            }
//            break;

//        case MsgID.MSG_SENSORS:
//            if (payload.Length >= Marshal.SizeOf<SensorPacket>())
//                message = new SensorMessage { Data = MemoryMarshal.Read<SensorPacket>(payload) };
//            break;

//        case MsgID.MSG_STATS:
//            if (payload.Length >= Marshal.SizeOf<StatsPacket>())
//                message = new StatsMessage { Data = MemoryMarshal.Read<StatsPacket>(payload) };
//            break;

//        case MsgID.MSG_LOG:
//            if (payload.Length >= 1)
//            {
//                var level = (LogLevel)payload[0];
//                var text = Encoding.UTF8.GetString(payload.Slice(1));
//                message = new RawLogMessage { Level = level, Text = text };
//            }
//            break;

//        case MsgID.MSG_CONFIG_SET:

//        case MsgID.MSG_CONFIG_GET:

//        case MsgID.MSG_CONFIG_REP:
//            if (payload.Length >= Marshal.SizeOf<ConfigPacket>())
//                message = new ConfigMessage { Data = MemoryMarshal.Read<ConfigPacket>(payload) };
//            break;

//        case MsgID.MSG_ACK:
//            if (payload.Length >= Marshal.SizeOf<AckPacket>())
//            {
//                var ackData = MemoryMarshal.Read<AckPacket>(payload);
//                message = new AckMessage { Data = ackData };
//                HandleAck(ackData);
//            }
//            break;
//    }

//    if (message != null)
//    {
//        OnLogReceived(connection.IpAddress, message);
//    }
//}

//private void HandleAck(AckPacket ack)
//{
//    if (_ackWaiters.TryRemove(ack.RefSeq, out var tcs))
//    {
//        if (ack.Result == 0) tcs.TrySetResult(true);
//        else tcs.TrySetResult(false);
//    }
//}

//public Task<bool> SendBinaryCommandAsync(
//    string ipAddress,
//    CmdType cmd,
//    int arg1 = 0,
//    int arg2 = 0,
//    int timeoutMs = 1000)
//{
//    if (arg1 == 0 && arg2 == 0)
//    {
//        var packet = new SimpleCmdPacket
//        {
//            CmdType = (byte)cmd
//        };

//        return SendPacketAsync(ipAddress, MsgID.MSG_CMD_SIMPLE, packet, timeoutMs);
//    }
//    else
//    {
//        var packet = new ParamCmdPacket
//        {
//            CmdType = (byte)cmd,
//            Arg = arg1
//        };

//        return SendPacketAsync(ipAddress, MsgID.MSG_CMD_WITH_ARG, packet, timeoutMs);
//    }
//}

//public Task<bool> SendConfigSetAsync(
//    string ipAddress,
//    ConfigParamID param,
//    int value,
//    int timeoutMs = 1000)
//{
//    var packet = new ConfigPacket
//    {
//        ParamID = (byte)param,
//        Value = value
//    };

//    return SendPacketAsync(ipAddress, MsgID.MSG_CONFIG_SET, packet, timeoutMs);
//}

//private async Task<bool> SendPacketAsync<TPayload>(
//    string ipAddress,
//    MsgID msgId,
//    TPayload payload,
//    int timeoutMs = 1000)
//    where TPayload : struct
//{
//    ShuttleConnection? connection;

//    lock (_lock)
//    {
//        if (!_connections.TryGetValue(ipAddress, out var conn))
//            return false;

//        connection = conn;
//    }

//    if (connection.Transport == null)
//        return false;

//    byte seq = connection.NextSeq++;

//    int payloadSize = Marshal.SizeOf<TPayload>();

//    const int headerSize = 6;
//    const int crcSize = 2;

//    int frameSize = headerSize + payloadSize + crcSize;

//    byte[] frame = new byte[frameSize];

//    // Header
//    frame[0] = PROTOCOL_SYNC_1_V2;
//    frame[1] = PROTOCOL_SYNC_2_V2;
//    frame[2] = (byte)msgId;
//    frame[3] = ProtocolConstants.TARGET_ID_NONE;
//    frame[4] = seq;
//    frame[5] = (byte)payloadSize;

//    // Payload
//    MemoryMarshal.Write(frame.AsSpan(headerSize, payloadSize), in payload);

//    // CRC
//    ushort crc = Crc16Ccitt(frame.AsSpan(0, headerSize + payloadSize));
//    BinaryPrimitives.WriteUInt16LittleEndian(
//        frame.AsSpan(headerSize + payloadSize, 2),
//        crc);

//    try
//    {
//        var tcs = new TaskCompletionSource<bool>();
//        _ackWaiters[seq] = tcs;

//        var cts = new CancellationTokenSource(timeoutMs);

//        cts.Token.Register(() =>
//        {
//            if (_ackWaiters.TryRemove(seq, out var pending))
//                pending.TrySetResult(false);
//        });

//        await connection.Transport.WriteAsync(frame, CancellationToken.None);
//        await connection.Transport.FlushAsync(CancellationToken.None);

//        return await tcs.Task;
//    }
//    catch
//    {
//        _ackWaiters.TryRemove(seq, out _);
//        return false;
//    }
//}

//private static ushort Crc16Ccitt(ReadOnlySpan<byte> data)
//{
//    ushort crc = 0xFFFF;
//    foreach (byte b in data)
//    {
//        crc ^= (ushort)(b << 8);
//        for (int i = 0; i < 8; i++)
//        {
//            if ((crc & 0x8000) != 0)
//                crc = (ushort)((crc << 1) ^ 0x1021);
//            else
//                crc <<= 1;
//        }
//    }
//    return crc;
//}