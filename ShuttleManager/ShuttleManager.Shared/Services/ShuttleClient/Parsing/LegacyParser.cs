using ShuttleManager.Shared.Models;
using ShuttleManager.Shared.Models.Protocol;
using System.Globalization;

namespace ShuttleManager.Shared.Services.ShuttleClient.Parsing;

public class LegacyParser
{
    public static ShuttleMessageBase? Parse(string line)
    {
        line = line.Trim();

        if (line.StartsWith("Batt"))
        {
            var match = BattRegex().Match(line);
            if (match.Success)
            {
                return new TelemetryMessage
                {
                    Data = new TelemetryPacket
                    {
                        BatteryVoltage_mV = (ushort)(
                            double.Parse(match.Groups[1].Value,
                            CultureInfo.InvariantCulture) * 1000),

                        BatteryCharge = byte.Parse(match.Groups[2].Value)
                    }
                };
            }
        }

        if (line.StartsWith("Temperature"))
        {
            var match = TempRegex().Match(line);

            if (match.Success)
            {
                return new SensorMessage
                {
                    Data = new SensorPacket
                    {
                        Temperature_dC = (short)(
                            double.Parse(match.Groups[1].Value,
                            CultureInfo.InvariantCulture) * 10)
                    }
                };
            }
        }

        return null;
    }
}