using Microsoft.Extensions.Logging;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Net.Sockets;

namespace ShuttleManager.Shared.Services.OtaUpdate;

public sealed class OtaUpdateService : IOtaUpdateService
{
    private readonly ILogger<OtaUpdateService> _logger;

    public OtaUpdateService(ILogger<OtaUpdateService> logger)
        => _logger = logger;

    private const byte CMD_INIT = 0x01;
    private const byte CMD_ERASE = 0x02;
    private const byte CMD_WRITE = 0x03;
    private const byte CMD_RUN = 0x04;
    private const byte CMD_WRITE_STREAM = 0x05;

    private const byte RESP_OK = 0xAA;

    private const int STM_PORT = 8080;
    private const int ESP_PORT = 8081;

    private const uint STM_BASE_ADDRESS = 0x08000000;

    // ================= PUBLIC =================

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
        var sw = Stopwatch.StartNew();

        try
        {
            var result = target == OtaTarget.Stm32
                ? await RunStmAsync(ip, firmware, progress, token, fullErase)
                : await RunEspAsync(ip, firmware, progress, token);

            sw.Stop();

            if (result.IsSuccess)
                _logger.LogInformation("OTA Successful in {Sec}s", sw.Elapsed.TotalSeconds.ToString("F2"));
            else
                _logger.LogError("OTA Failed in {Sec}s: {Error}", sw.Elapsed.TotalSeconds.ToString("F2"), result.Error);

            return result;
        }
        catch (OperationCanceledException)
        {
            return OtaResult.Fail("OTA Cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OTA Critical Error");
            return OtaResult.Fail(ex.Message);
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

        await client.ConnectAsync(ip, STM_PORT, token);
        using var stream = client.GetStream();

        // INIT
        await SendByte(stream, CMD_INIT, token);
        await EnsureOk(stream, token);

        // ERASE
        await SendByte(stream, CMD_ERASE, token);
        await SendByte(stream, fullErase ? (byte)0x01 : (byte)0x00, token);
        await EnsureOk(stream, token);

        // STREAM COMMAND
        await SendByte(stream, CMD_WRITE_STREAM, token);

        var header = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0), STM_BASE_ADDRESS);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), (uint)fw.Length);
        await stream.WriteAsync(header, token);

        await EnsureOk(stream, token);

        // === PHASE 1: UPLOAD (0-70%) ===

        const int chunkSize = 8192;
        int offset = 0;

        while (offset < fw.Length)
        {
            token.ThrowIfCancellationRequested();

            int len = Math.Min(chunkSize, fw.Length - offset);
            await stream.WriteAsync(fw.AsMemory(offset, len), token);

            offset += len;

            double uploadRatio = (double)offset / fw.Length;
            int uiPercent = (int)(uploadRatio * 70);

            progress?.Report(new OtaProgress(uiPercent, 100));
        }

        // === PHASE 2: FLASHING WAIT (70-95%) ===

        progress?.Report(new OtaProgress(70, 100));

        var flashingAnimation = AnimateProgressAsync(progress, 70, 95, 500, token);

        stream.ReadTimeout = 45000;
        await EnsureOk(stream, token);

        await flashingAnimation;

        // === PHASE 3: FINALIZE (95-100%) ===

        progress?.Report(new OtaProgress(95, 100));

        await SendByte(stream, CMD_RUN, token);
        await EnsureOk(stream, token);

        progress?.Report(new OtaProgress(100, 100));

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

        await client.ConnectAsync(ip, ESP_PORT, token);
        using var stream = client.GetStream();

        // INIT
        await SendByte(stream, CMD_INIT, token);

        var sizeBytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(sizeBytes, (uint)fw.Length);
        await stream.WriteAsync(sizeBytes, token);
        await EnsureOk(stream, token);

        // STREAM
        await SendByte(stream, CMD_WRITE_STREAM, token);
        await stream.WriteAsync(sizeBytes, token);
        await EnsureOk(stream, token);

        const int chunkSize = 8192;
        int offset = 0;

        // === PHASE 1: UPLOAD (0-70%) ===
        while (offset < fw.Length)
        {
            token.ThrowIfCancellationRequested();

            int len = Math.Min(chunkSize, fw.Length - offset);
            await stream.WriteAsync(fw.AsMemory(offset, len), token);

            offset += len;

            double ratio = (double)offset / fw.Length;
            int uiPercent = (int)(ratio * 70);

            progress?.Report(new OtaProgress(uiPercent, 100));
        }

        // === PHASE 2: FLASH WAIT ===

        progress?.Report(new OtaProgress(70, 100));
        var flashingAnimation = AnimateProgressAsync(progress, 70, 95, 400, token);

        stream.ReadTimeout = 30000;
        await EnsureOk(stream, token);

        await flashingAnimation;

        // === FINAL ===

        progress?.Report(new OtaProgress(95, 100));

        await SendByte(stream, CMD_RUN, token);
        await EnsureOk(stream, token);

        progress?.Report(new OtaProgress(100, 100));

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

    // ================= HELPERS =================

    private static async Task SendByte(NetworkStream stream, byte value, CancellationToken token)
        => await stream.WriteAsync(new[] { value }, token);

    private static async Task EnsureOk(NetworkStream stream, CancellationToken token)
    {
        var buffer = new byte[1];
        int read = await stream.ReadAsync(buffer, token);

        if (read != 1 || buffer[0] != RESP_OK)
            throw new InvalidOperationException("Device returned FAIL or unexpected response.");
    }
}

public enum OtaPhase
{
    Upload,
    Flashing,
    Finalizing
}

public enum OtaTarget
{
    Stm32,
    Esp32
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