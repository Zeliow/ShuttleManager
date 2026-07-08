using ShuttleManager.Shared.Models.Protocol;

namespace ShuttleManager.Shared.Models.Messages;

public class TelemetryMessage : ShuttleMessageBase
{
    public TelemetryPacket Data { get; set; }

    public override string ToFormattedTerminalString()
    {
        return
            $"[TELEMETRY] Position: {Data.CurrentPosition}mm, " +
            $"Speed: {Data.Speed}%, " +
            $"BatteryVoltage: {Data.BatteryVoltage_mV / 1000.0:F1}V, " +
            $"BatteryCharge: {Data.BatteryCharge}%, " +
            $"Error Code: {Data.ErrorCode}, " +
            $"Warning Code: {Data.WarningCode}, " +
            $"StateFlags: {Data.StateFlags}, " +
            $"ShuttleStatus: {Data.ShuttleStatus}, " +
            $"ShuttleNumber: {Data.ShuttleNumber}, " +
            $"PalletCount: {Data.PalletCount}";
    }
}