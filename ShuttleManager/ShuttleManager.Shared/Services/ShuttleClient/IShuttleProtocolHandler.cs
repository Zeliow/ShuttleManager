using ShuttleManager.Shared.Models.Protocol;
using ShuttleManager.Shared.Services.Enums;

namespace ShuttleManager.Shared.Services.ShuttleClient;

public interface IShuttleProtocolHandler
{
    ShuttleProtocolType Protocol { get; }

    void ProcessBuffer(ShuttleConnection connection);

    // Для Legacy команд
    Task<bool> SendCommandAsync(ShuttleConnection connection, string command, CancellationToken ct, int timeoutMs = 1000);

    // Для Binary команд
    Task<bool> SendCommandAsync(ShuttleConnection connection, CmdType cmd, int arg1, int arg2, int timeoutMs = 1000);
}