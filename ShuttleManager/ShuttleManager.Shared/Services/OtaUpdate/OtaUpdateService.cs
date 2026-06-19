using System.Buffers.Binary;
using System.Diagnostics;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using ShuttleManager.Shared.Interfaces;

namespace ShuttleManager.Shared.Services.OtaUpdate;

public sealed class OtaUpdateService : IOtaUpdateService
{
    private const byte CMD_INIT = 0x01;
    private const byte CMD_ERASE = 0x02;
    private const byte CMD_RUN = 0x04;
    private const byte CMD_WRITE_STREAM = 0x05;
    private const byte CMD_WRITE_CHUNK = 0x06;

    private const byte RESP_OK = 0xAA;
    private const byte RESP_FAIL = 0xFF;

    private const int STM_PORT = 8080;
    private const int ESP_PORT = 8081;

    private const uint STM_BASE_ADDRESS = 0x08000000;

    private const int CHUNK_SIZE = 4096;

    private const int MAX_RETRIES = 5;

    private readonly ILogger<OtaUpdateService> _logger;

    public OtaUpdateService(ILogger<OtaUpdateService> logger) => _logger = logger;

    public async Task<OtaResult> RunAsync(
        string ip,
        string filePath,
        OtaTarget target,
        IProgress<OtaProgress>? progress,
        CancellationToken token,
        bool fullErase = false)
    {
        if (!File.Exists(filePath))
            return OtaResult.Fail($"File not found {filePath}");

        if (Path.GetExtension(filePath).ToLower() != ".bin")
            return OtaResult.Fail("Only .bin supported");

        var firmware = await File.ReadAllBytesAsync(filePath, token);
        var stopWatch = Stopwatch.StartNew();

        try
        {
            _logger.LogInformation("Initiating Robust OTA Update for {Target} on {Ip}. File size: {Size} bytes. Full Erase: {FullErase}", target, ip, firmware.Length, fullErase);

            var result = target == OtaTarget.Stm32
                ? await RunStmAsync(ip, firmware, progress, token, fullErase)
                : await RunEspAsync(ip, firmware, progress, token);

            stopWatch.Stop();
            if (result.IsSuccess)
            {
                _logger.LogInformation("OTA Update Successful. Time elapsed: {Elapsed}s", stopWatch.Elapsed.TotalSeconds.ToString("F2"));
            }
            else
            {
                _logger.LogError("OTA Update Failed. Time elapsed: {Elapsed}s. Error: {Error}", stopWatch.Elapsed.TotalSeconds.ToString("F2"), result.Error);
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            return OtaResult.Fail("OTA Cancelled by user");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OTA Update Critical Exception");
            return OtaResult.Fail($"Exception: {ex.Message}");
        }
    }

    // ================= STM =================
    private async Task<OtaResult> RunStmAsync(
        string ip, byte[] fw, IProgress<OtaProgress>? progress, CancellationToken token, bool fullErase)
    {
        // ================= ВЫРАВНИВАНИЕ ПРОШИВКИ (PADDING) =================
        int remainder = fw.Length % 256;
        if (remainder != 0)
        {
            int padding = 256 - remainder;
            byte[] paddedFw = new byte[fw.Length + padding];

            // Копируем оригинальную прошивку
            Array.Copy(fw, paddedFw, fw.Length);

            // Добиваем хвост пустыми байтами (0xFF - стандарт для пустой flash-памяти)
            for (int i = fw.Length; i < paddedFw.Length; i++)
            {
                paddedFw[i] = 0xFF;
            }

            fw = paddedFw;
            _logger.LogInformation("[STM] Firmware padded by {Padding} bytes. Aligned size: {Size}", padding, fw.Length);
        }
        // ===================================================================

        using var client = new TcpClient { NoDelay = true, SendBufferSize = 64 * 1024 };
        await client.ConnectAsync(ip, STM_PORT, token);
        using var stream = client.GetStream();

        // 1. INIT
        stream.ReadTimeout = 5000;
        await SendByte(stream, CMD_INIT, token);
        await EnsureOk(stream, token);

        // 2. ERASE (Теперь просто устанавливает флаг, отвечает мгновенно)
        byte[] erasePayload = [CMD_ERASE, fullErase ? (byte)0x01 : (byte)0x00];
        await stream.WriteAsync(erasePayload, token);
        await EnsureOk(stream, token);

        // ================= ДОБАВИТЬ ДЛЯ ОТЛАДКИ =================
        if (fw.Length >= 8)
        {
            uint sp = BinaryPrimitives.ReadUInt32LittleEndian(fw.AsSpan(0, 4));
            uint rv = BinaryPrimitives.ReadUInt32LittleEndian(fw.AsSpan(4, 4));
            _logger.LogInformation("[STM] DIAGNOSTICS: Stack Pointer = 0x{SP:X8}, Reset Vector = 0x{RV:X8}", sp, rv);

            // Хак для обхода кривой валидации в ESP-IDF, которая ложно бракует SP >= 0x20020000.
            // Сдвигаем Stack Pointer на 4 байта вниз.
            if (sp >= 0x20020000)
            {
                uint patchedSp = 0x2001FFFC;
                BinaryPrimitives.WriteUInt32LittleEndian(fw.AsSpan(0, 4), patchedSp);
                _logger.LogInformation("[STM] Patched Stack Pointer to 0x{SP:X8} to bypass ESP validation bug.", patchedSp);
            }
        }
        // ========================================================

        // 3. ЗАПУСК STREAM (0x05)
        byte[] streamHeader = new byte[9];
        streamHeader[0] = CMD_WRITE_STREAM; // 0x05
        BinaryPrimitives.WriteUInt32LittleEndian(streamHeader.AsSpan(1), STM_BASE_ADDRESS);
        BinaryPrimitives.WriteUInt32LittleEndian(streamHeader.AsSpan(5), (uint)fw.Length);
        await stream.WriteAsync(streamHeader, token);

        // Читаем ПЕРВЫЙ 0xAA (Подтверждение готовности к приему)
        stream.ReadTimeout = 5000;
        await EnsureOk(stream, token);

        // 4. ОТПРАВКА ДАННЫХ
        int offset = 0;
        while (offset < fw.Length)
        {
            token.ThrowIfCancellationRequested();
            int len = Math.Min(CHUNK_SIZE, fw.Length - offset);
            await stream.WriteAsync(fw.AsMemory(offset, len), token);
            offset += len;
            progress?.Report(new OtaProgress(offset, fw.Length));

            await Task.Delay(5, token); // Минимальная задержка, чтобы не забить lwIP
        }

        // ВАЖНО: Ждем ВТОРОЙ 0xAA.
        // В этот момент ESP32 шьет STM32 из своего кэша. Это занимает время!
        _logger.LogInformation("[STM] All data sent. Waiting for ESP32 to flash STM32 (30-60s)...");
        stream.ReadTimeout = 120000; // Таймаут 2 минуты
        await EnsureOk(stream, token);

        // 5. RUN
        stream.ReadTimeout = 5000;
        await SendByte(stream, CMD_RUN, token);
        await EnsureOk(stream, token);

        return OtaResult.Success();
    }

    //// ================= STM =================
    //private async Task<OtaResult> RunStmAsync(
    //    string ip,
    //    byte[] fw,
    //    IProgress<OtaProgress>? progress,
    //    CancellationToken token,
    //    bool fullErase)
    //{
    //    using var client = new TcpClient();
    //    client.NoDelay = true;
    //    client.SendBufferSize = 64 * 1024;

    //    _logger.LogInformation("[STM] Connecting to {Ip}:{Port}...", ip, STM_PORT);
    //    await client.ConnectAsync(ip, STM_PORT, token);
    //    using var stream = client.GetStream();

    //    // 1. INIT
    //    _logger.LogInformation("[STM] Sending CMD_INIT (Entering Bootloader/Syncing)...");
    //    stream.ReadTimeout = 5000;
    //    await SendByte(stream, CMD_INIT, token);
    //    await EnsureOk(stream, token);
    //    _logger.LogInformation("[STM] Bootloader Initialized.");

    //    //// 2. ERASE
    //    //string eraseMode = fullErase ? "MASS ERASE (Deleting Config)" : "Smart Erase (Preserving Config)";
    //    //_logger.LogInformation("[STM] Sending CMD_ERASE ({Mode} - This may take 30-45s)...", eraseMode);
    //    //stream.ReadTimeout = 60000; // Important: Erase takes a long time

    //    //await SendByte(stream, CMD_ERASE, token);
    //    //await SendByte(stream, fullErase ? (byte)0x01 : (byte)0x00, token);
    //    //await EnsureOk(stream, token);
    //    //_logger.LogInformation("[STM] Flash Erased.");

    //    // 2. ERASE
    //    string eraseModeStr = fullErase ? "MASS ERASE (Deleting Config)" : "Smart Erase (Preserving Config)";
    //    _logger.LogInformation("[STM] Sending CMD_ERASE ({Mode} - This may take 30-45s)...", eraseModeStr);
    //    stream.ReadTimeout = 60000; // Important: Erase takes a long time

    //    // Упаковываем команду и режим в один пакет
    //    byte[] erasePayload = [CMD_ERASE, fullErase ? (byte)0x01 : (byte)0x00];
    //    await stream.WriteAsync(erasePayload, token);

    //    await EnsureOk(stream, token);
    //    _logger.LogInformation("[STM] Flash Erased.");

    //    //// 3. CHUNKED WRITE WITH RETRIES
    //    //_logger.LogInformation("[STM] Starting Chunked Firmware Upload (Robust Mode) ...");

    //    //int offset = 0;
    //    //int lastLogPercent = 0;

    //    //while (offset < fw.Length)
    //    //{
    //    //    token.ThrowIfCancellationRequested();

    //    //    int len = Math.Min(CHUNK_SIZE, fw.Length - offset);
    //    //    bool chunkSuccess = false;

    //    //    for (int attempt = 1; attempt <= MAX_RETRIES; attempt++)
    //    //    {
    //    //        try
    //    //        {
    //    //            stream.ReadTimeout = 10000;

    //    //            var header = new byte[7];
    //    //            header[0] = CMD_WRITE_CHUNK;
    //    //            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(1), STM_BASE_ADDRESS + (uint)offset);
    //    //            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(5), (ushort)len);

    //    //            await stream.WriteAsync(header, token);
    //    //            await stream.WriteAsync(fw.AsMemory(offset, len), token);

    //    //            await EnsureOk(stream, token);

    //    //            chunkSuccess = true;
    //    //            break;
    //    //        }
    //    //        catch (Exception ex)
    //    //        {
    //    //            _logger.LogWarning("[STM] Chunk at offset {Offset} failed (Attempt {Attempt}/{Max}): {Msg}", offset, attempt, MAX_RETRIES, ex.Message);
    //    //            if (attempt == MAX_RETRIES)
    //    //                return OtaResult.Fail($"Failed at offset {offset}");
    //    //            await Task.Delay(500, token);
    //    //        }
    //    //    }

    //    //    if (chunkSuccess)
    //    //    {
    //    //        offset += len;
    //    //        progress?.Report(new OtaProgress(offset, fw.Length));

    //    //        int percent = (int)((offset * 100) / fw.Length);
    //    //        if (percent - lastLogPercent >= 10) // Log every 10%
    //    //        {
    //    //            _logger.LogInformation("[STM] Uploading... {Percent}%", percent);
    //    //            lastLogPercent = percent;
    //    //        }
    //    //    }
    //    //}
    //    //_logger.LogInformation("[STM] Upload complete. Sending CMD_RUN (Rebooting target)...");
    //    //stream.ReadTimeout = 10000;
    //    //await SendByte(stream, CMD_RUN, token);
    //    //await EnsureOk(stream, token);

    //    // 3. STREAM WRITE (Совместимо с ESP32 CMD_WRITE_STREAM = 0x05)
    //    _logger.LogInformation("[STM] Starting Stream Firmware Upload...");

    //    // 3.1 Формируем и отправляем заголовок стрима: [Команда 1B] [Адрес 4B] [Длина 4B]
    //    byte[] streamHeader = new byte[9];
    //    streamHeader[0] = CMD_WRITE_STREAM; // 0x05
    //    BinaryPrimitives.WriteUInt32LittleEndian(streamHeader.AsSpan(1), STM_BASE_ADDRESS);
    //    BinaryPrimitives.WriteUInt32LittleEndian(streamHeader.AsSpan(5), (uint)fw.Length);

    //    await stream.WriteAsync(streamHeader, token);

    //    // 3.2 ESP32 должна ответить RESP_OK (0xAA) на получение заголовка
    //    stream.ReadTimeout = 5000;
    //    await EnsureOk(stream, token);
    //    _logger.LogInformation("[STM] Stream header accepted. Sending data...");

    //    // 3.3 Отправляем саму прошивку кусками, чтобы не переполнить TCP-буфер
    //    int offset = 0;
    //    int streamChunkSize = 4096; // Размер куска для отправки по сети
    //    int lastLogPercent = 0;

    //    while (offset < fw.Length)
    //    {
    //        token.ThrowIfCancellationRequested();
    //        int len = Math.Min(streamChunkSize, fw.Length - offset);

    //        await stream.WriteAsync(fw.AsMemory(offset, len), token);
    //        offset += len;

    //        progress?.Report(new OtaProgress(offset, fw.Length));

    //        int percent = (int)((offset * 100) / fw.Length);
    //        if (percent - lastLogPercent >= 10)
    //        {
    //            _logger.LogInformation("[STM] Streaming... {Percent}%", percent);
    //            lastLogPercent = percent;
    //        }

    //        // Небольшая пауза, чтобы ESP32 успевала переваривать данные в UART
    //        await Task.Delay(10, token);
    //    }

    //    //// 3.4 Ждем финального подтверждения от ESP32 после записи всей прошивки
    //    //_logger.LogInformation("[STM] All data sent. Waiting for target to finish flashing...");
    //    //stream.ReadTimeout = 60000; // Прошивка может занять время
    //    //await EnsureOk(stream, token);
    //    //_logger.LogInformation("[STM] Firmware Write Complete!");

    //    //return OtaResult.Success();

    //    // 3.4 Ждем финального подтверждения от ESP32 после записи всей прошивки
    //    _logger.LogInformation("[STM] All data sent. Waiting for target to finish flashing...");
    //    stream.ReadTimeout = 60000; // Прошивка может занять время
    //    await EnsureOk(stream, token);
    //    _logger.LogInformation("[STM] Firmware Write Complete!");

    //    // ================= ДОБАВИТЬ ЭТОТ БЛОК =================
    //    // 4. ЗАПУСК ПРОШИВКИ (REBOOT)
    //    _logger.LogInformation("[STM] Sending CMD_RUN (Rebooting target)...");
    //    stream.ReadTimeout = 10000;
    //    await SendByte(stream, CMD_RUN, token);
    //    await EnsureOk(stream, token);
    //    _logger.LogInformation("[STM] Target Rebooted Successfully.");

    //    //  =======================================================
    //    return OtaResult.Success();
    //}

    //// ================= ESP =================
    //private async Task<OtaResult> RunEspAsync(
    //    string ip,
    //    byte[] fw,
    //    IProgress<OtaProgress>? progress,
    //    CancellationToken token)
    //{
    //    using var client = new TcpClient();
    //    client.NoDelay = true;
    //    client.SendBufferSize = 64 * 1024;

    //    _logger.LogInformation("[ESP] Connecting to {Ip}:{Port}...", ip, ESP_PORT);
    //    await client.ConnectAsync(ip, ESP_PORT, token);
    //    using var stream = client.GetStream();

    //    _logger.LogInformation("[ESP] Sending CMD_INIT (Begin Update)...");
    //    stream.ReadTimeout = 5000;
    //    await SendByte(stream, CMD_INIT, token);

    //    var sizeBytes = new byte[4];
    //    BinaryPrimitives.WriteUInt32LittleEndian(sizeBytes, (uint)fw.Length);
    //    await stream.WriteAsync(sizeBytes, token);
    //    await EnsureOk(stream, token);

    //    _logger.LogInformation("[ESP] Starting Chunked Firmware Upload (Robust Mode)...");

    //    int offset = 0;
    //    int lastLogPercent = 0;

    //    while (offset < fw.Length)
    //    {
    //        token.ThrowIfCancellationRequested();

    //        int len = Math.Min(CHUNK_SIZE, fw.Length - offset);
    //        bool chunkSuccess = false;

    //        for (int attempt = 1; attempt <= MAX_RETRIES; attempt++)
    //        {
    //            try
    //            {
    //                stream.ReadTimeout = 10000;

    //                var header = new byte[7];
    //                header[0] = CMD_WRITE_CHUNK;
    //                BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(1), (uint)offset);
    //                BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(5), (ushort)len);

    //                await stream.WriteAsync(header, token);
    //                await stream.WriteAsync(fw.AsMemory(offset, len), token);

    //                await EnsureOk(stream, token);

    //                chunkSuccess = true;
    //                break;
    //            }
    //            catch (Exception ex)
    //            {
    //                _logger.LogWarning("[ESP] Chunk at offset {Offset} failed (Attempt {Attempt}/{Max}): {Msg}", offset, attempt, MAX_RETRIES, ex.Message);
    //                if (attempt == MAX_RETRIES)
    //                    return OtaResult.Fail($"Failed at offset {offset}");
    //                await Task.Delay(500, token);
    //            }
    //        }

    //        if (chunkSuccess)
    //        {
    //            offset += len;
    //            progress?.Report(new OtaProgress(offset, fw.Length));

    //            int percent = (int)((offset * 100) / fw.Length);
    //            if (percent - lastLogPercent >= 10)
    //            {
    //                _logger.LogInformation("[ESP] Uploading... {Percent}%", percent);
    //                lastLogPercent = percent;
    //            }
    //        }
    //    }

    //    _logger.LogInformation("[ESP] Upload complete. Sending CMD_RUN (Finalizing & Restarting)...");
    //    stream.ReadTimeout = 15000;
    //    await SendByte(stream, CMD_RUN, token);
    //    await EnsureOk(stream, token);

    //    return OtaResult.Success();
    //}
    //// ================= ESP =================
    //private async Task<OtaResult> RunEspAsync(
    //    string ip,
    //    byte[] fw,
    //    IProgress<OtaProgress>? progress,
    //    CancellationToken token)
    //{
    //    using var client = new TcpClient();
    //    client.NoDelay = true;
    //    client.SendBufferSize = 64 * 1024;

    //    _logger.LogInformation("[ESP] Connecting to {Ip}:{Port}...", ip, ESP_PORT);
    //    await client.ConnectAsync(ip, ESP_PORT, token);
    //    using var stream = client.GetStream();

    //    // 1. INIT (Упаковываем команду и размер в один TCP-пакет)
    //    _logger.LogInformation("[ESP] Sending CMD_INIT (Begin Update)...");
    //    stream.ReadTimeout = 5000;

    //    byte[] initPayload = new byte[5];
    //    initPayload[0] = CMD_INIT; // 0x01
    //    BinaryPrimitives.WriteUInt32LittleEndian(initPayload.AsSpan(1), (uint)fw.Length);

    //    await stream.WriteAsync(initPayload, token);
    //    await EnsureOk(stream, token);
    //    _logger.LogInformation("[ESP] Update.begin() successful.");

    //    // 2. СТАРТ ПОТОКА (STREAM WRITE)
    //    _logger.LogInformation("[ESP] Starting Stream Firmware Upload...");

    //    byte[] streamHeader = new byte[5];
    //    streamHeader[0] = CMD_WRITE_STREAM; // 0x05
    //    BinaryPrimitives.WriteUInt32LittleEndian(streamHeader.AsSpan(1), (uint)fw.Length);

    //    await stream.WriteAsync(streamHeader, token);
    //    await EnsureOk(stream, token);
    //    _logger.LogInformation("[ESP] Stream header accepted. Sending data...");

    //    // 3. ОТПРАВКА ДАННЫХ
    //    int offset = 0;
    //    int streamChunkSize = 4096; // Размер блока для передачи по сети
    //    int lastLogPercent = 0;

    //    while (offset < fw.Length)
    //    {
    //        token.ThrowIfCancellationRequested();
    //        int len = Math.Min(streamChunkSize, fw.Length - offset);

    //        await stream.WriteAsync(fw.AsMemory(offset, len), token);
    //        offset += len;

    //        progress?.Report(new OtaProgress(offset, fw.Length));

    //        int percent = (int)((offset * 100) / fw.Length);
    //        if (percent - lastLogPercent >= 10)
    //        {
    //            _logger.LogInformation("[ESP] Streaming... {Percent}%", percent);
    //            lastLogPercent = percent;
    //        }

    //        // Небольшая задержка, чтобы внутренний процесс записи во flash-память ESP32 успевал
    //        await Task.Delay(10, token);
    //    }

    //    // 4. ЗАПУСК ПРОШИВКИ (REBOOT)
    //    _logger.LogInformation("[ESP] All data sent. Sending CMD_RUN (Finalizing & Restarting)...");
    //    stream.ReadTimeout = 15000; // Финализация (Update.end) может занять пару секунд
    //    await SendByte(stream, CMD_RUN, token);
    //    await EnsureOk(stream, token);
    //    _logger.LogInformation("[ESP] Target Rebooted Successfully.");

    //    return OtaResult.Success();
    //}
    // ================= ESP =================
    private async Task<OtaResult> RunEspAsync(
        string ip, byte[] fw, IProgress<OtaProgress>? progress, CancellationToken token)
    {
        using var client = new TcpClient { NoDelay = true, SendBufferSize = 64 * 1024 };
        await client.ConnectAsync(ip, ESP_PORT, token);
        using var stream = client.GetStream();

        // 1. INIT + SIZE
        byte[] initPayload = new byte[5];
        initPayload[0] = CMD_INIT; // 0x01
        BinaryPrimitives.WriteUInt32LittleEndian(initPayload.AsSpan(1), (uint)fw.Length);
        await stream.WriteAsync(initPayload, token);

        // ВАЖНО: Под капотом вызывается esp_ota_begin, который стирает флешку.
        // Это может занимать до 10-15 секунд для больших прошивок.
        _logger.LogInformation("[ESP] Waiting for Flash Erase...");
        stream.ReadTimeout = 30000;
        await EnsureOk(stream, token);

        // 2. ЗАПУСК STREAM (0x05)
        byte[] streamHeader = new byte[5];
        streamHeader[0] = CMD_WRITE_STREAM; // 0x05
        BinaryPrimitives.WriteUInt32LittleEndian(streamHeader.AsSpan(1), (uint)fw.Length);
        await stream.WriteAsync(streamHeader, token);

        // Читаем ПЕРВЫЙ 0xAA
        stream.ReadTimeout = 5000;
        await EnsureOk(stream, token);

        // 3. ОТПРАВКА ДАННЫХ
        int offset = 0;
        while (offset < fw.Length)
        {
            token.ThrowIfCancellationRequested();
            int len = Math.Min(CHUNK_SIZE, fw.Length - offset);
            await stream.WriteAsync(fw.AsMemory(offset, len), token);
            offset += len;
            progress?.Report(new OtaProgress(offset, fw.Length));

            await Task.Delay(5, token);
        }

        // Читаем ВТОРОЙ 0xAA (Подтверждение загрузки всего файла)
        _logger.LogInformation("[ESP] All data sent. Validating image...");
        stream.ReadTimeout = 15000;
        await EnsureOk(stream, token);

        // 4. RUN (Активация загрузочного раздела)
        stream.ReadTimeout = 10000;
        await SendByte(stream, CMD_RUN, token);
        await EnsureOk(stream, token);

        return OtaResult.Success();
    }

    // ================= PROGRESS ANIMATION =================
    private static async Task AnimateProgressAsync(
        IProgress<OtaProgress>? progress,
        int from,
        int to,
        int delayMs,
        CancellationToken token)
    {
        for (int i = from; i <= to; i++)
        {
            progress?.Report(new OtaProgress(i, 100));
            await Task.Delay(delayMs, token);
        }
    }

    // ================= Helpers =================
    private static async Task SendByte(NetworkStream stream, byte value, CancellationToken token)
    {
        var buffer = new byte[] { value };
        await stream.WriteAsync(buffer, token);
    }

    private static async Task EnsureOk(NetworkStream stream, CancellationToken token)
    {
        var buffer = new byte[1];
        int read = await stream.ReadAsync(buffer, token);

        if (read != 1 || buffer[0] != RESP_OK)
        {
            var hex = BitConverter.ToString(buffer);
            var err = (read == 0) ? "No Data / Disconnected" : $"0x{hex}";
            throw new InvalidOperationException($"Device returned FAIL or Unexpected Data: {err}");
        }
    }
}

public enum OtaPhase
{
    /// <summary>
    /// Upload
    /// </summary>
    Upload,

    /// <summary>
    /// Flashing
    /// </summary>
    Flashing,

    /// <summary>
    /// Finale
    /// </summary>
    Finalizing,
}

public sealed record OtaProgress(long Sent, long Total)
{
    public int Percent => (int)((Sent * 100) / Total);
}

public sealed class OtaResult
{
    public bool IsSuccess { get; }
    public string? Error { get; }

    private OtaResult(bool success, string? error)
    {
        IsSuccess = success;
        Error = error;
    }

    public static OtaResult Success() => new(true, null);

    public static OtaResult Fail(string err) => new(false, err);
}

public enum OtaTarget
{
    /// <summary>
    /// STM-32
    /// </summary>
    Stm32,

    /// <summary>
    /// ESP-32
    /// </summary>
    Esp32,
}