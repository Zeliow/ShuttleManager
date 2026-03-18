using ShuttleManager.Shared.Models.Messages;

namespace ShuttleManager.Shared.Services.ShuttleClient.Helpers;

public class ProtocolCallbacks
{
    /// <summary>Gets вызывается при получении сообщения от шаттла.</summary>
    public required Action<string, ShuttleMessageBase>? OnMessage { get; init; }
}