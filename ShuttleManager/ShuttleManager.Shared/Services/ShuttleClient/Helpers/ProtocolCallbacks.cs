using ShuttleManager.Shared.Models;

namespace ShuttleManager.Shared.Services.ShuttleClient.Helpers;

public class ProtocolCallbacks
{
    /// <summary>Вызывается при получении сообщения от шаттла.</summary>
    public required Action<string, ShuttleMessageBase>? OnMessage { get; init; }
}