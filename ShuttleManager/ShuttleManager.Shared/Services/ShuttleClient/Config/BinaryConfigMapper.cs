using ShuttleManager.Shared.Models.Protocol;
using ShuttleManager.Shared.Services.Enums;

namespace ShuttleManager.Shared.Services.ShuttleClient.Config;

public static class BinaryConfigMapper
{
    public static ConfigParamID Map(ShuttleConfigCommand cmd)
    {
        return cmd switch
        {
            ShuttleConfigCommand.ReverseMode => ConfigParamID.CFG_REVERSE_MODE,
            ShuttleConfigCommand.MaxSpeed => ConfigParamID.CFG_MAX_SPEED,
            ShuttleConfigCommand.MinBattery => ConfigParamID.CFG_MIN_BATT,
            ShuttleConfigCommand.InterPalletDistance => ConfigParamID.CFG_INTER_PALLET,
            ShuttleConfigCommand.DistOfEdge => ConfigParamID.CFG_CHNL_OFFSET,
            ShuttleConfigCommand.ShuttleLength => ConfigParamID.CFG_SHUTTLE_LEN,
            ShuttleConfigCommand.ShuttleNumber => ConfigParamID.CFG_SHUTTLE_NUM,
            _ => throw new ArgumentOutOfRangeException()
        };
    }
}