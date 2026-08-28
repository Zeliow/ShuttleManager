using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ShuttleManager.Shared.Interfaces;
using ShuttleManager.Shared.Models;
using ShuttleManager.Shared.Models.Messages;
using ShuttleManager.Shared.Models.Protocol;
using ShuttleManager.Shared.Services.Enums;
using ShuttleManager.Shared.Services.ShuttleClient.BinaryService;
using ShuttleManager.Shared.Services.ShuttleClient.Helpers;
using ShuttleManager.Shared.Services.ShuttleClient.LegacyService;

namespace ShuttleManager.Shared.Services.ShuttleClient;

public class ShuttleConnection
{
    public ITransport? Transport { get; set; }
    public CancellationTokenSource? ReceiveCts { get; set; }
    public Task? ReceiveTask { get; set; }
    public string ShuttleId { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public int Port { get; set; }
    public DateTime LastActivity { get; set; } = DateTime.UtcNow;
    public readonly MemoryStream ReceiveBuffer = new();
    public ShuttleProtocolType Protocol { get; set; } = ShuttleProtocolType.Unknown;
    public IShuttleProtocolHandler? Handler { get; set; }
    public ConnectionState State { get; set; } = ConnectionState.Disconnected;

    /// <summary>Ожидающие ACK команды данного соединения. Ключ — локальный для соединения seq.</summary>
    public readonly ConcurrentDictionary<byte, TaskCompletionSource<bool>> AckWaiters = new();

    /// <summary>CTS цикла авто-реконнекта. Null, если реконнект не запущен.</summary>
    public CancellationTokenSource? ReconnectCts { get; set; }

    public bool ReconnectRunning { get; set; }

    public int ReconnectAttempt { get; set; }

    /// <summary>Флаг явного отключения пользователем — останавливает авто-реконнект.</summary>
    public bool UserDisconnectRequested { get; set; }

    /// <summary>True, если номер шаттла был задан явно (изменён через конфигурацию) — при реконнекте не пересчитывать из IP.</summary>
    public bool ShuttleIdFromConfig { get; set; }

    /// <summary>Сериализует попытки TCP-подключения (ручные и фоновые) — исключает двойные каналы.</summary>
    public readonly SemaphoreSlim ConnectGate = new(1, 1);

    /// <summary>CTS текущей попытки TCP-подключения — позволяет прервать зависший ConnectAsync.</summary>
    public CancellationTokenSource? ConnectAttemptCts { get; set; }

    /// <summary>Прогнозируемый новый IP шаттла после смены его номера (номер жёстко привязан к IP).</summary>
    public string? PendingIpAddress { get; set; }

    /// <summary>True, если сохранение номера в EEPROM выполнено — ожидаем перезагрузку контроллера и смену IP.</summary>
    public bool ExpectIpChangeAfterReboot { get; set; }

    /// <summary>
    /// Применяет смену номера шаттла: обновляет буквенно-цифровой ID и прогнозирует новый IP.
    /// </summary>
    public void ApplyShuttleNumberChange(int number, ShuttleOptions options)
    {
        ShuttleId = options.GetShuttleIdByNumber(number);
        ShuttleIdFromConfig = true;

        string? predicted = options.GetIpAddressByNumber(IpAddress, number);
        PendingIpAddress = !string.IsNullOrEmpty(predicted) &&
                           !string.Equals(predicted, IpAddress, StringComparison.OrdinalIgnoreCase)
            ? predicted
            : null;
    }

    private readonly object _seqLock = new();
    private byte _nextSeq;

    /// <summary>Выдаёт свободный seq, не занятый ожидающими ACK командами.</summary>
    public byte AllocateSeq()
    {
        lock (_seqLock)
        {
            for (int i = 0; i < 256; i++)
            {
                byte candidate = unchecked((byte)(_nextSeq + i));
                if (!AckWaiters.ContainsKey(candidate))
                {
                    _nextSeq = unchecked((byte)(candidate + 1));
                    return candidate;
                }
            }

            throw new InvalidOperationException("Нет свободных seq — слишком много команд ожидают ACK");
        }
    }

    /// <summary>Гасит все ожидающие ACK при разрыве соединения.</summary>
    public void FailPendingAcks()
    {
        foreach (KeyValuePair<byte, TaskCompletionSource<bool>> kvp in AckWaiters.ToArray())
        {
            if (AckWaiters.TryRemove(kvp.Key, out TaskCompletionSource<bool>? tcs))
            {
                tcs.TrySetResult(false);
            }
        }
    }
}

public class ShuttleHubClientService : IShuttleHubClientService, IDisposable
{
    private readonly ProtocolCallbacks _protocolCallbacks;
    private readonly IShuttleProtocolHandler _binaryHandler;
    private readonly IShuttleProtocolHandler _legacyHandler;
    private readonly ILogger<ShuttleHubClientService> _logger;
    private readonly ShuttleOptions _options;
    private readonly Dictionary<string, ShuttleConnection> _connections = [];
    private readonly object _lock = new();
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly Task _watchdogTask;

    public ShuttleHubClientService(
        ILoggerFactory? loggerFactory = null,
        IOptions<ShuttleOptions>? options = null)
    {
        ILoggerFactory factory = loggerFactory ?? NullLoggerFactory.Instance;
        _logger = factory.CreateLogger<ShuttleHubClientService>();
        _options = options?.Value ?? new ShuttleOptions();

        _protocolCallbacks = new ProtocolCallbacks
        {
            OnMessage = (ip, msg) => OnLogReceived(ip, msg),
        };

        _binaryHandler = new BinaryProtocolHandler(_protocolCallbacks, factory.CreateLogger<BinaryProtocolHandler>(), options);
        _legacyHandler = new LegacyProtocolHandler(_protocolCallbacks, factory.CreateLogger<LegacyProtocolHandler>(), options);

        _watchdogTask = Task.Run(WatchdogLoopAsync);
    }

    public event Action<string, ShuttleMessageBase>? LogReceived;

    public event Action<string, string>? Connected;

    public event Action<string>? Disconnected;

    public event Action<string>? Reconnecting;

    public async Task<List<IPAddress>> ScanNetworkAsync(
        string baseIp,
        int startIp,
        int endIp,
        int port,
        int timeoutMs = 1000,
        CancellationToken ct = default,
        IProgress<IPAddress>? progress = null)
    {
        var foundDevices = new ConcurrentBag<IPAddress>();

        await Parallel.ForEachAsync(
            Enumerable.Range(startIp, Math.Max(0, endIp - startIp + 1)),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = _options.ScanMaxParallelism,
                CancellationToken = ct,
            },
            async (lastOctet, token) =>
            {
                string ip = $"{baseIp}.{lastOctet}";
                try
                {
                    using var client = new TcpClient();
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
                    cts.CancelAfter(timeoutMs);

                    await client.ConnectAsync(IPAddress.Parse(ip), port, cts.Token);
                    if (client.Connected)
                    {
                        IPAddress address = IPAddress.Parse(ip);
                        foundDevices.Add(address);
                        progress?.Report(address);
                    }
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                }
                catch (SocketException)
                {
                }
            });

        return foundDevices.ToList();
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
                    IsConnected = kvp.Value.Transport?.IsConnected == true && kvp.Value.State == ConnectionState.Connected,
                });
            }

            return infos;
        }
    }

    //механизм подключения к шаттлу
    public async Task<bool> ConnectToShuttleAsync(string ipAddress, int port)
    {
        ShuttleConnection connection;

        lock (_lock)
        {
            if (_connections.TryGetValue(ipAddress, out ShuttleConnection? existing))
            {
                if (existing.State == ConnectionState.Connected)
                {
                    _logger.LogDebug("Подключение к {Ip} уже существует (State={State})", ipAddress, existing.State);
                    return true;
                }

                if (existing.State == ConnectionState.Connecting)
                {
                    // Прерываем текущую попытку (фоновую или ручную) — управление берёт этот вызов.
                    _logger.LogDebug("Прерываем текущую попытку подключения к {Ip} для ручного переподключения", ipAddress);
                    existing.ConnectAttemptCts?.Cancel();
                    existing.ReconnectCts?.Cancel();
                }

                // Переиспользуем объект после неудачной попытки/дисконнекта.
                connection = existing;
            }
            else
            {
                connection = new ShuttleConnection { IpAddress = ipAddress };
                _connections[ipAddress] = connection;
            }

            connection.UserDisconnectRequested = false;
        }

        connection.Port = port;
        return await ConnectInternalAsync(connection, reconnectAttempt: false);
    }

    private async Task<bool> ConnectInternalAsync(ShuttleConnection connection, bool reconnectAttempt)
    {
        // Сериализация с авто-реконнектом и ручными подключениями:
        // ручная попытка дожидается завершения фоновой и не создаёт второй TCP-канал.
        await connection.ConnectGate.WaitAsync();
        try
        {
            lock (_lock)
            {
                if (connection.State == ConnectionState.Connected)
                    return true;

                if (connection.State == ConnectionState.Disconnecting)
                    return false;

                connection.State = ConnectionState.Connecting;
            }

            var tcpClient = new TcpClient();
            using var attemptCts = new CancellationTokenSource(_options.ConnectTimeoutMs);
            lock (_lock)
            {
                connection.ConnectAttemptCts = attemptCts;
            }

            try
            {
                _logger.LogDebug("Старт TCP подключения к {Ip}:{Port}", connection.IpAddress, connection.Port);

                await tcpClient.ConnectAsync(connection.IpAddress, connection.Port, attemptCts.Token);

                // За время подключения пользователь мог отключиться или начать новое подключение.
                // Для фоновой попытки учитываем отмену цикла реконнекта, для ручной — только явные действия пользователя.
                if ((reconnectAttempt && connection.ReconnectCts?.IsCancellationRequested == true) ||
                    connection.UserDisconnectRequested ||
                    connection.State == ConnectionState.Disconnecting)
                {
                    _logger.LogDebug(
                        "Подключение к {Ip} прервано: ReconnectCancelled={R}, UserDisconnect={U}, State={S}",
                        connection.IpAddress,
                        connection.ReconnectCts?.IsCancellationRequested,
                        connection.UserDisconnectRequested,
                        connection.State);
                    tcpClient.Dispose();
                    return false;
                }

                tcpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                tcpClient.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, _options.KeepAliveTimeSeconds);
                tcpClient.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, _options.KeepAliveIntervalSeconds);
                tcpClient.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, _options.KeepAliveRetryCount);

                // Номер, заданный явно (изменённый через конфигурацию), переживает реконнект;
                // иначе вычисляем его из IP по правилам маппинга.
                connection.ShuttleId = connection.ShuttleIdFromConfig
                    ? connection.ShuttleId
                    : _options.ResolveShuttleId(connection.IpAddress);

                connection.Transport = new TcpConnection(tcpClient);
                connection.Protocol = ShuttleProtocolType.Unknown;
                connection.Handler = null;
                connection.ReceiveBuffer.SetLength(0);
                connection.LastActivity = DateTime.UtcNow;

                lock (_lock)
                {
                    if (_connections.TryGetValue(connection.IpAddress, out ShuttleConnection? current) &&
                        !ReferenceEquals(current, connection))
                    {
                        // Пользователь уже создал новое подключение — этот канал не нужен.
                        tcpClient.Dispose();
                        connection.Transport = null;
                        return false;
                    }

                    _connections[connection.IpAddress] = connection;
                    connection.State = ConnectionState.Connected;
                }

                _logger.LogInformation("Подключение к {Ip}:{Port} установлено (ShuttleId={ShuttleId})", connection.IpAddress, connection.Port, connection.ShuttleId);

                OnConnected(connection.IpAddress, connection.ShuttleId);

                connection.ReceiveCts = new CancellationTokenSource();
                connection.ReceiveTask = Task.Run(
                    async () =>
                    await ReceiveLoopAsync(connection, connection.ReceiveCts.Token), connection.ReceiveCts.Token);

                return true;
            }
            catch (OperationCanceledException) when (attemptCts.IsCancellationRequested)
            {
                tcpClient.Dispose();
                _logger.LogDebug(
                    "Попытка подключения к {Ip} прервана (таймаут {Timeout} мс или ручная отмена)",
                    connection.IpAddress,
                    _options.ConnectTimeoutMs);
                lock (_lock)
                {
                    if (connection.State == ConnectionState.Connecting)
                        connection.State = ConnectionState.Disconnected;
                }

                return false;
            }
            catch (Exception ex)
            {
                tcpClient.Dispose();
                _logger.LogError(ex, "Ошибка подключения к {Ip}:{Port}", connection.IpAddress, connection.Port);
                await InternalDisconnectAsync(connection.IpAddress, awaitReceiveTask: false);
                if (!reconnectAttempt)
                    connection.ReconnectCts?.Cancel();
                return false;
            }
            finally
            {
                lock (_lock)
                {
                    if (ReferenceEquals(connection.ConnectAttemptCts, attemptCts))
                        connection.ConnectAttemptCts = null;
                }
            }
        }
        finally
        {
            connection.ConnectGate.Release();
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
                    _logger.LogWarning("Соединение с {Ip} закрыто сервером (0 bytes)", connection.IpAddress);
                    await HandleConnectionLostAsync(connection);
                    break;
                }

                connection.LastActivity = DateTime.UtcNow;
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
                _logger.LogError(
                    ex,
                    "Ошибка приёма от {Ip} (Buffer={BufferBytes}, Protocol={Protocol})",
                    connection.IpAddress,
                    connection.ReceiveBuffer.Length,
                    connection.Protocol);
                await HandleConnectionLostAsync(connection);
                break;
            }
        }

        _logger.LogDebug("ReceiveLoop завершён для {Ip}", connection.IpAddress);
    }

    private async Task HandleConnectionLostAsync(ShuttleConnection connection)
    {
        bool userRequested = connection.UserDisconnectRequested;

        // Внимание: вызываем без ожидания ReceiveTask — этот метод сам выполняется внутри ReceiveLoop,
        // иначе был бы самодедлок.
        await InternalDisconnectAsync(connection.IpAddress, awaitReceiveTask: false);

        if (userRequested || !_options.ReconnectEnabled)
            return;

        await ReconnectLoopAsync(connection);
    }

    private async Task ReconnectLoopAsync(ShuttleConnection connection)
    {
        lock (_lock)
        {
            if (connection.ReconnectRunning)
                return;

            connection.ReconnectRunning = true;
            connection.ReconnectCts = new CancellationTokenSource();
        }

        try
        {
            // После смены номера + сохранения контроллер перезагрузился с новым IP —
            // переключаем цель реконнекта на прогнозируемый адрес.
            ApplyPendingIpChange(connection);

            while (!connection.ReconnectCts.IsCancellationRequested && !connection.UserDisconnectRequested)
            {
                if (_options.MaxReconnectAttempts >= 0 && connection.ReconnectAttempt >= _options.MaxReconnectAttempts)
                {
                    _logger.LogWarning(
                        "Авто-реконнект к {Ip} прекращён: исчерпаны попытки ({Attempts})",
                        connection.IpAddress,
                        connection.ReconnectAttempt);
                    break;
                }

                int shift = Math.Min(connection.ReconnectAttempt, 10);
                int delayMs = Math.Min(_options.ReconnectBaseDelayMs * (1 << shift), _options.ReconnectMaxDelayMs);

                try
                {
                    await Task.Delay(delayMs, connection.ReconnectCts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                OnReconnecting(connection.IpAddress);
                _logger.LogInformation("Попытка реконнекта {Attempt} к {Ip}...", connection.ReconnectAttempt + 1, connection.IpAddress);

                if (await ConnectInternalAsync(connection, reconnectAttempt: true))
                {
                    connection.ReconnectAttempt = 0;
                    return;
                }

                connection.ReconnectAttempt++;
            }
        }
        finally
        {
            lock (_lock)
            {
                connection.ReconnectRunning = false;
            }
        }
    }

    /// <summary>
    /// Если ожидается смена IP после перезагрузки — переключает цель реконнекта
    /// с текущего адреса соединения на прогнозируемый.
    /// </summary>
    private void ApplyPendingIpChange(ShuttleConnection connection)
    {
        lock (_lock)
        {
            if (!connection.ExpectIpChangeAfterReboot || string.IsNullOrEmpty(connection.PendingIpAddress))
                return;

            string oldIp = connection.IpAddress;
            string newIp = connection.PendingIpAddress;

            connection.ExpectIpChangeAfterReboot = false;
            connection.PendingIpAddress = null;

            if (string.Equals(oldIp, newIp, StringComparison.OrdinalIgnoreCase))
                return;

            if (_connections.TryGetValue(newIp, out ShuttleConnection? existing) && !ReferenceEquals(existing, connection))
            {
                // Новый IP уже занят другим (живым) соединением — не конфликтуем, гасим цикл.
                _logger.LogWarning("IP {NewIp} уже занят другим соединением — авто-реконнект после смены номера отменён", newIp);
                connection.ReconnectCts?.Cancel();
                return;
            }

            _connections.Remove(oldIp);
            connection.IpAddress = newIp;
            _connections[newIp] = connection;

            _logger.LogInformation(
                "Шаттл сменил IP после смены номера: {OldIp} -> {NewIp} (ShuttleId={ShuttleId})",
                oldIp,
                newIp,
                connection.ShuttleId);
        }
    }

    private async Task WatchdogLoopAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (true)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(_lifetimeCts.Token))
                    return;
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (!_options.WatchdogEnabled)
                continue;

            List<ShuttleConnection> stale = [];
            lock (_lock)
            {
                foreach (KeyValuePair<string, ShuttleConnection> kvp in _connections)
                {
                    ShuttleConnection connection = kvp.Value;
                    if (connection.State == ConnectionState.Connected &&
                        (DateTime.UtcNow - connection.LastActivity) > TimeSpan.FromMilliseconds(_options.WatchdogTimeoutMs))
                    {
                        stale.Add(connection);
                    }
                }
            }

            foreach (ShuttleConnection connection in stale)
            {
                _logger.LogWarning(
                    "Watchdog: нет данных от {Ip} более {TimeoutMs} мс — переподключение",
                    connection.IpAddress,
                    _options.WatchdogTimeoutMs);
                await InternalDisconnectAsync(connection.IpAddress, awaitReceiveTask: true);
                if (_options.ReconnectEnabled)
                    await ReconnectLoopAsync(connection);
            }
        }
    }

    private void DetectProtocol(ShuttleConnection connection)
    {
        var data = connection.ReceiveBuffer.ToArray();

        if (data.Length >= 2 &&
            data[0] == ProtocolConstants.PROTOCOL_SYNC_1_V2 &&
            data[1] == ProtocolConstants.PROTOCOL_SYNC_2_V2)
        {
            connection.Protocol = ShuttleProtocolType.Binary;
            connection.Handler = _binaryHandler;
            _logger.LogDebug("Обнаружен бинарный протокол: {Ip}", connection.IpAddress);
            return;
        }

        if (data.Any(b => b == '\n'))
        {
            connection.Protocol = ShuttleProtocolType.Legacy;
            connection.Handler = _legacyHandler;
            _logger.LogDebug("Обнаружен legacy-протокол: {Ip}", connection.IpAddress);
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
        _logger.LogDebug("IP {Ip}; Command: {Command}", ip, command);
        if (!_connections.TryGetValue(ip, out var conn))
            return false;

        if (conn.Handler == null)
            return false;

        bool result = await conn.Handler.SendCommandAsync(conn, command, arg1, arg2, CancellationToken.None, _options.AckTimeoutMs);

        if (result && command == ShuttleCommand.SaveConfig)
        {
            bool expectIpChange;
            lock (_lock)
            {
                expectIpChange = conn.PendingIpAddress != null;
                if (expectIpChange)
                    conn.ExpectIpChangeAfterReboot = true;
            }

            if (expectIpChange && _options.AutoRebootAfterIdSave)
                ScheduleAutoReboot(conn);
        }

        return result;
    }

    /// <summary>После сохранения изменённого номера в EEPROM — отправляем перезагрузку контроллера с задержкой.</summary>
    private void ScheduleAutoReboot(ShuttleConnection connection)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_options.AutoRebootDelayMs);

                IShuttleProtocolHandler? handler = connection.Handler;
                if (handler != null && connection.Transport?.IsConnected == true)
                {
                    _logger.LogInformation("Отправка перезагрузки контроллера {Ip} после сохранения номера...", connection.IpAddress);
                    await handler.SendCommandAsync(
                        connection,
                        ShuttleCommand.SystemReset,
                        0,
                        0,
                        CancellationToken.None,
                        _options.AckTimeoutMs);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ошибка авто-перезагрузки {Ip}", connection.IpAddress);
            }
        });
    }

    public async Task<bool> SendConfigAsync(
        string ip,
        ShuttleConfigCommand param,
        int value)
    {
        _logger.LogDebug("IP {Ip}; Value: {Value}; param: {Param}", ip, value, param);
        if (!_connections.TryGetValue(ip, out var connection))
            return false;

        if (connection.Handler == null)
            return false;

        return await connection.Handler.SendConfigAsync(connection, param, value, _options.AckTimeoutMs);
    }

    public async Task<bool> SendManualCommandAsync(string ip, string rawCommand, int timeoutMs = 1000)
    {
        _logger.LogDebug("IP {Ip}; rawCommand: {RawCommand}", ip, rawCommand);
        if (!_connections.TryGetValue(ip, out var conn))
            return false;

        if (conn.Handler == null)
            return false;

        return await conn.Handler.SendManualCommandAsync(conn, rawCommand, CancellationToken.None, _options.AckTimeoutMs);
    }

    public async Task<bool> RequestFullConfigAsync(string ip)
    {
        if (!_connections.TryGetValue(ip, out var conn))
            return false;

        if (conn.Handler is not BinaryProtocolHandler binary)
            return false;

        return await binary.RequestFullConfigAsync(conn);
    }

    public Task DisconnectAsync(string ipAddress)
    {
        lock (_lock)
        {
            if (_connections.TryGetValue(ipAddress, out ShuttleConnection? connection))
            {
                connection.UserDisconnectRequested = true;
                connection.ReconnectCts?.Cancel();
                connection.ConnectAttemptCts?.Cancel();
            }
        }

        return InternalDisconnectAsync(ipAddress);
    }

    private async Task InternalDisconnectAsync(string ipAddress, bool awaitReceiveTask = true)
    {
        ShuttleConnection? connectionToDispose = null;
        bool wasConnected = false;

        lock (_lock)
        {
            if (_connections.TryGetValue(ipAddress, out var connection))
            {
                if (connection.State == ConnectionState.Disconnecting)
                {
                    return;
                }

                connectionToDispose = connection;
                wasConnected = connection.State == ConnectionState.Connected;
                connection.State = ConnectionState.Disconnecting;

                connection.ReceiveCts?.Cancel();
                connection.ReceiveCts?.Dispose();
                connection.ReceiveCts = null;

                connection.Transport?.Dispose();
                connection.Transport = null;

                connection.FailPendingAcks();

                _connections.Remove(ipAddress);
            }
        }

        if (connectionToDispose != null)
        {
            if (awaitReceiveTask && connectionToDispose.ReceiveTask != null)
            {
                try
                {
                    await connectionToDispose.ReceiveTask;
                }
                catch (OperationCanceledException)
                {
                }
            }

            lock (_lock)
            {
                connectionToDispose.State = ConnectionState.Disconnected;
            }

            // Событие шлём только если соединение реально было установлено.
            if (wasConnected)
            {
                OnDisconnected(ipAddress);
            }
        }
    }

    public void Dispose()
    {
        _lifetimeCts.Cancel();

        lock (_lock)
        {
            foreach (var kvp in _connections)
            {
                var conn = kvp.Value;
                conn.UserDisconnectRequested = true;
                conn.ReconnectCts?.Cancel();

                conn.State = ConnectionState.Disconnecting;

                conn.ReceiveCts?.Cancel();
                conn.ReceiveCts?.Dispose();

                conn.Transport?.Dispose();
                conn.Transport = null;

                conn.FailPendingAcks();

                conn.State = ConnectionState.Disconnected;
            }

            _connections.Clear();
        }

        _lifetimeCts.Dispose();
    }

    private void OnConnected(string ip, string id) => Connected?.Invoke(ip, id);

    private void OnDisconnected(string ip) => Disconnected?.Invoke(ip);

    private void OnReconnecting(string ip) => Reconnecting?.Invoke(ip);

    private void OnLogReceived(string ip, ShuttleMessageBase msg) => LogReceived?.Invoke(ip, msg);
}
