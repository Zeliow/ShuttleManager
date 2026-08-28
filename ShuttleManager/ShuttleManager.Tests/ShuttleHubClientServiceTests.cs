using System.Net;
using System.Net.Sockets;
using ShuttleManager.Shared.Models;
using ShuttleManager.Shared.Services.ShuttleClient;

namespace ShuttleManager.Tests;

public class ShuttleHubClientServiceTests
{
    private static TcpListener StartListener(out int port)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        port = ((IPEndPoint)listener.LocalEndpoint).Port;
        return listener;
    }

    [Fact]
    public async Task ConnectTwice_DoesNotOpenSecondTcpConnection()
    {
        TcpListener listener = StartListener(out int port);
        using var service = new ShuttleHubClientService();

        try
        {
            bool first = await service.ConnectToShuttleAsync("127.0.0.1", port);
            Assert.True(first);

            Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
            using TcpClient accepted = await acceptTask;

            bool second = await service.ConnectToShuttleAsync("127.0.0.1", port);
            Assert.True(second);

            // Если бы открылось второе TCP-соединение, accept завершился бы успехом, а не таймаутом.
            using var cts = new CancellationTokenSource(500);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await listener.AcceptTcpClientAsync(cts.Token));

            List<Shuttle> connected = service.GetConnectedShuttles();
            Assert.Single(connected);
        }
        finally
        {
            await service.DisconnectAsync("127.0.0.1");
            listener.Stop();
        }
    }

    [Fact]
    public async Task Disconnect_FiresDisconnectedEventOnceAndClearsConnections()
    {
        TcpListener listener = StartListener(out int port);
        using var service = new ShuttleHubClientService();
        var disconnectedIps = new List<string>();

        try
        {
            service.Disconnected += ip => disconnectedIps.Add(ip);

            bool connected = await service.ConnectToShuttleAsync("127.0.0.1", port);
            Assert.True(connected);

            Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
            using TcpClient accepted = await acceptTask;

            await service.DisconnectAsync("127.0.0.1");

            var ip = Assert.Single(disconnectedIps);
            Assert.Equal("127.0.0.1", ip);
            Assert.Empty(service.GetConnectedShuttles());

            // Повторный дисконнект — идемпотентен, событие не дублируется.
            await service.DisconnectAsync("127.0.0.1");
            Assert.Single(disconnectedIps);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task FailedConnect_ReturnsFalseAndDoesNotFireDisconnected()
    {
        TcpListener listener = StartListener(out int port);
        int closedPort = port;
        listener.Stop();

        using var service = new ShuttleHubClientService();
        var disconnectedIps = new List<string>();
        service.Disconnected += ip => disconnectedIps.Add(ip);

        bool connected = await service.ConnectToShuttleAsync("127.0.0.1", closedPort);

        Assert.False(connected);
        Assert.Empty(disconnectedIps);
        Assert.Empty(service.GetConnectedShuttles());
    }

    [Fact]
    public async Task ConnectAfterFailedAttempt_Succeeds()
    {
        TcpListener listener = StartListener(out int port);

        // Сначала порт, где никто не слушает.
        var deadListener = new TcpListener(IPAddress.Loopback, 0);
        deadListener.Start();
        int deadPort = ((IPEndPoint)deadListener.LocalEndpoint).Port;
        deadListener.Stop();

        using var service = new ShuttleHubClientService();

        bool first = await service.ConnectToShuttleAsync("127.0.0.1", deadPort);
        Assert.False(first);

        bool second = await service.ConnectToShuttleAsync("127.0.0.1", port);
        Assert.True(second);

        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        using TcpClient accepted = await acceptTask;

        Assert.Single(service.GetConnectedShuttles());

        await service.DisconnectAsync("127.0.0.1");
        listener.Stop();
    }
}
