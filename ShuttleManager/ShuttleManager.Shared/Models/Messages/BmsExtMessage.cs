using ShuttleManager.Shared.Models.Protocol;

namespace ShuttleManager.Shared.Models.Messages;

public class BmsExtMessage : ShuttleMessageBase
{
    public BmsExtPacket Data { get; set; }

    public override string ToFormattedTerminalString()
    {
        return
            $"[BMS_EXT] PackVoltage: {Data.PackVoltage_mV}mV, " +
            $"Current: {Data.PackCurrent_cA}cA, " +
            $"RemainCap: {Data.RemainCapacity_cAh}cAh, " +
            $"SOC: {Data.SocPercent}%";
    }
}
