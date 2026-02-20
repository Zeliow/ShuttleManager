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
                // 1. Look for Sync (0xAA 0x55)
                int syncIndex = -1;
                for (int i = offset; i < data.Length - 1; i++)
                {
                    if (data[i] == 0xAA && data[i + 1] == 0x55)
                    {
                        syncIndex = i;
                        break;
                    }
                }

                // 2. Look for Newline (legacy text support)
                int newlineIndex = Array.IndexOf(data, (byte)'\n', offset);

                // Priority: Binary Frame if Sync exists and (no newline OR Sync is before newline)
                if (syncIndex != -1 && (newlineIndex == -1 || syncIndex < newlineIndex))
                {
                    // Check Header Size (6 bytes)
                    if (data.Length - syncIndex < 6)
                    {
                        // Not enough data for header, keep buffer from syncIndex
                        if (syncIndex > offset) processedAny = true;
                        offset = syncIndex;
                        break; // Need more data
                    }

                    // Read Header
                    // Sync1(1), Sync2(1), Length(2), Seq(1), MsgID(1)
                    ushort payloadLength = BinaryPrimitives.ReadUInt16LittleEndian(new ReadOnlySpan<byte>(data, syncIndex + 2, 2));

                    int totalFrameSize = 6 + payloadLength + 2; // Header + Payload + CRC

                    if (data.Length - syncIndex < totalFrameSize)
                    {
                        // Not enough data for full frame
                        if (syncIndex > offset) processedAny = true;
                        offset = syncIndex;
                        break; // Need more data
                    }

                    // Validate CRC
                    ushort receivedCrc = BinaryPrimitives.ReadUInt16BigEndian(new ReadOnlySpan<byte>(data, syncIndex + 6 + payloadLength, 2));
                    ushort calculatedCrc = Crc16Ccitt(new ReadOnlySpan<byte>(data, syncIndex, 6 + payloadLength));

                    if (receivedCrc == calculatedCrc)
                    {
                        // Valid Frame
                        byte seq = data[syncIndex + 4];
                        byte msgId = data[syncIndex + 5];
                        var payload = new ReadOnlySpan<byte>(data, syncIndex + 6, payloadLength);

                        HandleBinaryMessage(connection, (MsgID)msgId, payload, seq);

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
            MemoryMarshal.Write(frame.AsSpan(6, payloadSize), in cmdPacket);

            // CRC
            ushort crc = Crc16Ccitt(new ReadOnlySpan<byte>(frame, 0, 6 + payloadSize));
            BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(6 + payloadSize, 2), crc);

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
            MemoryMarshal.Write(frame.AsSpan(6, payloadSize), in cfgPacket);

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