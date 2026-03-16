using ShuttleManager.Shared.Models;
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
            ShuttleCommand.LiftUp => $"{shuttleId}dUp___",
            ShuttleCommand.LiftDown => $"{shuttleId}dDown_",
            ShuttleCommand.Calibrate => $"{shuttleId}dClbr_",
            ShuttleCommand.SealForward => $"{shuttleId}dComFo",
            ShuttleCommand.SealBackward => $"{shuttleId}dComBa",
            ShuttleCommand.Left => $"{shuttleId}dLeft_",
            ShuttleCommand.Right => $"{shuttleId}dRight",
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
            ShuttleCommand.SystemReset => CmdType.CMD_SYSTEM_RESET,
            ShuttleCommand.Reset => CmdType.CMD_SYSTEM_RESET,
            ShuttleCommand.SaveConfig => CmdType.CMD_SAVE_EEPROM,
            ShuttleCommand.LiftUp => CmdType.CMD_LIFT_UP,
            ShuttleCommand.LiftDown => CmdType.CMD_LIFT_DOWN,
            ShuttleCommand.Calibrate => CmdType.CMD_CALIBRATE,
            ShuttleCommand.SealForward => CmdType.CMD_COMPACT_F,
            ShuttleCommand.SealBackward => CmdType.CMD_COMPACT_R,
            ShuttleCommand.Left => CmdType.CMD_MOVE_LEFT_MAN,
            ShuttleCommand.Right => CmdType.CMD_MOVE_RIGHT_MAN,
            ShuttleCommand.MoveDistanceBackward => CmdType.CMD_MOVE_DIST_R,
            ShuttleCommand.MoveDistanceForward => CmdType.CMD_MOVE_DIST_F,
            _ => throw new NotSupportedException()
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

            ShuttleConfigCommand.MoveDistanceBackward => $"{shuttleId}dMf{value}", // ok?
            ShuttleConfigCommand.MoveDistanceForward => $"{shuttleId}dMr{value}", // ok?
            ShuttleConfigCommand.DT => $"DT{currentTime:HH:mm:ss dd/MM/yyyy}", // ok?
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
            ShuttleConfigCommand.DistOfEdge => ConfigParamID.CFG_CHNL_OFFSET,
            ShuttleConfigCommand.ShuttleLength => ConfigParamID.CFG_SHUTTLE_LEN,
            ShuttleConfigCommand.ShuttleNumber => ConfigParamID.CFG_SHUTTLE_NUM,
            _ => throw new ArgumentOutOfRangeException()
        };
    }
}