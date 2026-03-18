using ShuttleManager.Shared.Models.Protocol;
using ShuttleManager.Shared.Services.Enums;

namespace ShuttleManager.Shared.Services.ShuttleClient.Command;

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