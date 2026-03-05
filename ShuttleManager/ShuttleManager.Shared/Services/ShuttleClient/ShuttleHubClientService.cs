using ShuttleManager.Shared.Models;
using ShuttleManager.Shared.Models.Protocol;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace ShuttleManager.Shared.Services.ShuttleClient
{
    public class ShuttleHubClientService : IShuttleHubClientService, IDisposable
    {
        public event Action<string, ShuttleMessageBase>? LogReceived;

        public event Action<string, int>? Connected;

        public event Action<string>? Disconnected;

        private readonly Dictionary<string, ShuttleConnection> _connections = [];
        private readonly ConcurrentDictionary<byte, TaskCompletionSource<bool>> _ackWaiters = new();
        private readonly object _lock = new();

        private class ShuttleConnection
        {
            public TcpClient? TcpClient { get; set; }
            public Stream? NetworkStream { get; set; }
            public CancellationTokenSource? ReceiveCts { get; set; }
            public Task? ReceiveTask { get; set; }
            public int ShuttleId { get; set; } = -1;
            public string IpAddress { get; set; } = string.Empty;
            public readonly MemoryStream ReceiveBuffer = new();
            public byte NextSeq { get; set; } = 0;
        }

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
                        IsConnected = kvp.Value.TcpClient?.Connected == true,
                    });
                }
                return infos;
            }
        }

        public ConnectedShuttleInfo? GetShuttleInfo(string ipAddress)
        {
            lock (_lock)
            {
                if (_connections.TryGetValue(ipAddress, out var conn))
                {
                    return new ConnectedShuttleInfo
                    {
                        IpAddress = conn.IpAddress,
                        IsConnected = conn.TcpClient?.Connected == true,
                        ShuttleId = conn.ShuttleId
                    };
                }
            }
            return null;
        }

        public async Task ConnectToShuttleAsync(string ipAddress, int port)
        {
            lock (_lock) { } // Keeping existing lock pattern

            var connection = new ShuttleConnection { IpAddress = ipAddress };

            try
            {
                Debug.WriteLine("Старт TCP контакта для прямого подключнения");
                OnLogReceived(connection.IpAddress, new RawLogMessage { Level = LogLevel.LOG_INFO, Text = "Connecting..." });

                connection.TcpClient = new TcpClient();
                await connection.TcpClient.ConnectAsync(ipAddress, port);

                connection.TcpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                connection.TcpClient.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 60);
                connection.TcpClient.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 60);
                connection.TcpClient.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 1);

                connection.NetworkStream = connection.TcpClient.GetStream();

                OnConnected(ipAddress, connection.ShuttleId);

                connection.ReceiveCts = new CancellationTokenSource();
                connection.ReceiveTask = Task.Run(async () => await ReceiveLoopAsync(connection, connection.ReceiveCts.Token), connection.ReceiveCts.Token);

                lock (_lock)
                {
                    _connections[ipAddress] = connection;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ShuttleHubClientService] Ошибка подключения к {ipAddress}: {ex.Message}");
                await InternalDisconnectAsync(ipAddress);
            }
        }

        private async Task ReceiveLoopAsync(ShuttleConnection connection, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[1024];
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (connection.NetworkStream == null) break;

                    int bytesRead = await connection.NetworkStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                    if (bytesRead == 0)
                    {
                        Debug.WriteLine($"[ShuttleHubClientService] Соединение с {connection.IpAddress} закрыто сервером (0 bytes).");
                        await InternalDisconnectAsync(connection.IpAddress);
                        break;
                    }

                    connection.ReceiveBuffer.Write(buffer, 0, bytesRead);
                    ProcessBuffer(connection);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ShuttleHubClientService] Ошибка приёма от {connection.IpAddress}: {ex.Message}");
                    await InternalDisconnectAsync(connection.IpAddress);
                    break;
                }
            }
            Debug.WriteLine($"[ShuttleHubClientService] ReceiveLoop завершён для {connection.IpAddress}");
        }

        private void ProcessBuffer(ShuttleConnection connection)
        {
            byte[] data = connection.ReceiveBuffer.ToArray();
            int offset = 0;
            bool processedAny = false;

            while (offset < data.Length)
            {
                // 1. Look for Sync (0xBB 0xCC) - new protocol
                int syncIndex = -1;
                for (int i = offset; i < data.Length - 1; i++)
                {
                    if (data[i] == 0xBB && data[i + 1] == 0xCC)
                    {
                        syncIndex = i;
                        break;
                    }
                }

                // 2. Look for old protocol Sync (0xAA 0x55) - fallback
                int oldSyncIndex = -1;
                for (int i = offset; i < data.Length - 1; i++)
                {
                    if (data[i] == 0xAA && data[i + 1] == 0x55)
                    {
                        oldSyncIndex = i;
                        break;
                    }
                }

                // 3. Look for Newline (legacy text support)
                int newlineIndex = Array.IndexOf(data, (byte)'\n', offset);

                // Priority: New protocol Binary Frame if Sync exists and (no newline OR Sync is before newline)
                if (syncIndex != -1 && (newlineIndex == -1 || syncIndex < newlineIndex))
                {
                    // Check Header Size (6 bytes for new protocol)
                    if (data.Length - syncIndex < 6)
                    {
                        // Not enough data for header, keep buffer from syncIndex
                        if (syncIndex > offset) processedAny = true;
                        offset = syncIndex;
                        break; // Need more data
                    }

                    // Read Header
                    // Sync1(1), Sync2(1), MsgID(1), TargetID(1), Seq(1), Length(1)
                    byte payloadLength = data[syncIndex + 5];

                    int totalFrameSize = 6 + payloadLength + 2; // Header + Payload + CRC

                    if (data.Length - syncIndex < totalFrameSize)
                    {
                        // Not enough data for full frame
                        if (syncIndex > offset) processedAny = true;
                        offset = syncIndex;
                        break; // Need more data
                    }

                    // Validate CRC
                    ushort receivedCrc = (ushort)(data[syncIndex + 6 + payloadLength] | (data[syncIndex + 6 + payloadLength + 1] << 8));
                    ushort calculatedCrc = ProtocolUtils.CalcCRC16(new ReadOnlySpan<byte>(data, syncIndex, 6 + payloadLength));

                    if (receivedCrc == calculatedCrc)
                    {
                        // Valid Frame
                        byte msgId = data[syncIndex + 2];
                        byte seq = data[syncIndex + 4];
                        var payload = new ReadOnlySpan<byte>(data, syncIndex + 6, payloadLength);

                        HandleNewProtocolBinaryMessage(connection, (MsgID)msgId, payload, seq);

                        offset = syncIndex + totalFrameSize;
                        processedAny = true;
                    }
                    else
                    {
                        // Invalid CRC - skip sync bytes and try finding next sync
                        Debug.WriteLine($"[ShuttleHubClientService] CRC Mismatch from {connection.IpAddress}");
                        offset = syncIndex + 2;
                        processedAny = true;
                    }
                }
                // Fallback to old protocol
                else if (oldSyncIndex != -1 && (newlineIndex == -1 || oldSyncIndex < newlineIndex))
                {
                    // Check Header Size (6 bytes for old protocol)
                    if (data.Length - oldSyncIndex < 6)
                    {
                        // Not enough data for header, keep buffer from oldSyncIndex
                        if (oldSyncIndex > offset) processedAny = true;
                        offset = oldSyncIndex;
                        break; // Need more data
                    }

                    // Read Header
                    // Sync1(1), Sync2(1), Length(2), Seq(1), MsgID(1)
                    ushort payloadLengthOld = BinaryPrimitives.ReadUInt16LittleEndian(new ReadOnlySpan<byte>(data, oldSyncIndex + 2, 2));

                    int totalFrameSize = 6 + payloadLengthOld + 2; // Header + Payload + CRC

                    if (data.Length - oldSyncIndex < totalFrameSize)
                    {
                        // Not enough data for full frame
                        if (oldSyncIndex > offset) processedAny = true;
                        offset = oldSyncIndex;
                        break; // Need more data
                    }

                    // Validate CRC
                    ushort receivedCrc = BinaryPrimitives.ReadUInt16LittleEndian(new ReadOnlySpan<byte>(data, oldSyncIndex + 6 + payloadLengthOld, 2));
                    ushort calculatedCrc = Crc16Ccitt(new ReadOnlySpan<byte>(data, oldSyncIndex, 6 + payloadLengthOld));

                    if (receivedCrc == calculatedCrc)
                    {
                        // Valid Frame
                        byte seq = data[oldSyncIndex + 4];
                        byte msgId = data[oldSyncIndex + 5];
                        var payload = new ReadOnlySpan<byte>(data, oldSyncIndex + 6, payloadLengthOld);

                        HandleBinaryMessage(connection, (MsgID)msgId, payload, seq);

                        offset = oldSyncIndex + totalFrameSize;
                        processedAny = true;
                    }
                    else
                    {
                        // Invalid CRC - skip sync bytes and try finding next sync
                        Debug.WriteLine($"[ShuttleHubClientService] Old Protocol CRC Mismatch from {connection.IpAddress}");
                        offset = oldSyncIndex + 2;
                        processedAny = true;
                    }
                }
                //else if (newlineIndex != -1)
                //{
                //    // Found newline before any sync -> Text line
                //    int length = newlineIndex - offset;
                //    if (length > 0)
                //    {
                //        string line = Encoding.UTF8.GetString(data, offset, length).Trim();
                //        if (line.Length > 0 && line[^1] == '\r') line = line[..^1];

                //        if (!string.IsNullOrWhiteSpace(line))
                //        {
                //            OnLogReceived(connection.IpAddress, new RawLogMessage { Level = LogLevel.LOG_INFO, Text = line });
                //        }
                //    }
                //    offset = newlineIndex + 1; // Skip \n
                //    processedAny = true;
                //}
                else
                {
                    // No Sync, No Newline found in remaining data
                    // If buffer is getting huge without sync or newline, we might want to discard
                    // But for now, we wait for more data.
                    break;
                }
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

        private void HandleBinaryMessage(ShuttleConnection connection, MsgID msgId, ReadOnlySpan<byte> payload, byte seq)
        {
            ShuttleMessageBase? message = null;

            switch (msgId)
            {
                case MsgID.MSG_HEARTBEAT:
                    if (payload.Length >= Marshal.SizeOf<TelemetryPacket>())
                        message = new TelemetryMessage { Data = MemoryMarshal.Read<TelemetryPacket>(payload) };
                    break;

                case MsgID.MSG_SENSORS:
                    if (payload.Length >= Marshal.SizeOf<SensorPacket>())
                        message = new SensorMessage { Data = MemoryMarshal.Read<SensorPacket>(payload) };
                    break;

                case MsgID.MSG_STATS:
                    if (payload.Length >= Marshal.SizeOf<StatsPacket>())
                        message = new StatsMessage { Data = MemoryMarshal.Read<StatsPacket>(payload) };
                    break;

                case MsgID.MSG_LOG:
                    if (payload.Length >= 1)
                    {
                        var level = (LogLevel)payload[0];
                        var text = Encoding.UTF8.GetString(payload.Slice(1));
                        message = new RawLogMessage { Level = level, Text = text };
                    }
                    break;

                case MsgID.MSG_CONFIG_SET:
                case MsgID.MSG_CONFIG_GET:
                case MsgID.MSG_CONFIG_REP:
                    if (payload.Length >= Marshal.SizeOf<ConfigPacket>())
                        message = new ConfigMessage { Data = MemoryMarshal.Read<ConfigPacket>(payload) };
                    break;

                case MsgID.MSG_ACK:
                    if (payload.Length >= Marshal.SizeOf<AckPacket>())
                    {
                        var ackData = MemoryMarshal.Read<AckPacket>(payload);
                        message = new AckMessage { Data = ackData };
                        HandleAck(ackData);
                    }
                    break;
            }

            if (message != null)
            {
                OnLogReceived(connection.IpAddress, message);
            }
        }

        private void HandleNewProtocolBinaryMessage(ShuttleConnection connection, MsgID msgId, ReadOnlySpan<byte> payload, byte seq)
        {
            ShuttleMessageBase? message = null;

            switch (msgId)
            {
                case MsgID.MSG_HEARTBEAT:
                    if (payload.Length >= Marshal.SizeOf<TelemetryPacket>())
                        message = new TelemetryMessage { Data = MemoryMarshal.Read<TelemetryPacket>(payload) };
                    break;

                case MsgID.MSG_SENSORS:
                    if (payload.Length >= Marshal.SizeOf<SensorPacket>())
                        message = new SensorMessage { Data = MemoryMarshal.Read<SensorPacket>(payload) };
                    break;

                case MsgID.MSG_STATS:
                    if (payload.Length >= Marshal.SizeOf<StatsPacket>())
                        message = new StatsMessage { Data = MemoryMarshal.Read<StatsPacket>(payload) };
                    break;

                case MsgID.MSG_LOG:
                    if (payload.Length >= 1)
                    {
                        var level = (LogLevel)payload[0];
                        // Extract message from payload starting from index 1
                        int messageLength = Math.Min(payload.Length - 1, ProtocolConstants.MAX_LOG_STRING_LEN);
                        if (messageLength > 0)
                        {
                            var textBytes = payload.Slice(1, messageLength).ToArray();
                            // Find null terminator if present
                            int nullIndex = Array.IndexOf(textBytes, (byte)0);
                            if (nullIndex >= 0)
                            {
                                messageLength = nullIndex;
                                textBytes = payload.Slice(1, messageLength).ToArray();
                            }
                        }
                        var text = Encoding.UTF8.GetString(payload.Slice(1, messageLength));
                        message = new RawLogMessage { Level = level, Text = text };
                    }
                    break;

                case MsgID.MSG_CONFIG_SET:
                case MsgID.MSG_CONFIG_GET:
                case MsgID.MSG_CONFIG_REP:
                    if (payload.Length >= Marshal.SizeOf<ConfigPacket>())
                        message = new ConfigMessage { Data = MemoryMarshal.Read<ConfigPacket>(payload) };
                    break;

                case MsgID.MSG_CONFIG_SYNC_REQ:
                case MsgID.MSG_CONFIG_SYNC_PUSH:
                case MsgID.MSG_CONFIG_SYNC_REP:
                    if (payload.Length >= Marshal.SizeOf<FullConfigPacket>())
                        message = new ConfigMessage { Data = ConvertToConfigPacket(MemoryMarshal.Read<FullConfigPacket>(payload)) };
                    break;

                case MsgID.MSG_CMD_SIMPLE:
                    if (payload.Length >= Marshal.SizeOf<SimpleCmdPacket>())
                    {
                        var cmdPacket = MemoryMarshal.Read<SimpleCmdPacket>(payload);
                        // Process simple command if needed
                    }
                    break;

                case MsgID.MSG_CMD_WITH_ARG:
                    if (payload.Length >= Marshal.SizeOf<ParamCmdPacket>())
                    {
                        var cmdPacket = MemoryMarshal.Read<ParamCmdPacket>(payload);
                        // Process command with argument if needed
                    }
                    break;

                case MsgID.MSG_SET_DATETIME:
                    if (payload.Length >= Marshal.SizeOf<DateTimePacket>())
                    {
                        var dateTimePacket = MemoryMarshal.Read<DateTimePacket>(payload);
                        // Process datetime sync if needed
                    }
                    break;

                case MsgID.MSG_ACK:
                    if (payload.Length >= Marshal.SizeOf<AckPacket>())
                    {
                        var ackData = MemoryMarshal.Read<AckPacket>(payload);
                        message = new AckMessage { Data = ackData };
                        HandleAck(ackData);
                    }
                    break;

                case MsgID.MSG_REQ_HEARTBEAT:
                case MsgID.MSG_REQ_SENSORS:
                case MsgID.MSG_REQ_STATS:
                    // These are request messages, may trigger sending responses
                    break;
            }

            if (message != null)
            {
                OnLogReceived(connection.IpAddress, message);
            }
        }

        // Helper method to convert FullConfigPacket to ConfigPacket for message handling
        private ConfigPacket ConvertToConfigPacket(FullConfigPacket fullConfig)
        {
            // This is a simplified conversion - in practice you might handle FullConfigPacket differently
            return new ConfigPacket { Value = fullConfig.ShuttleNumber, ParamID = (byte)ConfigParamID.CFG_SHUTTLE_NUM };
        }

        private void HandleAck(AckPacket ack)
        {
            if (_ackWaiters.TryRemove(ack.RefSeq, out var tcs))
            {
                if (ack.Result == 0) tcs.TrySetResult(true);
                else tcs.TrySetResult(false);
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
                        crc = (ushort)((crc << 1) ^ 0x1021);
                    else
                        crc <<= 1;
                }
            }
            return crc;
        }

        public async Task<bool> SendCommandToShuttleAsync(string ipAddress, string command, int timeoutMs = 1000)
        {
            // Legacy method wrapper - assumes command string maps to something,
            // but since we moved to binary, we should ideally use SendBinaryCommandAsync.
            // For now, let's just log or ignore if we can't map it.
            // OR: We can send it as a raw text line if the device supports it?
            // The protocol definition implies ONLY binary frames.
            // So we must map string to CmdType if possible.
            // But this method signature is fixed by Interface (which we will update).
            // I'll leave it as a placeholder that fails or tries to map basic commands.

            // NOTE: The UI calls this with "dStop_", etc.
            // I will implement a basic mapping in UI component, but here let's just return false
            // or try to send binary if we can guess.
            // Actually, I should update the Interface to remove this or change it.
            // For now, I'll keep it for compilation compatibility but it won't work with binary protocol.
            Debug.WriteLine($"[ShuttleHubClientService] SendCommandToShuttleAsync(string) is deprecated. Use SendBinaryCommandAsync.");
            return false;
        }

        public async Task<bool> SendNewProtocolCommandAsync(string ipAddress, CmdType cmd, int arg1 = 0, byte targetId = ProtocolConstants.TARGET_ID_NONE, int timeoutMs = 1000)
        {
            var connection = GetConnection(ipAddress);
            if (connection?.NetworkStream == null) return false;

            try
            {
                byte[] packet;
                if (cmd == CmdType.CMD_MOVE_DIST_R || cmd == CmdType.CMD_MOVE_DIST_F || cmd == CmdType.CMD_LONG_UNLOAD_QTY)
                {
                    // Use command with argument
                    packet = PacketBuilder.BuildCommandWithArg(cmd, arg1, targetId, connection.NextSeq++);
                }
                else
                {
                    // Use simple command
                    packet = PacketBuilder.BuildSimpleCommand(cmd, targetId, connection.NextSeq++);
                }

                await connection.NetworkStream.WriteAsync(packet, 0, packet.Length);
                
                // Wait for ACK if needed
                var tcs = new TaskCompletionSource<bool>();
                var registration = _ackWaiters[packet[4]] = tcs; // Using sequence number from packet
                
                using var cts = new CancellationTokenSource(timeoutMs);
                using var reg = cts.Token.Register(() => tcs.TrySetResult(false));
                
                return await tcs.Task;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ShuttleHubClientService] SendNewProtocolCommandAsync error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> SendNewProtocolConfigSetAsync(string ipAddress, ConfigParamID param, int value, byte targetId = ProtocolConstants.TARGET_ID_NONE, int timeoutMs = 1000)
        {
            var connection = GetConnection(ipAddress);
            if (connection?.NetworkStream == null) return false;

            try
            {
                byte[] packet = PacketBuilder.BuildConfigSet(param, value, targetId, connection.NextSeq++);
                await connection.NetworkStream.WriteAsync(packet, 0, packet.Length);
                
                // Wait for ACK if needed
                var tcs = new TaskCompletionSource<bool>();
                var registration = _ackWaiters[packet[4]] = tcs; // Using sequence number from packet
                
                using var cts = new CancellationTokenSource(timeoutMs);
                using var reg = cts.Token.Register(() => tcs.TrySetResult(false));
                
                return await tcs.Task;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ShuttleHubClientService] SendNewProtocolConfigSetAsync error: {ex.Message}");
                return false;
            }
        }

        public async Task<byte[]> RequestFullConfigAsync(string ipAddress, byte targetId = ProtocolConstants.TARGET_ID_NONE, int timeoutMs = 1000)
        {
            var connection = GetConnection(ipAddress);
            if (connection?.NetworkStream == null) return Array.Empty<byte>();

            try
            {
                byte[] packet = PacketBuilder.BuildConfigSyncRequest(targetId, connection.NextSeq++);
                await connection.NetworkStream.WriteAsync(packet, 0, packet.Length);
                
                // We would need to wait for the config response here
                // This requires additional logic to capture the config packet
                // For now, returning empty array
                return Array.Empty<byte>();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ShuttleHubClientService] RequestFullConfigAsync error: {ex.Message}");
                return Array.Empty<byte>();
            }
        }

        public async Task<bool> SendDateTimeSyncAsync(string ipAddress, DateTime dateTime, byte targetId = ProtocolConstants.TARGET_ID_NONE, int timeoutMs = 1000)
        {
            var connection = GetConnection(ipAddress);
            if (connection?.NetworkStream == null) return false;

            try
            {
                byte[] packet = PacketBuilder.BuildDateTimeSync(dateTime, targetId, connection.NextSeq++);
                await connection.NetworkStream.WriteAsync(packet, 0, packet.Length);
                
                // Wait for ACK if needed
                var tcs = new TaskCompletionSource<bool>();
                var registration = _ackWaiters[packet[4]] = tcs; // Using sequence number from packet
                
                using var cts = new CancellationTokenSource(timeoutMs);
                using var reg = cts.Token.Register(() => tcs.TrySetResult(false));
                
                return await tcs.Task;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ShuttleHubClientService] SendDateTimeSyncAsync error: {ex.Message}");
                return false;
            }
        }

        private ShuttleConnection? GetConnection(string ipAddress)
        {
            lock (_lock)
            {
                return _connections.TryGetValue(ipAddress, out var connection) ? connection : null;
            }
        }

        public async Task<bool> SendBinaryCommandAsync(string ipAddress, CmdType cmd, int arg1 = 0, int arg2 = 0, int timeoutMs = 1000)
        {
            ShuttleConnection? connection;
            lock (_lock)
            {
                if (!_connections.TryGetValue(ipAddress, out var conn)) return false;
                connection = conn;
            }

            if (connection.NetworkStream == null) return false;

            byte seq = connection.NextSeq++;

            var cmdPacket = new CommandPacket
            {
                CmdType = (byte)cmd,
                Arg1 = arg1,
                Arg2 = arg2
            };

            int payloadSize = Marshal.SizeOf(cmdPacket);
            int frameSize = 6 + payloadSize + 2;
            byte[] frame = new byte[frameSize];

            // Header
            frame[0] = 0xAA;
            frame[1] = 0x55;
            BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(2, 2), (ushort)payloadSize);
            frame[4] = seq;
            frame[5] = (byte)MsgID.MSG_COMMAND;

            // Payload
            MemoryMarshal.Write(frame.AsSpan(6, payloadSize), ref cmdPacket);

            // CRC
            ushort crc = Crc16Ccitt(new ReadOnlySpan<byte>(frame, 0, 6 + payloadSize));
            BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(6 + payloadSize, 2), crc);

            try
            {
                var tcs = new TaskCompletionSource<bool>();
                _ackWaiters[seq] = tcs;

                // Cancel waiter after timeout
                var cts = new CancellationTokenSource(timeoutMs);
                cts.Token.Register(() =>
                {
                    if (_ackWaiters.TryRemove(seq, out var pendingTcs))
                    {
                        pendingTcs.TrySetResult(false); // Timeout
                    }
                });

                await connection.NetworkStream.WriteAsync(frame, 0, frame.Length);
                await connection.NetworkStream.FlushAsync();

                return await tcs.Task;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ShuttleHubClientService] SendBinaryCommandAsync Error: {ex.Message}");
                _ackWaiters.TryRemove(seq, out _);
                return false;
            }
        }

        public async Task<bool> SendConfigSetAsync(string ipAddress, ConfigParamID param, int value, int timeoutMs = 1000)
        {
            ShuttleConnection? connection;
            lock (_lock)
            {
                if (!_connections.TryGetValue(ipAddress, out var conn)) return false;
                connection = conn;
            }

            if (connection.NetworkStream == null) return false;

            byte seq = connection.NextSeq++;

            var cfgPacket = new ConfigPacket
            {
                ParamID = (byte)param,
                Value = value
            };

            int payloadSize = Marshal.SizeOf(cfgPacket);
            int frameSize = 6 + payloadSize + 2;
            byte[] frame = new byte[frameSize];

            // Header
            frame[0] = 0xAA;
            frame[1] = 0x55;
            BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(2, 2), (ushort)payloadSize);
            frame[4] = seq;
            frame[5] = (byte)MsgID.MSG_CONFIG_SET;

            // Payload
            MemoryMarshal.Write(frame.AsSpan(6, payloadSize), ref cfgPacket);

            // CRC
            ushort crc = Crc16Ccitt(new ReadOnlySpan<byte>(frame, 0, 6 + payloadSize));
            BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(6 + payloadSize, 2), crc);

            try
            {
                var tcs = new TaskCompletionSource<bool>();
                _ackWaiters[seq] = tcs;

                var cts = new CancellationTokenSource(timeoutMs);
                cts.Token.Register(() =>
                {
                    if (_ackWaiters.TryRemove(seq, out var pendingTcs))
                    {
                        pendingTcs.TrySetResult(false);
                    }
                });

                await connection.NetworkStream.WriteAsync(frame, 0, frame.Length);
                await connection.NetworkStream.FlushAsync();

                return await tcs.Task;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ShuttleHubClientService] SendConfigSetAsync Error: {ex.Message}");
                _ackWaiters.TryRemove(seq, out _);
                return false;
            }
        }

        public void DisconnectFromShuttle(string ipAddress) => _ = InternalDisconnectAsync(ipAddress);

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

                    connection.NetworkStream?.Close();
                    connection.NetworkStream?.Dispose();
                    connection.NetworkStream = null;

                    connection.TcpClient?.Close();
                    connection.TcpClient = null;

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
                    conn.NetworkStream?.Dispose();
                    conn.TcpClient?.Close();
                    conn.TcpClient?.Dispose();
                }
                _connections.Clear();
            }
        }

        private void OnConnected(string ip, int id) => Connected?.Invoke(ip, id);

        private void OnDisconnected(string ip) => Disconnected?.Invoke(ip);

        private void OnLogReceived(string ip, ShuttleMessageBase msg) => LogReceived?.Invoke(ip, msg);
    }
}