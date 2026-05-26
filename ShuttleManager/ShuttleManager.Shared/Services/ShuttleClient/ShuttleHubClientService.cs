using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using ShuttleManager.Shared.Interfaces;
using ShuttleManager.Shared.Models;
using ShuttleManager.Shared.Models.Messages;
using ShuttleManager.Shared.Services.Enums;
using ShuttleManager.Shared.Services.ShuttleClient.BinaryService;
using ShuttleManager.Shared.Services.ShuttleClient.Helpers;
using ShuttleManager.Shared.Services.ShuttleClient.LegacyService;

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
    // Protocol V2 constants for frame parsing
    private const byte PROTOCOL_SYNC_1_V2 = 0xBB;

    private const byte PROTOCOL_SYNC_2_V2 = 0xCC;
    private const int MAX_PAYLOAD_SIZE = 64; // Maximum reasonable payload size

    private readonly ProtocolCallbacks _protocolCallbacks;
    private readonly IShuttleProtocolHandler _binaryHandler;
    private readonly IShuttleProtocolHandler _legacyHandler;

    public ShuttleHubClientService()
    {
        _protocolCallbacks = new ProtocolCallbacks
        {
            OnMessage = (ip, msg) => OnLogReceived(ip, msg),
        };

        _binaryHandler = new BinaryProtocolHandler(_protocolCallbacks, _ackWaiters, _lock);
        _legacyHandler = new LegacyProtocolHandler(_protocolCallbacks);
    }

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
                            lock (foundDevices)
                                foundDevices.Add(IPAddress.Parse(ip));
                        }
                    }
                    catch (OperationCanceledException)
                    {
                    }
                }
                catch (SocketException)
                {
                }
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

    //механизм подключения к шаттлу
    public async Task ConnectToShuttleAsync(string ipAddress, int port)
    {
        lock (_lock)
        {
        }

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

            if (ipAddress[ipAddress.Length - 1] == '0')
            {
                connection.ShuttleId = shuttleNums[0];
            }
            else
            {
                connection.ShuttleId = shuttleNums[int.Parse(ipAddress.Remove(0, ipAddress.Length - 3)) - 131];
            }

            OnConnected(ipAddress, connection.ShuttleId);

            connection.ReceiveCts = new CancellationTokenSource();
            connection.ReceiveTask = Task.Run(
                async () =>
                await ReceiveLoopAsync(connection, connection.ReceiveCts.Token), connection.ReceiveCts.Token);

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
                if (connection.Transport == null)
                    break;

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

    public async Task<bool> RequestFullConfigAsync(string ip)
    {
        if (!_connections.TryGetValue(ip, out var conn))
            return false;

        if (conn.Handler is not BinaryProtocolHandler binary)
            return false;

        return await binary.RequestFullConfigAsync(conn);
    }

    public void DisconnectFromShuttle(string ipAddress)
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
                catch (OperationCanceledException)
                {
                }
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