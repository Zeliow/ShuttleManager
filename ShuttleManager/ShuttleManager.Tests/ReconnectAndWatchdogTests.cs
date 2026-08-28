using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using ShuttleManager.Shared.Services.ShuttleClient;

namespace ShuttleManager.Tests;

public class ReconnectAndWatchdogTests
{
    private static TcpListener StartListener(out int port)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        port = ((IPEndPoint)listener.LocalEndpoint).Port;
        return listener;
    }

    private static ShuttleOptions FastReconnectOptions()
    {
        return new ShuttleOptions
        {
            ConnectTimeoutMs = 300,
            ReconnectEnabled = true,
            MaxReconnectAttempts = -1,
            ReconnectBaseDelayMs = 50,
            ReconnectMaxDelayMs = 200,
            WatchdogEnabled = false,
        };
    }

    private static async Task<TcpClient> AcceptWithTimeoutAsync(TcpListener listener, int timeoutMs)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        return await listener.AcceptTcpClientAsync(cts.Token);
    }

    private static async Task WaitUntilPortRefusesAsync(int port, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            try
            {
                using var probe = new TcpClient();
                await probe.ConnectAsync(IPAddress.Loopback, port);
            }
            catch (SocketException)
            {
                return;
            }

            await Task.Delay(50);
        }
    }

    [Fact]
    public async Task ServerClosesConnection_ServiceReconnectsAutomatically()
    {
        TcpListener listener = StartListener(out int port);
        var options = FastReconnectOptions();
        var logs = new CollectingLoggerFactory();
        using var service = new ShuttleHubClientService(logs, Options.Create(options));
        var connectedEvents = new List<string>();
        service.Connected += (ip, _) => connectedEvents.Add(ip);

        try
        {
            Assert.True(await service.ConnectToShuttleAsync("127.0.0.1", port));

            using TcpClient first = await AcceptWithTimeoutAsync(listener, 3000);
            await first.Client.DisconnectAsync(false);

            // Сервер закрыл соединение → receive-loop должен запустить реконнект.
            using TcpClient second = await AcceptWithTimeoutAsync(listener, 5000);

            // Accept срабатывает раньше, чем клиент завершит OnConnected — ждём событие.
            var sw = Stopwatch.StartNew();
            while (connectedEvents.Count < 2 && sw.ElapsedMilliseconds < 3000)
            {
                await Task.Delay(20);
            }

            Assert.True(connectedEvents.Count == 2, $"Ожидалось 2 Connected-события, получено {connectedEvents.Count}.\nЛоги:\n{string.Join("\n", logs.Entries)}");
        }
        finally
        {
            await service.DisconnectAsync("127.0.0.1");
            listener.Stop();
        }
    }

    [Fact]
    public async Task UserDisconnect_DoesNotReconnect()
    {
        TcpListener listener = StartListener(out int port);
        var options = FastReconnectOptions();
        using var service = new ShuttleHubClientService(null, Options.Create(options));

        try
        {
            Assert.True(await service.ConnectToShuttleAsync("127.0.0.1", port));
            using TcpClient first = await AcceptWithTimeoutAsync(listener, 3000);

            await service.DisconnectAsync("127.0.0.1");

            // Пользователь отключился — реконнекта быть не должно.
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await AcceptWithTimeoutAsync(listener, 800));
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task Reconnect_StopsAfterMaxAttempts()
    {
        TcpListener listener = StartListener(out int port);

        var options = FastReconnectOptions();
        options.MaxReconnectAttempts = 3;
        var logs = new CollectingLoggerFactory();
        using var service = new ShuttleHubClientService(logs, Options.Create(options));
        var reconnectEvents = new List<string>();
        service.Reconnecting += ip => reconnectEvents.Add(ip);

        try
        {
            Assert.True(await service.ConnectToShuttleAsync("127.0.0.1", port));
            using TcpClient first = await AcceptWithTimeoutAsync(listener, 3000);

            // Останавливаем слушатель и ждём, пока порт реально закроется:
            // иначе попытки реконнекта зависают в SYN-ретраях ОС.
            listener.Stop();
            await WaitUntilPortRefusesAsync(port, 3000);

            // Обрываем серверную сторону: все последующие попытки реконнекта будут падать быстро.
            await first.Client.DisconnectAsync(false);

            // Даём реконнекту отработать все попытки (50 + 100 + 200 мс + время на подключение).
            await Task.Delay(2500);

            Assert.True(reconnectEvents.Count == 3, $"Ожидалось 3 попытки, получено {reconnectEvents.Count}.\nЛоги:\n{string.Join("\n", logs.Entries)}");

            // После исчерпания попыток новых быть не должно.
            await Task.Delay(800);
            Assert.True(reconnectEvents.Count == 3, $"После исчерпания попыток получено ещё событие. Логи:\n{string.Join("\n", logs.Entries)}");
        }
        finally
        {
            await service.DisconnectAsync("127.0.0.1");
            listener.Stop();
        }
    }

    [Fact]
    public async Task Watchdog_ForcesReconnectWhenNoDataArrives()
    {
        TcpListener listener = StartListener(out int port);
        var options = FastReconnectOptions();
        options.WatchdogEnabled = true;
        options.WatchdogTimeoutMs = 400;
        using var service = new ShuttleHubClientService(null, Options.Create(options));

        try
        {
            Assert.True(await service.ConnectToShuttleAsync("127.0.0.1", port));

            // Сервер принимает соединение, но ничего не шлёт → watchdog должен переподключить.
            using TcpClient first = await AcceptWithTimeoutAsync(listener, 3000);
            using TcpClient second = await AcceptWithTimeoutAsync(listener, 5000);
        }
        finally
        {
            await service.DisconnectAsync("127.0.0.1");
            listener.Stop();
        }
    }

    [Fact]
    public async Task ReconnectDisabled_NoReconnectAfterServerClose()
    {
        TcpListener listener = StartListener(out int port);
        var options = FastReconnectOptions();
        options.ReconnectEnabled = false;
        using var service = new ShuttleHubClientService(null, Options.Create(options));

        try
        {
            Assert.True(await service.ConnectToShuttleAsync("127.0.0.1", port));
            using TcpClient first = await AcceptWithTimeoutAsync(listener, 3000);
            await first.Client.DisconnectAsync(false);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await AcceptWithTimeoutAsync(listener, 800));
        }
        finally
        {
            listener.Stop();
        }
    }
}
