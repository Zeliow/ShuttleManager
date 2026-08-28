using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using ShuttleManager.Shared.Models.Protocol;
using ShuttleManager.Shared.Services.Enums;
using ShuttleManager.Shared.Services.ShuttleClient;
using ShuttleManager.Shared.Services.ShuttleClient.BinaryService;

namespace ShuttleManager.Tests;

public class IpChangeAfterIdSaveTests
{
    private static ShuttleOptions TestOptions() => new()
    {
        ConnectTimeoutMs = 500,
        AckTimeoutMs = 2000,
        ReconnectEnabled = true,
        MaxReconnectAttempts = -1,
        ReconnectBaseDelayMs = 50,
        ReconnectMaxDelayMs = 100,
        WatchdogEnabled = false,
        AutoRebootAfterIdSave = true,
        AutoRebootDelayMs = 100,
        IdRules =
        [
            new IpToIdRule
            {
                BaseIp = "127.0.0",
                StartOctet = 1,
                Ids = ["A1", "B2", "C3", "D4", "E5", "F6", "G7", "H8", "I9"],
            },
        ],
    };

    /// <summary>
    /// Эмулятор шаттла с бинарным протоколом: заявляет протокол heartbeat'ом,
    /// отвечает ACK на CONFIG_SET и CMD_SIMPLE; после ACK сохранения — «перезагружается» (закрывает соединение).
    /// </summary>
    private static async Task RunShuttleEmulatorAsync(TcpListener listener, CancellationToken ct, bool rebootAfterSaveAck)
    {
        using TcpClient client = await listener.AcceptTcpClientAsync(ct);
        using NetworkStream stream = client.GetStream();

        byte[] heartbeat = BinaryFrameCodec.BuildFrame(
            MsgID.MSG_HEARTBEAT,
            ProtocolConstants.TARGET_ID_NONE,
            0,
            new TelemetryPacket { CurrentPosition = 1 });
        await stream.WriteAsync(heartbeat, ct);

        var receiveBuffer = new MemoryStream();
        byte[] readBuf = new byte[1024];

        while (!ct.IsCancellationRequested)
        {
            int read = await stream.ReadAsync(readBuf, ct);
            if (read == 0)
                return;

            receiveBuffer.Write(readBuf, 0, read);

            byte[] data = receiveBuffer.ToArray();
            int offset = 0;
            bool processed = false;

            while (offset < data.Length)
            {
                FrameParseResult result = BinaryFrameCodec.TryParseFrame(data, offset, out ParsedFrame frame, out int nextOffset);
                if (result == FrameParseResult.NoSync)
                    break;

                if (result == FrameParseResult.Incomplete)
                {
                    offset = nextOffset;
                    break;
                }

                offset = nextOffset;
                processed = true;

                if (result != FrameParseResult.Ok)
                    continue;

                bool isConfigSet = frame.MsgId == (byte)MsgID.MSG_CONFIG_SET;
                bool isSimpleCmd = frame.MsgId == (byte)MsgID.MSG_CMD_SIMPLE;
                if (isConfigSet || isSimpleCmd)
                {
                    var ack = new AckPacket { RefSeq = frame.Seq, Result = AckResult.ACK_OK };
                    byte[] ackFrame = BinaryFrameCodec.BuildFrame(MsgID.MSG_ACK, ProtocolConstants.TARGET_ID_NONE, frame.Seq, in ack);
                    await stream.WriteAsync(ackFrame, ct);

                    if (rebootAfterSaveAck && isSimpleCmd)
                        return; // сохранение в EEPROM → перезагрузка контроллера
                }
            }

            if (processed)
            {
                receiveBuffer.SetLength(0);
                if (offset < data.Length)
                    receiveBuffer.Write(data, offset, data.Length - offset);
            }
        }
    }

    [Fact]
    public async Task NumberChangeThenSave_ReconnectsToPredictedNewIp()
    {
        var listenerOld = new TcpListener(IPAddress.Parse("127.0.0.1"), 0);
        listenerOld.Start();
        int port = ((IPEndPoint)listenerOld.LocalEndpoint).Port;

        // Номер 2 (B2) → IP = StartOctet(1) + 2 = 3 → 127.0.0.3.
        var listenerNew = new TcpListener(IPAddress.Parse("127.0.0.3"), port);
        listenerNew.Start();

        var logs = new CollectingLoggerFactory();
        using var service = new ShuttleHubClientService(logs, Options.Create(TestOptions()));
        var connectedIps = new List<string>();
        var disconnectedIps = new List<string>();
        service.Connected += (ip, _) => connectedIps.Add(ip);
        service.Disconnected += ip => disconnectedIps.Add(ip);

        using var emuOldCts = new CancellationTokenSource(20000);
        Task emuOld = RunShuttleEmulatorAsync(listenerOld, emuOldCts.Token, rebootAfterSaveAck: true);
        using var emuNewCts = new CancellationTokenSource(20000);
        Task emuNew = RunShuttleEmulatorAsync(listenerNew, emuNewCts.Token, rebootAfterSaveAck: false);

        try
        {
            Assert.True(await service.ConnectToShuttleAsync("127.0.0.1", port));

            // Протокол определяется асинхронно после первого heartbeat — повторяем до готовности.
            var idleSw = Stopwatch.StartNew();
            bool idChanged = false;
            while (!idChanged && idleSw.ElapsedMilliseconds < 5000)
            {
                idChanged = await service.SendConfigAsync("127.0.0.1", ShuttleConfigCommand.ShuttleNumber, 2);
                if (!idChanged)
                    await Task.Delay(100);
            }

            Assert.True(idChanged, "Смена номера не прошла: протокол не определён или ACK не получен");

            // Сохранение в EEPROM → контроллер перезагружается, старый IP умирает.
            bool saved = false;
            var saveSw = Stopwatch.StartNew();
            while (!saved && saveSw.ElapsedMilliseconds < 5000)
            {
                saved = await service.SendCommandAsync("127.0.0.1", ShuttleCommand.SaveConfig);
                if (!saved)
                    await Task.Delay(100);
            }

            Assert.True(saved, "Сохранение не прошло: ACK не получен");

            // Ждём Connected для нового IP.
            var sw = Stopwatch.StartNew();
            while (!connectedIps.Contains("127.0.0.3") && sw.ElapsedMilliseconds < 8000)
            {
                await Task.Delay(50);
            }

            Assert.True(
                connectedIps.Contains("127.0.0.3"),
                $"Реконнект к новому IP не произошёл.\nConnected: [{string.Join(", ", connectedIps)}]\nDisconnected: [{string.Join(", ", disconnectedIps)}]\nЛоги:\n{string.Join("\n", logs.Entries)}");

            Assert.Contains("127.0.0.1", disconnectedIps);
            Assert.Contains(service.GetConnectedShuttles(), s => s.IPAddress == "127.0.0.3");
            Assert.DoesNotContain(service.GetConnectedShuttles(), s => s.IPAddress == "127.0.0.1");
        }
        finally
        {
            await service.DisconnectAsync("127.0.0.3");
            await service.DisconnectAsync("127.0.0.1");
            listenerOld.Stop();
            listenerNew.Stop();

            emuOldCts.Cancel();
            emuNewCts.Cancel();
            try
            {
                await emuOld;
            }
            catch (OperationCanceledException)
            {
            }

            try
            {
                await emuNew;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    [Fact]
    public async Task NumberChangeWithoutSave_NoIpSwitchOnReconnect()
    {
        var listener = new TcpListener(IPAddress.Parse("127.0.0.1"), 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var options = TestOptions();
        options.AutoRebootAfterIdSave = false;
        using var service = new ShuttleHubClientService(null, Options.Create(options));
        var connectedIps = new List<string>();
        service.Connected += (ip, _) => connectedIps.Add(ip);

        using var emuCts = new CancellationTokenSource(20000);
        Task emu = RunShuttleEmulatorAsync(listener, emuCts.Token, rebootAfterSaveAck: false);

        try
        {
            Assert.True(await service.ConnectToShuttleAsync("127.0.0.1", port));

            bool idChanged = await service.SendConfigAsync("127.0.0.1", ShuttleConfigCommand.ShuttleNumber, 2);
            Assert.True(idChanged);

            // Номер изменён, но сохранения не было — реконнект (если случится) идёт на старый IP.
            // Здесь просто проверяем, что соединение живо и адрес не поменялся.
            Assert.Single(service.GetConnectedShuttles());
            Assert.Equal("127.0.0.1", service.GetConnectedShuttles()[0].IPAddress);
        }
        finally
        {
            await service.DisconnectAsync("127.0.0.1");
            listener.Stop();

            emuCts.Cancel();
            try
            {
                await emu;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}
