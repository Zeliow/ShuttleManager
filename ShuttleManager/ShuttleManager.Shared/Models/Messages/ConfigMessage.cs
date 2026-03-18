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