using ShuttleManager.Shared.Models.Protocol;

namespace ShuttleManager.Shared.Services.ShuttleClient.Config;

public static class LegacyCommandMapper
{
    public static string Map(string shuttleId, ShuttleCommand cmd)
    {
        return cmd switch
        {
            ShuttleCommand.Stop => $"{shuttleId}dStop_",
            ShuttleCommand.Load => $"{shuttleId}dLoad_",
            ShuttleCommand.LongLoad => $"{shuttleId}dLLoad",
            ShuttleCommand.Unload => $"{shuttleId}dUnld_",
            ShuttleCommand.LongUnload => $"{shuttleId}dLUnld",
            ShuttleCommand.Demo => $"{shuttleId}dDemo_",
            ShuttleCommand.SystemReset => "RBT",
            ShuttleCommand.Reset => $"{shuttleId}dReset",
            ShuttleCommand.SaveConfig => $"{shuttleId}dSaveC",
            ShuttleCommand.Calibrate => $"{shuttleId}dClbr_",
            ShuttleCommand.SealForward => $"{shuttleId}dComFo", //Уплотение вперёд
            ShuttleCommand.SealBackward => $"{shuttleId}dComBa", //Уплотение назад
            ShuttleCommand.LiftUp => $"{shuttleId}dUp___",
            ShuttleCommand.LiftDown => $"{shuttleId}dDown_",
            _ => throw new NotSupportedException()
        };
    }
}

public static class BinaryCommandMapper
{
    public static CmdType Map(ShuttleCommand cmd)
    {
        return cmd switch
        {
            ShuttleCommand.Stop => CmdType.CMD_STOP,
            ShuttleCommand.Load => CmdType.CMD_LOAD,
            ShuttleCommand.LongLoad => CmdType.CMD_LONG_LOAD,
            ShuttleCommand.Unload => CmdType.CMD_UNLOAD,
            ShuttleCommand.LongUnload => CmdType.CMD_LONG_UNLOAD,
            ShuttleCommand.Demo => CmdType.CMD_DEMO,
            ShuttleCommand.Reset => CmdType.CMD_SYSTEM_RESET,
            ShuttleCommand.SaveConfig => CmdType.CMD_SAVE_EEPROM,
            ShuttleCommand.Calibrate => CmdType.CMD_CALIBRATE,
            _ => throw new NotSupportedException()
        };
    }
}

public static class LegacyConfigMapper
{
    public static string Map(string shuttleId, ShuttleConfigCommand cmd, int value)
    {
        return cmd switch
        {
            ShuttleConfigCommand.ReverseMode => $"{shuttleId}dRevMo",
            ShuttleConfigCommand.MaxSpeed => $"{shuttleId}dSp{value}", // ok!
            ShuttleConfigCommand.MinBattery => $"{shuttleId}dBc{value}", // ok!
            ShuttleConfigCommand.InterPalletDistance => $"{shuttleId}dDm{value}", //ok!
            ShuttleConfigCommand.ChannelOffset => $"{shuttleId}dChOfs",
            ShuttleConfigCommand.ShuttleLength => $"{shuttleId}dSl{value}", //not ok. TODO: add zero before value for 800 lenght.
            ShuttleConfigCommand.ShuttleNumber => $"{shuttleId}dNN{value}", //ok!
            _ => throw new NotSupportedException(
                $"Конфигурационная команда '{cmd}' не поддерживается в легаси-протоколе")
        };
    }
}

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
            ShuttleConfigCommand.ChannelOffset => ConfigParamID.CFG_CHNL_OFFSET,
            ShuttleConfigCommand.ShuttleLength => ConfigParamID.CFG_SHUTTLE_LEN,
            ShuttleConfigCommand.ShuttleNumber => ConfigParamID.CFG_SHUTTLE_NUM,
            _ => throw new ArgumentOutOfRangeException()
        };
    }
}