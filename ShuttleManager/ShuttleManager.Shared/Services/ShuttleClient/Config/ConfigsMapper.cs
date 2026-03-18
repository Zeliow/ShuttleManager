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

public static class LegacyConfigMapper
{
    public static string Map(string shuttleId, ShuttleConfigCommand cmd, int value)
    {
        DateTime currentTime = DateTime.Now;
        return cmd switch
        {
            ShuttleConfigCommand.ReverseMode => $"{shuttleId}dRevMo", // ok?
            ShuttleConfigCommand.MaxSpeed => $"{shuttleId}dSp{value}", // ok!
            ShuttleConfigCommand.MinBattery => $"{shuttleId}dBc{value}", // ok!
            ShuttleConfigCommand.InterPalletDistance => $"{shuttleId}dDm{value}", //ok!
            ShuttleConfigCommand.DistOfEdge => $"{shuttleId}dMc{value}", // ok?
            ShuttleConfigCommand.ShuttleLength => $"{shuttleId}dSl{(value == 800 ? "080" : value)}",
            ShuttleConfigCommand.ShuttleNumber => $"{shuttleId}dNN{value}", // ok!
            //ShuttleConfigCommand.DT => $"DT{currentTime:HH:mm:ss dd/MM/yyyy}", // ok?
            _ => throw new NotSupportedException(
                $"Конфигурационная команда '{cmd}' не поддерживается в легаси-протоколе")
        };
    }
}