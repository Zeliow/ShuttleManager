using ShuttleManager.Shared.Models;
using ShuttleManager.Shared.Models.Protocol;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ShuttleManager.Shared.Services.ShuttleClient.Parsing;

public static class LegacyParser
{
    // Хранит текущие значения пакетов
    private static TelemetryPacket _telemetry = new();

    private static SensorPacket _sensor = new();
    private static StatsPacket _stats = new();

    // Парсинг одной строки — возвращает список сообщений
    public static List<ShuttleMessageBase> Parse(string line)
    {
        var result = new List<ShuttleMessageBase>();
        if (string.IsNullOrWhiteSpace(line)) return result;

        ParseTelemetry(line, result);
        ParseSensors(line, result);
        ParseStats(line, result);

        return result;
    }

    private static void ParseTelemetry(string line, List<ShuttleMessageBase> result)
    {
        // Batt voltage / Charge
        if (line.Contains("Batt voltage"))
        {
            try
            {
                var voltagePart = line.Split('V')[0].Split('=')[1].Trim();
                _telemetry.BatteryVoltage_mV = (ushort)(double.Parse(voltagePart, CultureInfo.InvariantCulture) * 1000);

                var chargeIndex = line.IndexOf("Charge", StringComparison.Ordinal);
                if (chargeIndex >= 0)
                {
                    var chargeStr = line.Substring(chargeIndex).Split('=')[1].Replace("%", "").Trim();
                    _telemetry.BatteryCharge = byte.Parse(chargeStr);
                }

                result.Add(new TelemetryMessage { Data = _telemetry });
            }
            catch { /* игнорируем кривые строки */ }
        }

        // Status
        if (line.Contains("Status"))
        {
            int start = line.IndexOf("(");
            int end = line.IndexOf(")");
            if (start > 0 && end > start)
            {
                if (int.TryParse(line.Substring(start + 1, end - (start + 1)), out int state))
                    _telemetry.ShuttleStatus = (ShuttleState)state;

                result.Add(new TelemetryMessage { Data = _telemetry });
            }
        }

        // Inverse / FIFO_LIFO
        if (line.Contains("Inverse") || line.Contains("FIFO_LIFO"))
        {
            if (line.Contains("Inverse = YES"))
                _telemetry.StateFlags |= 8;
            else
                _telemetry.StateFlags &= 0xFF - 8;

            if (line.Contains("FIFO_LIFO = LIFO"))
                _telemetry.StateFlags |= 4;
            else
                _telemetry.StateFlags &= 0xFF - 4;

            result.Add(new TelemetryMessage { Data = _telemetry });
        }

        // In channel
        if (line.Contains("In channel"))
        {
            if (line.Contains("YES"))
                _telemetry.StateFlags |= 16;
            else
                _telemetry.StateFlags &= 0xFF - 16;

            result.Add(new TelemetryMessage { Data = _telemetry });
        }

        // Lifter
        if (line.Contains("Lifter"))
        {
            if (line.Contains("UP: YES")) _telemetry.StateFlags |= 1;
            else _telemetry.StateFlags &= 0xFF - 1;

            if (line.Contains("DOWN: YES")) _telemetry.StateFlags |= 2;
            else _telemetry.StateFlags &= 0xFF - 2;

            result.Add(new TelemetryMessage { Data = _telemetry });
        }
    }

    private static void ParseSensors(string line, List<ShuttleMessageBase> result)
    {
        try
        {
            // Forward / Reverse distance
            if (line.Contains("Forwrd dist"))
            {
                var parts = line.Split('|');
                _sensor.DistanceF = ushort.Parse(parts[0].Split('=')[1].Trim());
                _sensor.DistanceR = ushort.Parse(parts[1].Split('=')[1].Trim());

                result.Add(new SensorMessage { Data = _sensor });
            }

            // Forward / Reverse pallet distance
            if (line.Contains("Forwrd plt dist"))
            {
                var parts = line.Split('|');
                _sensor.DistancePltF = ushort.Parse(parts[0].Split('=')[1].Trim());
                _sensor.DistancePltR = ushort.Parse(parts[1].Split('=')[1].Trim());

                result.Add(new SensorMessage { Data = _sensor });
            }

            // Angle
            if (line.Contains("Angle"))
            {
                _sensor.Angle = ushort.Parse(line.Split('=')[1].Trim());
                result.Add(new SensorMessage { Data = _sensor });
            }

            // Temperature
            if (line.Contains("Temperature"))
            {
                double temp = double.Parse(line.Split('=')[1].Trim(), CultureInfo.InvariantCulture);
                _sensor.Temperature_dC = (short)(temp * 10);
                result.Add(new SensorMessage { Data = _sensor });
            }
        }
        catch { /* игнорируем ошибки парсинга */ }
    }

    private static void ParseStats(string line, List<ShuttleMessageBase> result)
    {
        try
        {
            if (line.Contains("Load counter"))
            {
                _stats.LoadCounter = uint.Parse(line.Split('=')[1].Trim());
                result.Add(new StatsMessage { Data = _stats });
            }

            if (line.Contains("Unload counter"))
            {
                _stats.UnloadCounter = uint.Parse(line.Split('=')[1].Trim());
                result.Add(new StatsMessage { Data = _stats });
            }
        }
        catch { /* игнорируем */ }
    }
}