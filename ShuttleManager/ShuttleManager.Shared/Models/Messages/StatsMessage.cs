using ShuttleManager.Shared.Models.Protocol;

namespace ShuttleManager.Shared.Models.Messages;

public class StatsMessage : ShuttleMessageBase
{
    public StatsPacket Data { get; set; }

    public override string ToFormattedTerminalString()
    {
        return
            $"[STATS] TotalDist: {Data.TotalDist / 1000.0}m, " +
            $"Loads: {Data.LoadCounter} " +
            $"Unloads: {Data.UnloadCounter}, " +
            $"Compact: {Data.CompactCounter}, " +
            $"LiftUp: {Data.LiftUpCounter}, " +
            $"LiftDown: {Data.LiftDownCounter}, " +
            $"LifeTimePalletDetected: {Data.LifetimePalletsDetected}, " +
            $"TotalUpTimeMin: {Data.TotalUptimeMinutes}, " +
            $"MotorStall: {Data.MotorStallCount}, " +
            $"LiftOverload: {Data.LifterOverloadCount}, " +
            $"Crash: {Data.CrashCount}, " +
            $"WatchDogRes: {Data.ResetWatchdogCount}, " +
            $"LowBatteryEvents: {Data.LowBatteryEvents}";
    }
}