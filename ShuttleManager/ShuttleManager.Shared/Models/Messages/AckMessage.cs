using ShuttleManager.Shared.Models.Protocol;

namespace ShuttleManager.Shared.Models.Messages;

public class AckMessage : ShuttleMessageBase
{
    public AckPacket Data { get; set; }

    public override string ToFormattedTerminalString()
    {
        return $"[ACK] RefSeq: {Data.RefSeq}, Result: {Data.Result}";
    }
}