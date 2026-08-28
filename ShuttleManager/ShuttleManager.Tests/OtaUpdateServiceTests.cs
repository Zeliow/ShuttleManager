using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using ShuttleManager.Shared.Services.OtaUpdate;

namespace ShuttleManager.Tests;

public class OtaUpdateServiceTests
{
    private static readonly byte[] AckOk = [0xAA];

    private sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }

    private static async Task ReadExactlyAsync(NetworkStream stream, byte[] buffer, int count)
    {
        int received = 0;
        while (received < count)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(received, count - received));
            if (read == 0)
                throw new EndOfStreamException("Bootloader closed connection unexpectedly");

            received += read;
        }
    }

    private static async Task RunStmBootloaderAsync(TcpListener listener, int expectedFwSize, CancellationToken ct)
    {
        using TcpClient client = await listener.AcceptTcpClientAsync(ct);
        using NetworkStream stream = client.GetStream();
        var header = new byte[7];

        // INIT: 1 байт
        await ReadExactlyAsync(stream, header, 1);
        await stream.WriteAsync(AckOk);

        // ERASE: 2 байта
        await ReadExactlyAsync(stream, header, 2);
        await stream.WriteAsync(AckOk);

        // Чанки: заголовок 7 байт + payload
        var chunk = new byte[4096];
        int receivedTotal = 0;
        while (receivedTotal < expectedFwSize)
        {
            await ReadExactlyAsync(stream, header, 7);
            ushort len = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(5));
            await ReadExactlyAsync(stream, chunk, len);
            await stream.WriteAsync(AckOk);
            receivedTotal += len;
        }

        // RUN: 1 байт
        await ReadExactlyAsync(stream, header, 1);
        await stream.WriteAsync(AckOk);
    }

    [Fact]
    public async Task RunAsync_StmBootloaderEmulation_SucceedsAndReportsProgress()
    {
        const int stmPort = 8080;

        // Если порт занят (параллельные процессы/тесты) — пропускаем.
        TcpListener? listener = null;
        try
        {
            listener = new TcpListener(IPAddress.Loopback, stmPort);
            listener.Start();
        }
        catch (SocketException)
        {
            return;
        }

        try
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"ota-test-{Guid.NewGuid():N}.bin");
            try
            {
                const int fwSize = 10000;
                byte[] firmware = new byte[fwSize];
                new Random(42).NextBytes(firmware);
                await File.WriteAllBytesAsync(tempFile, firmware);

                using var bootloaderCts = new CancellationTokenSource(30000);
                Task bootloaderTask = RunStmBootloaderAsync(listener, fwSize, bootloaderCts.Token);

                var progressReports = new List<OtaProgress>();
                var progress = new SynchronousProgress<OtaProgress>(progressReports.Add);

                var service = new OtaUpdateService(NullLogger<OtaUpdateService>.Instance);
                OtaResult result = await service.RunAsync(
                    "127.0.0.1",
                    tempFile,
                    OtaTarget.Stm32,
                    progress,
                    CancellationToken.None,
                    fullErase: false);

                Assert.True(result.IsSuccess, result.Error);

                await bootloaderTask;

                Assert.Equal(fwSize, progressReports[^1].Sent);
                Assert.Equal(100, progressReports[^1].Percent);
            }
            finally
            {
                File.Delete(tempFile);
            }
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task RunAsync_MissingFile_ReturnsFail()
    {
        var service = new OtaUpdateService(NullLogger<OtaUpdateService>.Instance);

        OtaResult result = await service.RunAsync(
            "127.0.0.1",
            Path.Combine(Path.GetTempPath(), "does-not-exist-ota-test.bin"),
            OtaTarget.Stm32,
            null,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("File not found", result.Error);
    }

    [Fact]
    public async Task RunAsync_NonBinExtension_ReturnsFail()
    {
        string tempFile = Path.GetTempFileName() + ".hex";
        await File.WriteAllTextAsync(tempFile, "not a firmware");

        try
        {
            var service = new OtaUpdateService(NullLogger<OtaUpdateService>.Instance);

            OtaResult result = await service.RunAsync(
                "127.0.0.1",
                tempFile,
                OtaTarget.Stm32,
                null,
                CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Contains("Only .bin supported", result.Error);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
