using ShuttleManager.Shared.Models.Protocol;

namespace ShuttleManager.Shared.Models.Messages;

public class SensorMessage : ShuttleMessageBase
{
    public SensorPacket Data { get; set; }

    public override string ToFormattedTerminalString()
    {
        return
            $"[SENSORS] DistF: {Data.DistanceF}mm, " +
            $"DistR: {Data.DistanceR}mm, " +
            $"DistPlitR: {Data.DistancePltR}, " +
            $"DistPlitF: {Data.DistancePltF}, " +
            $"Temp: {Data.Temperature_dC / 10.0:F1}C, " +
            $"Angle: {Data.Angle * (360.0 / 4096.0):F2}, " +
            $"LifterCurrent: {Data.LifterCurrent}, " +
            $"HardwareFlags: {Data.HardwareFlags}";
    }
}