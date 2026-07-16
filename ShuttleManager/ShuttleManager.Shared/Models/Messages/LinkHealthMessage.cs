using ShuttleManager.Shared.Models.Protocol;

namespace ShuttleManager.Shared.Models.Messages;

public class LinkHealthMessage : ShuttleMessageBase
{
    public LinkHealthPacket Data { get; set; }

    public override string ToFormattedTerminalString()
    {
        return
            $"[LINK_HEALTH] RSSI: {Data.PacketRssiDbm}dBm, " +
            $"RSSI Raw: {Data.PacketRssiRaw}, " +
            $"Flags: {Data.Flags}, " +
            $"Age: {Data.PacketRssiAgeMs}ms";
    }
}
