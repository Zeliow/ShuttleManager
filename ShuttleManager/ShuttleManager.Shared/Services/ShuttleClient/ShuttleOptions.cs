namespace ShuttleManager.Shared.Services.ShuttleClient;

/// <summary>Правило маппинга IP-адреса в идентификатор шаттла.</summary>
public class IpToIdRule
{
    /// <summary>Префикс сети, например "192.168.40".</summary>
    public string BaseIp { get; set; } = string.Empty;

    /// <summary>Первый октет диапазона, с которого начинаются индексы Ids.</summary>
    public int StartOctet { get; set; }

    /// <summary>Идентификаторы шаттлов по порядку октетов.</summary>
    public List<string> Ids { get; set; } = [];
}

/// <summary>Конфигурация сервисов ShuttleManager (секция "Shuttle" в appsettings.json).</summary>
public class ShuttleOptions
{
    public const string SectionName = "Shuttle";

    public int DefaultPort { get; set; } = 23;

    public int ConnectTimeoutMs { get; set; } = 5000;

    public int AckTimeoutMs { get; set; } = 1000;

    public int KeepAliveTimeSeconds { get; set; } = 5;

    public int KeepAliveIntervalSeconds { get; set; } = 5;

    public int KeepAliveRetryCount { get; set; } = 1;

    public bool ReconnectEnabled { get; set; } = true;

    /// <summary>Максимум попыток реконнекта; -1 = без ограничения.</summary>
    public int MaxReconnectAttempts { get; set; } = -1;

    public int ReconnectBaseDelayMs { get; set; } = 1000;

    public int ReconnectMaxDelayMs { get; set; } = 30000;

    public bool WatchdogEnabled { get; set; } = true;

    public int WatchdogTimeoutMs { get; set; } = 15000;

    /// <summary>Автоматически отправлять перезагрузку контроллера после сохранения изменённого номера шаттла.</summary>
    public bool AutoRebootAfterIdSave { get; set; } = true;

    /// <summary>Задержка перед авто-перезагрузкой (дать EEPROM завершить запись), мс.</summary>
    public int AutoRebootDelayMs { get; set; } = 800;

    public int ScanTimeoutMs { get; set; } = 1000;

    public int ScanMaxParallelism { get; set; } = 32;

    public List<IpToIdRule> IdRules { get; set; } =
    [
        new IpToIdRule
        {
            BaseIp = "192.168.40",
            StartOctet = 130,
            Ids =
            [
                "A1", "B2", "C3", "D4", "E5", "F6", "G7", "H8", "I9",
                "10", "11", "12", "13", "14", "15", "16", "17", "18", "19", "20",
                "21", "22", "23", "24", "25", "26", "27", "28", "29", "30", "31", "32",
            ],
        },
    ];

    /// <summary>
    /// Определяет идентификатор шаттла (буквенно-цифровое выражение вида "A1", "B2")
    /// по его числовому номеру. Формат важен: legacy-команды начинаются с префикса этого номера.
    /// </summary>
    public string GetShuttleIdByNumber(int number)
    {
        foreach (IpToIdRule rule in IdRules)
        {
            int index = number - 1;
            if (index >= 0 && index < rule.Ids.Count)
                return rule.Ids[index];
        }

        return number.ToString();
    }

    /// <summary>
    /// Определяет ожидаемый IP-адрес шаттла по его числовому номеру:
    /// адрес жёстко привязан к номеру как BaseIp.(StartOctet + number).
    /// Возвращает null, если текущий IP не принадлежит ни одному правилу.
    /// </summary>
    public string? GetIpAddressByNumber(string currentIpAddress, int number)
    {
        foreach (IpToIdRule rule in IdRules)
        {
            if (currentIpAddress.StartsWith(rule.BaseIp + ".", StringComparison.Ordinal))
                return $"{rule.BaseIp}.{rule.StartOctet + number}";
        }

        return null;
    }

    /// <summary>Определяет идентификатор шаттла по IP-адресу.</summary>
    public string ResolveShuttleId(string ipAddress)
    {
        foreach (IpToIdRule rule in IdRules)
        {
            if (!ipAddress.StartsWith(rule.BaseIp + ".", StringComparison.Ordinal))
                continue;

            string octetPart = ipAddress.Substring(rule.BaseIp.Length + 1);
            if (!int.TryParse(octetPart, out int lastOctet))
                break;

            if (lastOctet < rule.StartOctet)
                break;

            // Историческое поведение: октет StartOctet и StartOctet+1 дают Ids[0],
            // дальше по порядку: octet -> Ids[octet - StartOctet - 1].
            int index = lastOctet - rule.StartOctet - 1;
            if (index < 0)
                index = 0;

            if (index < rule.Ids.Count)
                return rule.Ids[index];

            break;
        }

        int fallbackOctet = 0;
        string fallbackPart = ipAddress.Substring(ipAddress.LastIndexOf('.') + 1);
        if (int.TryParse(fallbackPart, out fallbackOctet))
            return fallbackOctet.ToString();

        return ipAddress;
    }
}
