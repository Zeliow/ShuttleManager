using ShuttleManager.Shared.Models;
using ShuttleManager.Shared.Models.Protocol;
using System.Globalization;

namespace ShuttleManager.Shared.Services.ShuttleClient.Parsing;

public static class LegacyParser
{
    public static ShuttleMessageBase? Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        var tokens = line.Split('|', StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length == 0)
            return null;

        var packet = new TelemetryPacket();

        bool hasData = false;

        foreach (var token in tokens)
        {
            var parts = token.Split('=', 2, StringSplitOptions.TrimEntries);

            if (parts.Length != 2)
                continue;

            var key = parts[0].ToLowerInvariant();
            var value = parts[1];

            if (!ushort.TryParse(value, out ushort number))
                continue;

            switch (key)
            {
                case "position":
                    packet.CurrentPosition = number;
                    hasData = true;
                    break;

                case "speed":
                    packet.Speed = number;
                    hasData = true;
                    break;

                case "battery":
                case "batt":
                    packet.BatteryCharge = (byte)number;
                    hasData = true;
                    break;

                case "voltage":
                case "volt":
                    packet.BatteryVoltage_mV = number;
                    hasData = true;
                    break;

                case "angle":
                    // если нужно можно сохранить
                    break;

                case "lenght":
                case "length":
                    // если нужно
                    break;
            }
        }

        if (!hasData)
            return null;

        return new TelemetryMessage
        {
            Data = packet
        };
    }
}