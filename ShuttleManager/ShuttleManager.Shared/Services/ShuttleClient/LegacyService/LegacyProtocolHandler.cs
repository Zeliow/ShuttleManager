using ShuttleManager.Shared.Models;
using ShuttleManager.Shared.Models.Protocol;
using ShuttleManager.Shared.Services.Enums;
using ShuttleManager.Shared.Services.ShuttleClient.Parsing;
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

        // Отправляем через колбэк
        foreach (var msg in messages)
        {
            _callbacks.OnMessage?.Invoke(connection.IpAddress, msg);
        }
    }

    public async Task<bool> SendCommandAsync(ShuttleConnection connection, string command, CancellationToken ct, int timeoutMs = 1000)
    {
        if (connection.Transport == null) return false;

        if (!command.EndsWith("\n")) command += "\n";
        var data = Encoding.UTF8.GetBytes(command);

        await connection.Transport.WriteAsync(data, ct);
        await connection.Transport.FlushAsync(ct);

        return true;
    }

    public Task<bool> SendCommandAsync(ShuttleConnection connection, CmdType cmd, int arg1, int arg2, int timeoutMs = 1000)
    {
        // Legacy не использует бинарные команды
        throw new NotSupportedException("LegacyProtocolHandler does not support binary commands");
    }
}