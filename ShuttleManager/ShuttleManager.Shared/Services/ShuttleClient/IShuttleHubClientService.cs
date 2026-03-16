using ShuttleManager.Shared.Models;
using ShuttleManager.Shared.Models.Protocol;
using System.Net;

namespace ShuttleManager.Shared.Services.ShuttleClient;

public interface IShuttleHubClientService
{
    event Action<string, ShuttleMessageBase>? LogReceived;        // Передаёт IP и лог

    event Action<string, int>? Connected;              // Передаёт IP и ID шаттла

    event Action<string>? Disconnected;                // Передаёт IP

    public Task ConnectToShuttleAsync(string ipAddress, int port);

    public Task DisconnectFromShuttleAsync(string ipAddress);

    //Legacy protocol
    public Task<bool> SendCommandToShuttleAsync(string ipAddress, string command, int timeoutMs);

    //Binary protocol
    public Task<bool> SendBinaryCommandAsync(string ipAddress, CmdType cmd, int arg1 = 0, int arg2 = 0, int timeoutMs = 1000);

    //Binary protocol
    public Task<bool> SendDateTimeAsync(string ipAddress, DateTime utcTime, int timeoutMs = 1000);

    //Binary protocol
    public Task<bool> SendConfigSetAsync(string ipAddress, ConfigParamID param, int value, int timeoutMs = 1000);

    List<Shuttle> GetConnectedShuttles();

    public Task<List<IPAddress>> ScanNetworkAsync(string baseIp, int startIp, int endIp, int port, int timeoutMs = 1000);
}