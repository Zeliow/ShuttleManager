using ShuttleManager.Shared.Interfaces;
using ShuttleManager.Shared.Models;
using ShuttleManager.Shared.Models.Protocol;
using ShuttleManager.Shared.Services.Enums;
using ShuttleManager.Shared.Services.ShuttleClient.Command;
using ShuttleManager.Shared.Services.ShuttleClient.Config;
using ShuttleManager.Shared.Services.ShuttleClient.Helpers;
using System.Diagnostics;
using System.Text;

namespace ShuttleManager.Shared.Services.ShuttleClient.LegacyService;

public class LegacyProtocolHandler : IShuttleProtocolHandler
{
    private readonly ProtocolCallbacks _callbacks;

    public ShuttleProtocolType Protocol => ShuttleProtocolType.Legacy;

    public LegacyProtocolHandler(ProtocolCallbacks callbacks)
    {
        _callbacks = callbacks;
    }

    // Разбор буфера Legacy (строковый протокол)
    public void ProcessBuffer(ShuttleConnection connection)
    {
        var data = connection.ReceiveBuffer.ToArray();
        int start = 0;

        while (true)
        {
            int newline = Array.IndexOf(data, (byte)'\n', start);
            if (newline < 0) break;

            int length = newline - start;
            var line = Encoding.UTF8.GetString(data, start, length).Trim();

            HandleLegacyLine(connection, line);

            start = newline + 1;
        }

        // Обновляем буфер на остаток
        connection.ReceiveBuffer.SetLength(0);
        if (start < data.Length)
            connection.ReceiveBuffer.Write(data, start, data.Length - start);
    }

    private void HandleLegacyLine(ShuttleConnection connection, string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;

        // Парсим строку в один или несколько сообщений
        var messages = LegacyParser.Parse(line);

        // Всегда добавляем raw пакет
        var raw = new RawLogMessage
        {
            Level = LogLevel.LOG_INFO,
            Text = line
        };
        messages.Add(raw);

        foreach (var msg in messages)
        {
            _callbacks.OnMessage?.Invoke(connection.IpAddress, msg);
        }
    }

    private async Task<bool> SendPacketAsync(ShuttleConnection connection, string text, CancellationToken cancellationToken)
    {
        if (!text.EndsWith("\n")) text += "\n";
        var data = Encoding.UTF8.GetBytes(text);
        Debug.WriteLine($"command text: {text}");
        Debug.WriteLine($"command data: {data}");
        await connection.Transport!.WriteAsync(data, cancellationToken);
        await connection.Transport.FlushAsync(cancellationToken);

        return true;
    }

    public async Task<bool> SendCommandAsync(ShuttleConnection connection, ShuttleCommand cmd, int arg1, int arg2, CancellationToken ct, int timeoutMs = 1000)
    {
        if (connection.Transport == null) return false;

        var text = LegacyCommandMapper.Map(connection.ShuttleId, cmd, arg1);

        return await SendPacketAsync(connection, text, ct);
    }

    public async Task<bool> SendConfigAsync(ShuttleConnection connection, ShuttleConfigCommand param, int value, int timeoutMs = 1000)
    {
        if (param == ShuttleConfigCommand.ShuttleNumber)
            connection.ShuttleId = Convert.ToString(value);

        var text = LegacyConfigMapper.Map(connection.ShuttleId, param, value);

        return await SendPacketAsync(connection, text, CancellationToken.None);
    }

    public async Task<bool> SendDateTimeAsync(ShuttleConnection connection, DateTime utcTime, int timeoutMs = 1000)
    {
        var cmd = $"DT{utcTime:HH:mm:ss dd/MM/yyyy}\n";

        return await SendPacketAsync(connection, cmd, CancellationToken.None);
    }

    public async Task<bool> SendManualCommandAsync(
        ShuttleConnection connection,
        string rawCommand,
        CancellationToken ct,
        int timeoutMs = 1000)
    {
        if (connection.Transport == null) return false;

        return await SendPacketAsync(connection, rawCommand, ct);
    }
}