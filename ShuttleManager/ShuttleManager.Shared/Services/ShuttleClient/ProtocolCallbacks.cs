using ShuttleManager.Shared.Models;

namespace ShuttleManager.Shared.Services.ShuttleClient;

public class ProtocolCallbacks
{
    /// <summary>Вызывается при получении сообщения от шаттла.</summary>
    public required Action<string, ShuttleMessageBase>? OnMessage { get; init; }

    /// <summary>Можно добавить OnError, OnHeartbeat и т.д., если нужно.</summary>
}