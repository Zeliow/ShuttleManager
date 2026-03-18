using ShuttleManager.Shared.Services.Enums;

namespace ShuttleManager.Shared.Services.ShuttleClient.Command;

public static class LegacyCommandMapper
{
    public static string Map(string shuttleId, ShuttleCommand cmd, int arg)
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
            ShuttleCommand.MoveDistanceBackward => $"{shuttleId}dMf{arg}",
            ShuttleCommand.MoveDistanceForward => $"{shuttleId}dMr{arg}",
            _ => throw new NotSupportedException()
        };
    }
}