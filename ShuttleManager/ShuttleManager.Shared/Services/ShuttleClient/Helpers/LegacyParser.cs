using ShuttleManager.Shared.Models;
using ShuttleManager.Shared.Models.Protocol;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ShuttleManager.Shared.Services.ShuttleClient.Helpers;

public static class LegacyParser
{
    // Хранит текущие значения пакетов
    private static TelemetryPacket _telemetry = new();

    private static SensorPacket _sensor = new();

    private static StatsPacket _stats = new();

    // Главный метод парсинга строки
    public static List<ShuttleMessageBase> Parse(string line)
    {
        var messages = new List<ShuttleMessageBase>();
        if (string.IsNullOrWhiteSpace(line)) return messages;

        ParseTelemetry(line, messages);
        ParseSensors(line, messages);
        ParseStats(line, messages);

        return messages;
    }

    #region Telemetry Parsing

    private static void ParseTelemetry(string line, List<ShuttleMessageBase> messages)
    {
        try
        {
            // Batt voltage / Charge
            if (line.Contains("Batt voltage"))
            {
                var match = Regex.Match(line, @"Batt voltage = ([\d.]+)V Charge = (\d+)%");
                if (match.Success)
                {
                    _telemetry.BatteryVoltage_mV = (ushort)(double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) * 1000);
                    _telemetry.BatteryCharge = byte.Parse(match.Groups[2].Value);
                    messages.Add(new TelemetryMessage { Data = _telemetry });
                }
            }

            // Shuttle number / length
            if (line.Contains("Shuttle number"))
            {
                var match = Regex.Match(line, @"Shuttle number = (\d+)\s+Shuttle length = (\d+)");
                if (match.Success)
                {
                    _telemetry.ShuttleNumber = byte.Parse(match.Groups[1].Value);
                    messages.Add(new TelemetryMessage { Data = _telemetry });
                }
            }

            // Status
            if (line.Contains("Status"))
            {
                var match = Regex.Match(line, @"Status = (.+?)\s+\((\d+)\)");
                if (match.Success)
                {
                    _telemetry.ShuttleStatus = (ShuttleState)int.Parse(match.Groups[2].Value);
                    messages.Add(new TelemetryMessage { Data = _telemetry });
                }
            }

            // Inverse / FIFO_LIFO
            if (line.Contains("Inverse") || line.Contains("FIFO_LIFO"))
            {
                if (line.Contains("Inverse = YES")) _telemetry.StateFlags |= 8;
                else _telemetry.StateFlags &= 0xFF - 8;

                if (line.Contains("FIFO_LIFO = LIFO")) _telemetry.StateFlags |= 4;
                else _telemetry.StateFlags &= 0xFF - 4;

                messages.Add(new TelemetryMessage { Data = _telemetry });
            }

            // In channel
            if (line.Contains("In channel"))
            {
                if (line.Contains("YES")) _telemetry.StateFlags |= 16;
                else _telemetry.StateFlags &= 0xFF - 16;

                messages.Add(new TelemetryMessage { Data = _telemetry });
            }

            // Lifter
            if (line.Contains("Lifter"))
            {
                if (line.Contains("UP: YES")) _telemetry.StateFlags |= 1;
                else _telemetry.StateFlags &= 0xFF - 1;

                if (line.Contains("DOWN: YES")) _telemetry.StateFlags |= 2;
                else _telemetry.StateFlags &= 0xFF - 2;

                messages.Add(new TelemetryMessage { Data = _telemetry });
            }

            // Angle / Length / Position
            if (line.Contains("Angle") && line.Contains("Lenght") && line.Contains("position"))
            {
                var match = Regex.Match(line, @"Angle = (\d+)\s*\|\s*Lenght = (\d+)\s*\|\s*position = (\d+)");
                if (match.Success)
                {
                    _telemetry.CurrentPosition = ushort.Parse(match.Groups[3].Value);
                    messages.Add(new TelemetryMessage { Data = _telemetry });
                }
            }

            // Temperature
            if (line.Contains("Temperature"))
            {
                var match = Regex.Match(line, @"Temperature = ([\d.]+)");
                if (match.Success)
                {
                    double temp = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                    _telemetry.StateFlags |= 0; // оставляем для совместимости
                    messages.Add(new TelemetryMessage { Data = _telemetry });
                }
            }
        }
        catch { /* Игнорируем некорректные строки */ }
    }

    #endregion Telemetry Parsing

    #region Sensor Parsing

    private static void ParseSensors(string line, List<ShuttleMessageBase> messages)
    {
        try
        {
            // Forward / Reverse distance
            if (line.Contains("Forwrd dist"))
            {
                var match = Regex.Match(line, @"Forwrd dist = (\d+)\s*\|\s*Revrs dist = (\d+)");
                if (match.Success)
                {
                    _sensor.DistanceF = ushort.Parse(match.Groups[1].Value);
                    _sensor.DistanceR = ushort.Parse(match.Groups[2].Value);
                    messages.Add(new SensorMessage { Data = _sensor });
                }
            }

            // Forward / Reverse pallet distance
            if (line.Contains("Forwrd plt dist"))
            {
                var match = Regex.Match(line, @"Forwrd plt dist = (\d+)\s*\|\s*Revrs plt dist = (\d+)");
                if (match.Success)
                {
                    _sensor.DistancePltF = ushort.Parse(match.Groups[1].Value);
                    _sensor.DistancePltR = ushort.Parse(match.Groups[2].Value);
                    messages.Add(new SensorMessage { Data = _sensor });
                }
            }

            // Pallet detectors
            if (line.Contains("Plt dtchk"))
            {
                var match = Regex.Match(line, @"Plt dtchk (F1|R1) = (\d+)\s*\|\s*Plt dtchk (F2|R2) = (\d+)");
                if (match.Success)
                {
                    var side = match.Groups[1].Value.StartsWith("F") ? "F" : "R";
                    int det1 = int.Parse(match.Groups[2].Value);
                    int det2 = int.Parse(match.Groups[4].Value);

                    if (side == "F")
                    {
                        _sensor.DistancePltF = (ushort)det1;
                        _sensor.DistancePltR = (ushort)det2;
                    }
                    else
                    {
                        _sensor.DistancePltF = (ushort)det1;
                        _sensor.DistancePltR = (ushort)det2;
                    }

                    messages.Add(new SensorMessage { Data = _sensor });
                }
            }

            // Angle
            if (line.Contains("Angle"))
            {
                var match = Regex.Match(line, @"Angle = (\d+)");
                if (match.Success)
                {
                    _sensor.Angle = ushort.Parse(match.Groups[1].Value);
                    messages.Add(new SensorMessage { Data = _sensor });
                }
            }

            // Temperature
            if (line.Contains("Temperature"))
            {
                var match = Regex.Match(line, @"Temperature = ([\d.]+)");
                if (match.Success)
                {
                    _sensor.Temperature_dC = (short)(double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) * 10);
                    messages.Add(new SensorMessage { Data = _sensor });
                }
            }
        }
        catch { /* Игнорируем */ }
    }

    #endregion Sensor Parsing

    #region Stats Parsing

    private static void ParseStats(string line, List<ShuttleMessageBase> messages)
    {
        try
        {
            if (line.Contains("Load counter"))
            {
                var match = Regex.Match(line, @"Load counter = (\d+)");
                if (match.Success)
                {
                    _stats.LoadCounter = uint.Parse(match.Groups[1].Value);
                    messages.Add(new StatsMessage { Data = _stats });
                }
            }

            if (line.Contains("Unload counter"))
            {
                var match = Regex.Match(line, @"Unload counter = (\d+)");
                if (match.Success)
                {
                    _stats.UnloadCounter = uint.Parse(match.Groups[1].Value);
                    messages.Add(new StatsMessage { Data = _stats });
                }
            }
        }
        catch { /* Игнорируем */ }
    }

    #endregion Stats Parsing
}