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
    private const byte CMD_WRITE_CHUNK = 0x06;

    private const byte RESP_OK = 0xAA;
    private const byte RESP_FAIL = 0xFF;

    private const int STM_PORT = 8080;
    private const int ESP_PORT = 8081;

    private const uint STM_BASE_ADDRESS = 0x08000000;
    private const uint STM_CONFIG_SECTOR_BASE = 0x08060000;
    private const uint STM_SMART_ERASE_LIMIT = STM_CONFIG_SECTOR_BASE - STM_BASE_ADDRESS;

    private const int CHUNK_SIZE = 4096;

    private const int MAX_RETRIES = 5;

    private const int CONNECT_TIMEOUT_MS = 10000;
    private const int INIT_RESPONSE_TIMEOUT_MS = 5000;
    private const int ERASE_RESPONSE_TIMEOUT_MS = 60000;
    private const int STM_CHUNK_RESPONSE_TIMEOUT_MS = 30000;
    private const int ESP_CHUNK_RESPONSE_TIMEOUT_MS = 10000;
    private const int STM_RUN_RESPONSE_TIMEOUT_MS = 10000;
    private const int ESP_RUN_RESPONSE_TIMEOUT_MS = 15000;

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
        if (firmware.Length == 0)
            return OtaResult.Fail("Firmware file is empty");

        var stopWatch = Stopwatch.StartNew();

        try
        {
            _logger.LogInformation(
                "Initiating Robust OTA Update for {Target} on {Ip}. File size: {Size} bytes. Full Erase: {FullErase}",
                target,
                ip,
                firmware.Length,
                fullErase);

            OtaResult result = target == OtaTarget.Stm32
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
        string ip,
        byte[] fw,
        IProgress<OtaProgress>? progress,
        CancellationToken token,
        bool fullErase)
    {
        if (!fullErase && fw.Length > STM_SMART_ERASE_LIMIT)
        {
            return OtaResult.Fail(
                $"STM firmware size {fw.Length} bytes overlaps preserved config sector at 0x{STM_CONFIG_SECTOR_BASE:X8}; enable full erase or move config storage.");
        }

        (TcpClient client, NetworkStream stream) = await OpenConnectionAsync(ip, STM_PORT, token);
        using (client)
        {
            // 1. INIT
            _logger.LogInformation("[STM] Sending CMD_INIT (Entering Bootloader/Syncing)...");
            stream.ReadTimeout = 5000;
            await SendByte(stream, CMD_INIT, token);
            await EnsureOk(stream, token, INIT_RESPONSE_TIMEOUT_MS);
            _logger.LogInformation("[STM] Bootloader Initialized.");

            // 2. ERASE
            string eraseMode = fullErase ? "MASS ERASE (Deleting Config)" : "Smart Erase (Preserving Config)";
            _logger.LogInformation("[STM] Sending CMD_ERASE ({Mode} - This may take 30-45s)...", eraseMode);
            stream.ReadTimeout = ERASE_RESPONSE_TIMEOUT_MS;

            byte[] erasePayload = [CMD_ERASE, fullErase ? (byte)0x01 : (byte)0x00];
            await stream.WriteAsync(erasePayload, token);

            await EnsureOk(stream, token, ERASE_RESPONSE_TIMEOUT_MS);
            _logger.LogInformation("[STM] Flash Erased.");

            // 3. CHUNKED WRITE WITH RETRIES
            OtaResult upload = await UploadFirmwareAsync(
                stream,
                fw,
                STM_BASE_ADDRESS,
                STM_CHUNK_RESPONSE_TIMEOUT_MS,
                "STM",
                progress,
                token);
            if (!upload.IsSuccess)
                return upload;

            // 4. RUN
            _logger.LogInformation("[STM] Sending CMD_RUN (Rebooting target)...");
            stream.ReadTimeout = STM_RUN_RESPONSE_TIMEOUT_MS;
            await SendByte(stream, CMD_RUN, token);
            await EnsureOk(stream, token, STM_RUN_RESPONSE_TIMEOUT_MS);
            _logger.LogInformation("[STM] Target Rebooted Successfully.");

            return OtaResult.Success();
        }
    }

    // ================= ESP =================
    private async Task<OtaResult> RunEspAsync(
        string ip,
        byte[] fw,
        IProgress<OtaProgress>? progress,
        CancellationToken token)
    {
        (TcpClient client, NetworkStream stream) = await OpenConnectionAsync(ip, ESP_PORT, token);
        using (client)
        {
            // 1. INIT (команда и размер в одном TCP-пакете)
            _logger.LogInformation("[ESP] Sending CMD_INIT (Begin Update)...");
            stream.ReadTimeout = 5000;

            byte[] initPayload = new byte[5];
            initPayload[0] = CMD_INIT;
            BinaryPrimitives.WriteUInt32LittleEndian(initPayload.AsSpan(1), (uint)fw.Length);

            await stream.WriteAsync(initPayload, token);
            await EnsureOk(stream, token, INIT_RESPONSE_TIMEOUT_MS);
            _logger.LogInformation("[ESP] Update.begin() successful.");

            // 2. CHUNKED WRITE WITH RETRIES
            OtaResult upload = await UploadFirmwareAsync(
                stream,
                fw,
                0,
                ESP_CHUNK_RESPONSE_TIMEOUT_MS,
                "ESP",
                progress,
                token);
            if (!upload.IsSuccess)
                return upload;

            // 3. ЗАПУСК ПРОШИВКИ (REBOOT)
            _logger.LogInformation("[ESP] All data sent. Sending CMD_RUN (Finalizing & Restarting)...");
            stream.ReadTimeout = ESP_RUN_RESPONSE_TIMEOUT_MS;
            await SendByte(stream, CMD_RUN, token);
            await EnsureOk(stream, token, ESP_RUN_RESPONSE_TIMEOUT_MS);
            _logger.LogInformation("[ESP] Target Rebooted Successfully.");

            return OtaResult.Success();
        }
    }

    // ================= Общая загрузка чанками =================
    private async Task<OtaResult> UploadFirmwareAsync(
        NetworkStream stream,
        byte[] fw,
        uint baseAddress,
        int chunkResponseTimeoutMs,
        string target,
        IProgress<OtaProgress>? progress,
        CancellationToken token)
    {
        _logger.LogInformation("[{Target}] Starting Chunked Firmware Upload...", target);

        int offset = 0;
        int lastLogPercent = 0;

        while (offset < fw.Length)
        {
            token.ThrowIfCancellationRequested();
            int len = Math.Min(CHUNK_SIZE, fw.Length - offset);
            bool chunkSuccess = false;

            for (int attempt = 1; attempt <= MAX_RETRIES; attempt++)
            {
                try
                {
                    stream.ReadTimeout = 10000;

                    var header = new byte[7];
                    header[0] = CMD_WRITE_CHUNK;
                    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(1), baseAddress + (uint)offset);
                    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(5), (ushort)len);

                    await stream.WriteAsync(header, token);
                    await stream.WriteAsync(fw.AsMemory(offset, len), token);

                    await EnsureOk(stream, token, chunkResponseTimeoutMs);

                    chunkSuccess = true;
                    break;
                }
                catch (Exception ex) when (attempt < MAX_RETRIES)
                {
                    _logger.LogWarning(
                        "[{Target}] Chunk at offset {Offset} failed (Attempt {Attempt}/{Max}): {Msg}",
                        target,
                        offset,
                        attempt,
                        MAX_RETRIES,
                        ex.Message);
                    await Task.Delay(500, token);
                }
            }

            if (!chunkSuccess)
                return OtaResult.Fail($"Failed at offset {offset}");

            offset += len;
            progress?.Report(new OtaProgress(offset, fw.Length));

            int percent = (int)((offset * 100) / fw.Length);
            if (percent - lastLogPercent >= 10)
            {
                _logger.LogInformation("[{Target}] Uploading... {Percent}%", target, percent);
                lastLogPercent = percent;
            }
        }

        _logger.LogInformation("[{Target}] Firmware Write Complete!", target);
        return OtaResult.Success();
    }

    // ================= Helpers =================
    private static async Task<(TcpClient Client, NetworkStream Stream)> OpenConnectionAsync(
        string ip,
        int port,
        CancellationToken token)
    {
        var client = new TcpClient
        {
            NoDelay = true,
            SendBufferSize = 64 * 1024,
        };

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeoutCts.CancelAfter(CONNECT_TIMEOUT_MS);

            await client.ConnectAsync(ip, port, timeoutCts.Token);
            return (client, client.GetStream());
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            client.Dispose();
            throw new TimeoutException($"Timed out connecting to {ip}:{port} after {CONNECT_TIMEOUT_MS} ms");
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private static async Task SendByte(NetworkStream stream, byte value, CancellationToken token)
    {
        var buffer = new byte[] { value };
        await stream.WriteAsync(buffer, token);
    }

    private static async Task EnsureOk(NetworkStream stream, CancellationToken token, int timeoutMs)
    {
        var buffer = new byte[1];
        int read;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeoutCts.CancelAfter(timeoutMs);

        try
        {
            read = await stream.ReadAsync(buffer, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException($"Timed out waiting for OTA response after {timeoutMs} ms");
        }

        if (read == 1 && buffer[0] == RESP_OK)
            return;

        if (read == 1 && buffer[0] == RESP_FAIL)
            throw new InvalidOperationException("Device returned FAIL");

        var err = (read == 0) ? "No Data / Disconnected" : $"0x{BitConverter.ToString(buffer)}";
        throw new InvalidOperationException($"Device returned FAIL or Unexpected Data: {err}");
    }
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