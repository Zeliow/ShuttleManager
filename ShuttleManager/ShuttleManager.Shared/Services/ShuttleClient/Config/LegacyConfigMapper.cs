using ShuttleManager.Shared.Services.Enums;

namespace ShuttleManager.Shared.Services.ShuttleClient.Config;

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