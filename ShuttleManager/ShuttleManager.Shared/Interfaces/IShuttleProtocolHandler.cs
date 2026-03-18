using ShuttleManager.Shared.Models.Protocol;
using ShuttleManager.Shared.Services.Enums;
using ShuttleManager.Shared.Services.ShuttleClient;

namespace ShuttleManager.Shared.Interfaces;

public interface IShuttleProtocolHandler
{
    ShuttleProtocolType Protocol { get; }

    void ProcessBuffer(ShuttleConnection connection);

    Task<bool> SendCommandAsync(ShuttleConnection connection, ShuttleCommand cmd, int arg1, int arg2, CancellationToken ct, int timeoutMs = 1000);

    // Конфигурация
    Task<bool> SendConfigAsync(ShuttleConnection connection, ShuttleConfigCommand param, int value, int timeoutMs = 1000);

    // Установка времени
    Task<bool> SendDateTimeAsync(ShuttleConnection connection, DateTime utcTime, int timeoutMs = 1000);

    //Chats command
    Task<bool> SendManualCommandAsync(ShuttleConnection connection, string rawCommand, CancellationToken ct, int timeoutMs = 1000);
}