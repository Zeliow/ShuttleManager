using ShuttleManager.Shared.Models.Protocol;

namespace ShuttleManager.Shared.Models
{
    public abstract class ShuttleMessageBase
    {
        public abstract string ToFormattedTerminalString();
    }

    public class TelemetryMessage : ShuttleMessageBase
    {
        public TelemetryPacket Data { get; set; }

        public override string ToFormattedTerminalString()
        {
            return $"[TELEMETRY] Position: {Data.CurrentPosition}mm, " +
                $"Speed: {Data.Speed}%, " +
                $"BatteryVoltage: {Data.BatteryVoltage_mV / 1000.0:F1}V, " +
                $"BatteryCharge: {Data.BatteryCharge}%, " +
                $"Error Code: {Data.ErrorCode}, " +
                $"StateFlags: {Data.StateFlags}, " +
                $"ShuttleStatus: {Data.ShuttleStatus}, " +
                $"ShuttleNumber: {Data.ShuttleNumber}, " +
                $"PalletCount: {Data.PalletCount}";
        }
    }

    public class SensorMessage : ShuttleMessageBase
    {
        public SensorPacket Data { get; set; }

        public override string ToFormattedTerminalString()
        {
            return $"[SENSORS] DistF: {Data.DistanceF}mm, " +
                $"DistR: {Data.DistanceR}mm, " +
                $"DistPlitR: {Data.DistancePltR}, " +
                $"DistPlitF: {Data.DistancePltF}, " +
                $"Temp: {Data.Temperature_dC / 10.0:F1}C, " +
                $"Angle: {Data.Angle * (360.0 / 4096.0):F2}, " +
                $"LifterCurrent: {Data.LifterCurrent}, " +
                $"HardwareFlags: {Data.HardwareFlags}";
        }
    }

    public class StatsMessage : ShuttleMessageBase
    {
        public StatsPacket Data { get; set; }

        public override string ToFormattedTerminalString()
        {
            return $"[STATS] TotalDist: {Data.TotalDist / 1000.0}m, " +
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
                $"WatchDogRes: {Data.WatchdogResets}, " +
                $"LowBatteryEvents: {Data.LowBatteryEvents}";
        }
    }

    public class RawLogMessage : ShuttleMessageBase
    {
        public LogLevel Level { get; set; }
        public string Text { get; set; } = string.Empty;

        public override string ToFormattedTerminalString()
        {
            return $"[{Level}] {Text}";
        }
    }

    public class ConfigMessage : ShuttleMessageBase
    {
        public ConfigPacket Data { get; set; }

        public override string ToFormattedTerminalString()
        {
            return $"[CONFIG] Param: {(ConfigParamID)Data.ParamID}, Value: {Data.Value}";
        }
    }

    public class AckMessage : ShuttleMessageBase
    {
        public AckPacket Data { get; set; }

        public override string ToFormattedTerminalString()
        {
            return $"[ACK] RefSeq: {Data.RefSeq}, Result: {Data.Result}";
        }
    }
}