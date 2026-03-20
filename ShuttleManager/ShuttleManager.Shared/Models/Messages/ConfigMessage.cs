using ShuttleManager.Shared.Models.Protocol;

namespace ShuttleManager.Shared.Models.Messages;

public class ConfigMessage : ShuttleMessageBase
{
    public ConfigPacket Data { get; set; }

    public override string ToFormattedTerminalString()
    {
        return $"[CONFIG] Param: {(ConfigParamID)Data.ParamID}, Value: {Data.Value}";
    }
}

public class FullConfigMessage : ShuttleMessageBase
{
    public FullConfigPacket Data { get; set; }

    public override string ToFormattedTerminalString()
    {
        return $"[FULL CONFIG] InterPallet: {Data.InterPallet}, " +
            $"Shuttle Lenght: {Data.ShuttleLen}, " +
            $"Max Speed: {Data.MaxSpeed}, " +
            $"Wait Time: {Data.WaitTime}, " +
            $"MrpOffset: {Data.MprOffset}, " +
            $"Channer offset: {Data.ChnlOffset}, " +
            $"Shuttle Number: {Data.ShuttleNumber}, " +
            $"Min Batt: {Data.MinBatt}, " +
            $"FIFO|| LIFO: {Data.FifoLifo}, " +
            $"Reverse Mode: {Data.ReverseMode}";
    }
}