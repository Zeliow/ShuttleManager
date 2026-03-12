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
            return $"[TELEMETRY] Pos: {Data.CurrentPosition}mm, Spd: {Data.Speed}%, V: {Data.BatteryVoltage_mV / 1000.0:F1}V, Batt: {Data.BatteryCharge}%";
        }
    }

    public class SensorMessage : ShuttleMessageBase
    {
        public SensorPacket Data { get; set; }

        public override string ToFormattedTerminalString()
        {
            return $"[SENSORS] DistF: {Data.DistanceF}mm, DistR: {Data.DistanceR}mm, Temp: {Data.Temperature_dC / 10.0:F1}C";
        }
    }

    public class StatsMessage : ShuttleMessageBase
    {
        public StatsPacket Data { get; set; }

        public override string ToFormattedTerminalString()
        {
            return $"[STATS] TotalDist: {Data.TotalDist}m, Loads: {Data.LoadCounter}";
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