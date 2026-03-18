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
        string ip,
        byte[] fw,
        IProgress<OtaProgress>? progress,
        CancellationToken token,
        bool fullErase)
    {
        using var client = new TcpClient();
        client.NoDelay = true;
        client.SendBufferSize = 64 * 1024;

        _logger.LogInformation("[STM] Connecting to {Ip}:{Port}...", ip, STM_PORT);
        await client.ConnectAsync(ip, STM_PORT, token);
        using var stream = client.GetStream();

        // 1. INIT
        _logger.LogInformation("[STM] Sending CMD_INIT (Entering Bootloader/Syncing)...");
        stream.ReadTimeout = 5000;
        await SendByte(stream, CMD_INIT, token);
        await EnsureOk(stream, token);
        _logger.LogInformation("[STM] Bootloader Initialized.");

        // 2. ERASE
        string eraseMode = fullErase ? "MASS ERASE (Deleting Config)" : "Smart Erase (Preserving Config)";
        _logger.LogInformation("[STM] Sending CMD_ERASE ({Mode} - This may take 30-45s)...", eraseMode);
        stream.ReadTimeout = 60000; // Important: Erase takes a long time

        await SendByte(stream, CMD_ERASE, token);
        await SendByte(stream, fullErase ? (byte)0x01 : (byte)0x00, token);
        await EnsureOk(stream, token);
        _logger.LogInformation("[STM] Flash Erased.");

        // 3. CHUNKED WRITE WITH RETRIES
        _logger.LogInformation("[STM] Starting Chunked Firmware Upload (Robust Mode) ...");

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
                    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(1), STM_BASE_ADDRESS + (uint)offset);
                    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(5), (ushort)len);

                    await stream.WriteAsync(header, token);
                    await stream.WriteAsync(fw.AsMemory(offset, len), token);

                    await EnsureOk(stream, token);

                    chunkSuccess = true;
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("[STM] Chunk at offset {Offset} failed (Attempt {Attempt}/{Max}): {Msg}", offset, attempt, MAX_RETRIES, ex.Message);
                    if (attempt == MAX_RETRIES)
                        return OtaResult.Fail($"Failed at offset {offset}");
                    await Task.Delay(500, token);
                }
            }

            if (chunkSuccess)
            {
                offset += len;
                progress?.Report(new OtaProgress(offset, fw.Length));

                int percent = (int)((offset * 100) / fw.Length);
                if (percent - lastLogPercent >= 10) // Log every 10%
                {
                    _logger.LogInformation("[STM] Uploading... {Percent}%", percent);
                    lastLogPercent = percent;
                }
            }
        }

        _logger.LogInformation("[STM] Upload complete. Sending CMD_RUN (Rebooting target)...");
        stream.ReadTimeout = 10000;
        await SendByte(stream, CMD_RUN, token);
        await EnsureOk(stream, token);

        return OtaResult.Success();
    }

    // ================= ESP =================
    private async Task<OtaResult> RunEspAsync(
        string ip,
        byte[] fw,
        IProgress<OtaProgress>? progress,
        CancellationToken token)
    {
        using var client = new TcpClient();
        client.NoDelay = true;
        client.SendBufferSize = 64 * 1024;

        _logger.LogInformation("[ESP] Connecting to {Ip}:{Port}...", ip, ESP_PORT);
        await client.ConnectAsync(ip, ESP_PORT, token);
        using var stream = client.GetStream();

        _logger.LogInformation("[ESP] Sending CMD_INIT (Begin Update)...");
        stream.ReadTimeout = 5000;
        await SendByte(stream, CMD_INIT, token);

        var sizeBytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(sizeBytes, (uint)fw.Length);
        await stream.WriteAsync(sizeBytes, token);
        await EnsureOk(stream, token);

        _logger.LogInformation("[ESP] Starting Chunked Firmware Upload (Robust Mode)...");

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
                    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(1), (uint)offset);
                    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(5), (ushort)len);

                    await stream.WriteAsync(header, token);
                    await stream.WriteAsync(fw.AsMemory(offset, len), token);

                    await EnsureOk(stream, token);

                    chunkSuccess = true;
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("[ESP] Chunk at offset {Offset} failed (Attempt {Attempt}/{Max}): {Msg}", offset, attempt, MAX_RETRIES, ex.Message);
                    if (attempt == MAX_RETRIES)
                        return OtaResult.Fail($"Failed at offset {offset}");
                    await Task.Delay(500, token);
                }
            }

            if (chunkSuccess)
            {
                offset += len;
                progress?.Report(new OtaProgress(offset, fw.Length));

                int percent = (int)((offset * 100) / fw.Length);
                if (percent - lastLogPercent >= 10)
                {
                    _logger.LogInformation("[ESP] Uploading... {Percent}%", percent);
                    lastLogPercent = percent;
                }
            }
        }

        _logger.LogInformation("[ESP] Upload complete. Sending CMD_RUN (Finalizing & Restarting)...");
        stream.ReadTimeout = 15000;
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