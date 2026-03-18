using ShuttleManager.Shared.Models;
using ShuttleManager.Shared.Models.Protocol;
using ShuttleManager.Shared.Services.Enums;
using System.Net;

namespace ShuttleManager.Shared.Interfaces;

public interface IShuttleHubClientService
{
    event Action<string, ShuttleMessageBase>? LogReceived;        // Передаёт IP и лог

    event Action<string, string>? Connected;              // Передаёт IP и ID шаттла

    event Action<string>? Disconnected;                // Передаёт IP

    public Task ConnectToShuttleAsync(string ipAddress, int port);

    void DisconnectFromShuttle(string ipAddress);

    public Task<bool> SendCommandAsync(string ip, ShuttleCommand command, int arg1 = 0, int arg2 = 0);

    public Task<bool> SendConfigAsync(string ip, ShuttleConfigCommand param, int value);

    public Task<bool> SendDateTimeAsync(string ipAddress, DateTime utcTime, int timeoutMs = 1000);

    public Task<bool> SendManualCommandAsync(string ip, string rawCommand, int timeoutMs = 1000);

    List<Shuttle> GetConnectedShuttles();

    public Task<List<IPAddress>> ScanNetworkAsync(string baseIp, int startIp, int endIp, int port, int timeoutMs = 1000);
}