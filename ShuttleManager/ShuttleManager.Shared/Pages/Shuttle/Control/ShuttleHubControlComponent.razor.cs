using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using ShuttleManager.Shared.Interfaces;
using ShuttleManager.Shared.Services;

namespace ShuttleManager.Shared.Pages.Shuttle.Control;

public partial class ShuttleHubControlComponent : IAsyncDisposable
{
    [Inject]
    private IJSRuntime JSRuntime { get; set; } = default!;

    [Inject]
    private IShuttleHubClientService HubClientService { get; set; } = default!;

    [Inject]
    private ILogger<ShuttleHubControlComponent> Logger { get; set; } = default!;

    [Inject]
    private IWebBrowserService WebBrowserService { get; set; } = default!;

    [Inject]
    private IFilePickerService FilePickerService { get; set; } = default!;

    [Inject]
    private IOtaUpdateService OtaService { get; set; } = default!;

    [Parameter]
    public Models.Shuttle Shuttle { get; set; } = null!;

    [Parameter]
    public EventCallback<string> OnDisconnected { get; set; }

    private string[] shuttleNums = { "A1", "B2", "C3", "D4", "E5", "F6", "G7", "H8", "I9", "10", "11", "12", "13", "14", "15", "16", "17", "18", "19", "20", "21", "22", "23", "24", "25", "26", "27", "28", "29", "30", "31", "32" };

    private int _shuttleNumberInput = 1;
    private int _moveDistanceBackwardInput = 0;
    private int _moveDistanceForwardInput = 0;
    private int _distanceOfEdge = 0;
    private int _lenghtOfShuttle = 800;
    private int _maxSpeedInput = 0;
    private int _minPowerInput = 0;
    private int _pallentDistance = 0;
    private static bool IsAndroid => OperatingSystem.IsAndroid();

    private readonly Channel<string> _logChannel = Channel.CreateUnbounded<string>();
    private readonly List<string> _statusBlockLines = new();

    private string _manualCommand = string.Empty;
    private string _componentId = Guid.NewGuid().ToString();
    private bool _isCommandInProgress;
    private int _connectionAttempts = 0;
    private CancellationTokenSource _componentCts = new();
    private bool _isFullErased = false;

    private string CurrentStatus => Shuttle.CurrentStatus;
    private int BatteryPercentageValue => Shuttle.BatteryPercentage;
    private string LastActivityTime => Shuttle.LastActivity.ToString("HH:mm:ss");
    private string Uptime => (DateTime.Now - Shuttle.ConnectionTime).ToString(@"hh\:mm\:ss");
    private string BatteryData => $"Батарея: {Shuttle.BatteryVoltage:F1}V | Заряд: {Shuttle.BatteryPercentage}%";
    private bool IsConnected => Shuttle.IsConnected;
    private string ComponentId => _componentId;
    private string ManualCommand { get => _manualCommand; set => _manualCommand = value; }
    private string pathLogShuttle = string.Empty;

    private bool _inStatusBlock = false;

    //Ota Update
    private string? _selectedFile;

    private int _otaPercent;
    private bool _isOtaRunning;
    private string _otaStatus = string.Empty;
    private CancellationTokenSource? _otaCts;

    [GeneratedRegex(@"^CB\s+(\d+)$")]
    private static partial Regex CbRegex();

    [GeneratedRegex(@"Batt voltage = ([\d.]+)V Charge = (\d+)% limit = (\d+)%")]
    private static partial Regex BattRegex();

    [GeneratedRegex(@"Inverse = (YES|NO)")]
    private static partial Regex InverseRegex();

    [GeneratedRegex(@"Status = (.+?)\s+\((\d+)\)")]
    private static partial Regex StatusRegex();

    [GeneratedRegex(@"MPR = (\d+)\s+max speed = (\d+)")]
    private static partial Regex MprRegex();

    [GeneratedRegex(@"Shuttle number = (\d+)\s+Shuttle length = (\d+)")]
    private static partial Regex ShuttleInfoRegex();

    [GeneratedRegex(@"Temperature = ([\d.]+)")]
    private static partial Regex TempRegex();

    [GeneratedRegex(@"Angle = (\d+)\s*\|\s*Lenght = (\d+)\s*\|\s*position = (\d+)")]
    private static partial Regex AngleRegex();

    [GeneratedRegex(@"FIFO_LIFO = (FIFO|LIFO)")]
    private static partial Regex FifoLifoRegex();

    [GeneratedRegex(@"Forwrd dist = (\d+)\s*\|\s*Revrs dist = (\d+)")]
    private static partial Regex DistRegex();

    [GeneratedRegex(@"Forwrd plt dist = (\d+)\s*\|\s*Revrs plt dist = (\d+)")]
    private static partial Regex PltDistRegex();

    [GeneratedRegex(@"Plt dtchk (F1|R1) = (\d+)\s*\|\s*Plt dtchk (F2|R2) = (\d+)")]
    private static partial Regex PltDetRegex();

    [GeneratedRegex(@"In channel:\s+(YES|NO)")]
    private static partial Regex InChanRegex();

    [GeneratedRegex(@"Lifter UP:\s+(YES|NO)\s*\|\s*Lifter DOWN:\s+(YES|NO)")]
    private static partial Regex LifterRegex();

    [GeneratedRegex(@"Bumper forward = (\d+)\s*\|\s*Bumper reverse = (\d+)")]
    private static partial Regex BumperRegex();

    [GeneratedRegex(@"Zero point MPR = (\d+)\s+channel offset = (\d+)")]
    private static partial Regex ZeroOffRegex();

    [GeneratedRegex(@"Wait time on unload = (\d+)\s+Sec")]
    private static partial Regex WaitTimeRegex();

    protected override async Task OnInitializedAsync()
    {
        try
        {
            if (!Directory.Exists($"{AppContext.BaseDirectory}/logs"))
            {
                Directory.CreateDirectory($"{AppContext.BaseDirectory}/logs");
            }

            Logger.LogInformation("+++ Component INIT: Subscribing to events for {IP}", Shuttle.IPAddress);
            HubClientService.Connected += OnHubConnected;
            HubClientService.Disconnected += OnHubDisconnected;
            HubClientService.LogReceived += OnLogReceived;
            pathLogShuttle = Path.Combine($"{AppContext.BaseDirectory}/logs", $"Shuttle_Log_Number_{DateTime.Now.Day}_{DateTime.Now.Month}_{DateTime.Now.Year}_{Shuttle.IPAddress}.txt");
            Logger.LogInformation("Компонент инициализирован для {IpAddress}", Shuttle.IPAddress);

            // Start the background log processing loop to throttle UI updates
            _ = ProcessLogsLoopAsync();

            StateHasChanged();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Ошибка инициализации компонента для {IpAddress}", Shuttle.IPAddress);
            LogToTerminal($"[ERROR] Ошибка инициализации: {ex.Message}\n");
        }
    }

    private async Task StartOta(OtaTarget target)
    {
        await ChoiceFile();

        if (_selectedFile == null)
            return;

        _isOtaRunning = true;
        _otaPercent = 0;
        _otaStatus = "Starting OTA...";
        _otaCts = new CancellationTokenSource();

        var progress = new Progress<OtaProgress>(p =>
        {
            _otaPercent = p.Percent;
            InvokeAsync(StateHasChanged);
        });

        try
        {
            var result = await OtaService.RunAsync(
                Shuttle.IPAddress!,
                _selectedFile,
                target,
                progress,
                _otaCts.Token,
                _isFullErased);

            _otaStatus = result.IsSuccess ? "OTA Completed" : $"Error: {result.Error}";
        }
        catch (Exception ex)
        {
            _otaStatus = $"Error: {ex.Message}";
            Logger.LogError(ex, "Ошибка OTA для {IpAddress}", Shuttle.IPAddress);
        }
        finally
        {
            _otaCts?.Dispose();
            _otaCts = null;
            _isOtaRunning = false;
            StateHasChanged();
        }
    }

    private void CancelOta()
    {
        _otaCts?.Cancel();
    }

    private string SeeCorrentNumberShuttle(string numberShuttle)
    {
        if (string.IsNullOrEmpty(numberShuttle))
            return string.Empty;
        if (char.IsDigit(numberShuttle[0]))
        {
            return numberShuttle;
        }
        else
        {
            return numberShuttle.Substring(1);
        }
    }

    private bool IsEventForThisShuttle(string ipAddress)
    {
        return ipAddress == Shuttle.IPAddress;
    }

    private async void OnHubConnected(string ip, int id)
    {
        if (!IsEventForThisShuttle(ip))
            return;

        await InvokeAsync(() =>
        {
            Shuttle.IsConnected = true;
            Shuttle.ConnectionTime = DateTime.Now;
            Shuttle.LastActivity = DateTime.Now;
            LogToTerminal($"[SUCCESS] Подключено к шаттлу ID: {id}\n");
            StateHasChanged();
        });
    }

    private async void OnHubDisconnected(string ip)
    {
        if (!IsEventForThisShuttle(ip))
            return;

        await InvokeAsync(() =>
        {
            Shuttle.IsConnected = false;
            LogToTerminal("[WARNING] Соединение разорвано\n");
            StateHasChanged();
            _ = OnDisconnected.InvokeAsync(ip);
        });
    }

    private void ParseAndHandleResponse(string response)
    {
        var line = response.Trim();

        if (line.StartsWith("CB"))
        {
            var match = CbRegex().Match(line);
            if (match.Success && int.TryParse(match.Groups[1].Value, out int batteryPercentageScr))
            {
                Shuttle.BatteryPercentage = batteryPercentageScr;
                Logger.LogInformation($"Parsed CB: Battery {batteryPercentageScr}%");
            }
        }
        else if (line.StartsWith("Batt"))
        {
            var match = BattRegex().Match(line);
            if (match.Success)
            {
                if (double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double batteryVoltage) &&
                    int.TryParse(match.Groups[2].Value, out int batteryPercentage) &&
                    int.TryParse(match.Groups[3].Value, out int batteryLimit))
                {
                    Shuttle.BatteryVoltage = batteryVoltage;
                    Shuttle.BatteryPercentage = batteryPercentage;
                    Shuttle.BatteryLimit = batteryLimit;
                    Logger.LogInformation($"Parsed Batt: {batteryVoltage}V {batteryPercentage}% {batteryLimit}%");
                }
            }
        }
        else if (line.StartsWith("Inverse"))
        {
            var match = InverseRegex().Match(line);
            if (match.Success)
            {
                Shuttle.Inverse = match.Groups[1].Value == "YES";
                Logger.LogInformation($"Parsed Inverse: {Shuttle.Inverse}");
            }
        }
        else if (line.StartsWith("Status"))
        {
            var match = StatusRegex().Match(line);
            if (match.Success)
            {
                Shuttle.CurrentStatus = match.Groups[1].Value.Trim();
                if (int.TryParse(match.Groups[2].Value, out int statusCode))
                {
                    Shuttle.StatusCode = statusCode;
                }

                Logger.LogInformation($"Parsed Status: {Shuttle.CurrentStatus} ({Shuttle.StatusCode})");
            }
        }
        else if (line.StartsWith("MPR"))
        {
            var match = MprRegex().Match(line);
            if (match.Success)
            {
                if (int.TryParse(match.Groups[1].Value, out int interPalletDistance) &&
                    int.TryParse(match.Groups[2].Value, out int maxSpeed))
                {
                    Shuttle.InterPalleteDistance = interPalletDistance;
                    Shuttle.MaxSpeed = maxSpeed;
                    Logger.LogInformation($"Parsed MPR/MaxSp: MPR={interPalletDistance}, MaxSp={maxSpeed}");
                }
            }
        }
        else if (line.StartsWith("Shuttle number"))
        {
            var match = ShuttleInfoRegex().Match(line);
            if (match.Success)
            {
                if (int.TryParse(match.Groups[1].Value, out int shuttleNumber) &&
                    int.TryParse(match.Groups[2].Value, out int shuttleLength))
                {
                    Shuttle.ShuttleLength = shuttleLength;
                    Logger.LogInformation($"Parsed Shuttle: Num={Shuttle.ShuttleNumber}, Len={shuttleLength}");
                }
            }
        }
        else if (line.StartsWith("Temperature"))
        {
            var match = TempRegex().Match(line);
            if (match.Success)
            {
                if (double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double temperature))
                {
                    Shuttle.Temperature = temperature;
                    Logger.LogInformation($"Parsed Temp: {temperature}°C");
                }
            }
        }
        else if (line.StartsWith("Angle"))
        {
            var match = AngleRegex().Match(line);
            if (match.Success)
            {
                if (int.TryParse(match.Groups[1].Value, out int angle) &&
                    int.TryParse(match.Groups[2].Value, out int length) &&
                    int.TryParse(match.Groups[3].Value, out int position))
                {
                    Shuttle.Angle = angle;
                    Shuttle.Length = length;
                    Shuttle.Position = position;
                    Logger.LogInformation($"Parsed Angle/Length/Pos: {angle}/{length}/{position}");
                }
            }
        }
        else if (line.StartsWith("FIFO_LIFO"))
        {
            var match = FifoLifoRegex().Match(line);
            if (match.Success)
            {
                Shuttle.FifoLifoMode = match.Groups[1].Value;
                Logger.LogInformation($"Parsed FIFO/LIFO: {Shuttle.FifoLifoMode}");
            }
        }
        else if (line.StartsWith("Forwrd dist"))
        {
            var match = DistRegex().Match(line);
            if (match.Success)
            {
                if (int.TryParse(match.Groups[1].Value, out int forwardDist) &&
                    int.TryParse(match.Groups[2].Value, out int reverseDist))
                {
                    Shuttle.ForwardDistance = forwardDist;
                    Shuttle.ReverseDistance = reverseDist;
                    Logger.LogInformation($"Parsed Dist F/R: {forwardDist}/{reverseDist}");
                }
            }
        }
        else if (line.StartsWith("Forwrd plt dist"))
        {
            var match = PltDistRegex().Match(line);
            if (match.Success)
            {
                if (int.TryParse(match.Groups[1].Value, out int forwardPalletDist) &&
                    int.TryParse(match.Groups[2].Value, out int reversePalletDist))
                {
                    Shuttle.ForwardPalletDistance = forwardPalletDist;
                    Shuttle.ReversePalletDistance = reversePalletDist;
                    Logger.LogInformation($"Parsed Pallet Dist F/R: {forwardPalletDist}/{reversePalletDist}");
                }
            }
        }
        else if (line.StartsWith("Plt dtchk"))
        {
            var match = PltDetRegex().Match(line);
            if (match.Success)
            {
                string side = match.Groups[1].Value.Substring(0, 1);
                if (int.TryParse(match.Groups[2].Value, out int detector1) &&
                    int.TryParse(match.Groups[4].Value, out int detector2))
                {
                    if (side == "F")
                    {
                        Shuttle.PalletDetectorFront1 = detector1;
                        Shuttle.PalletDetectorFront2 = detector2;
                    }
                    else if (side == "R")
                    {
                        Shuttle.PalletDetectorRear1 = detector1;
                        Shuttle.PalletDetectorRear2 = detector2;
                    }

                    Logger.LogInformation($"Parsed Pallet Det {side}: {detector1}/{detector2}");
                }
            }
        }
        else if (line.StartsWith("In channel"))
        {
            var match = InChanRegex().Match(line);
            if (match.Success)
            {
                Shuttle.IsInChannel = match.Groups[1].Value == "YES";
                Logger.LogInformation($"Parsed In Channel: {Shuttle.IsInChannel}");
            }
        }
        else if (line.StartsWith("Lifter"))
        {
            var match = LifterRegex().Match(line);
            if (match.Success)
            {
                Shuttle.IsLifterUp = match.Groups[1].Value == "YES";
                Shuttle.IsLifterDown = match.Groups[2].Value == "YES";
                Logger.LogInformation($"Parsed Lifter: UP={Shuttle.IsLifterUp}, DOWN={Shuttle.IsLifterDown}");
            }
        }
        else if (line.StartsWith("Bumper"))
        {
            var match = BumperRegex().Match(line);
            if (match.Success)
            {
                if (int.TryParse(match.Groups[1].Value, out int bumperForward) &&
                    int.TryParse(match.Groups[2].Value, out int bumperReverse))
                {
                    Shuttle.BumperForward = bumperForward;
                    Shuttle.BumperReverse = bumperReverse;
                    Logger.LogInformation($"Parsed Bumper F/R: {bumperForward}/{bumperReverse}");
                }
            }
        }
        else if (line.StartsWith("Zero point MPR"))
        {
            var match = ZeroOffRegex().Match(line);
            if (match.Success)
            {
                if (int.TryParse(match.Groups[1].Value, out int zeroPointMpr) &&
                    int.TryParse(match.Groups[2].Value, out int channelOffset))
                {
                    Shuttle.ZeroPointMpr = zeroPointMpr;
                    Shuttle.ChannelOffset = channelOffset;
                    Logger.LogInformation($"Parsed Zero/Offset: {zeroPointMpr}/{channelOffset}");
                }
            }
        }
        else if (line.StartsWith("Wait time on unload"))
        {
            var match = WaitTimeRegex().Match(line);
            if (match.Success)
            {
                if (int.TryParse(match.Groups[1].Value, out int waitTime))
                {
                    Shuttle.WaitTimeUnload = waitTime;
                    Logger.LogInformation($"Parsed Wait Time: {waitTime}s");
                }
            }
        }
    }

    private void OnLogReceived(string ip, string log)
    {
        if (!IsEventForThisShuttle(ip))
            return;

        _logChannel.Writer.TryWrite(log);
    }

    private async Task ProcessLogsLoopAsync()
    {
        var rawLogsBatch = new List<string>();
        var formattedLogsBatch = new List<string>();
        var fileLogsBatch = new List<string>();

        try
        {
            while (!_componentCts.Token.IsCancellationRequested)
            {
                if (await _logChannel.Reader.WaitToReadAsync(_componentCts.Token))
                {
                    rawLogsBatch.Clear();
                    formattedLogsBatch.Clear();
                    fileLogsBatch.Clear();

                    while (_logChannel.Reader.TryRead(out var log))
                    {
                        rawLogsBatch.Add(log);
                        fileLogsBatch.Add($"[{DateTime.Now}] {log}");
                    }

                    if (fileLogsBatch.Count > 0)
                    {
                        try
                        {
                            await File.AppendAllLinesAsync(pathLogShuttle, fileLogsBatch);
                        }
                        catch (Exception ex)
                        {
                            Logger.LogWarning(ex, "Failed to write logs to file");
                        }
                    }

                    await InvokeAsync(() =>
                    {
                        foreach (var log in rawLogsBatch)
                        {
                            var timestamp = DateTime.Now;
                            ParseAndHandleResponse(log);

                            if (log.StartsWith("-----------------------------------------------"))
                            {
                                if (!_inStatusBlock)
                                {
                                    _inStatusBlock = true;
                                    _statusBlockLines.Clear();
                                }
                                else
                                {
                                    _inStatusBlock = false;
                                    Shuttle.FullStatusBlock = string.Join("\n", _statusBlockLines);
                                }
                            }
                            else if (_inStatusBlock)
                            {
                                _statusBlockLines.Add(log);
                            }

                            if (log.Contains("##HEARTBEAT##"))
                            {
                                formattedLogsBatch.Add($"[HEARTBEAT] {log}\n");
                            }
                            else
                            {
                                int telemetryIndex = log.IndexOf("##TELEMETRY##");
                                var cleanLog = telemetryIndex >= 0 ? log.Substring(0, telemetryIndex) : log;
                                formattedLogsBatch.Add($"[{timestamp:HH:mm:ss}] {cleanLog}\n");
                            }
                        }

                        if (formattedLogsBatch.Count > 0)
                        {
                            Shuttle.AddRangeTerminalMessages(formattedLogsBatch);
                            StateHasChanged();
                        }
                    });

                    if (formattedLogsBatch.Count > 0)
                    {
                        _ = ScrollTerminalToBottomAsync();
                    }

                    await Task.Delay(100, _componentCts.Token);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Ошибка в цикле обработки логов");
        }
    }

    private void LogToTerminalInternal(string message)
    {
        Shuttle.AddTerminalMessage(message);
    }

    private async Task SendCommandAsync(string command, string description = "")
    {
        if (!Shuttle.IsConnected)
        {
            LogToTerminal("[ERROR] Нет подключения\n");
            return;
        }

        _isCommandInProgress = true;
        try
        {
            StateHasChanged();

            var displayCommand = string.IsNullOrEmpty(description) ? command : $"{command} ({description})";
            LogToTerminal($"[CMD] Отправка: {displayCommand}\n");

            if (Shuttle.IPAddress is null)
            {
                throw new Exception("error with null IP");
            }
            else
            {
                var success = await HubClientService.SendCommandToShuttleAsync(Shuttle.IPAddress, command, 1000);
            }
        }
        catch (OperationCanceledException)
        {
            LogToTerminal($"[WARNING] Команда отменена: {command}\n");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Ошибка отправки команды {Command} к {IpAddress}", command, Shuttle.IPAddress);
            LogToTerminal($"[ERROR] Исключение при отправке: {ex.Message}\n");
        }
        finally
        {
            _isCommandInProgress = false;
            StateHasChanged();
        }
    }

    private async Task OpenLogsShuttle()
    {
        if (IsAndroid)
        {
            return;
        }

        string way = Path.Combine(AppContext.BaseDirectory, "logs");
        Process.Start("explorer.exe", way);
    }

    private async Task OnAraseModeChanged()
    {
        _isFullErased = !_isFullErased;
    }

    private async Task SendStopCommand() => await SendCommandAsync($"{Shuttle.ShuttleNumber}dStop_", "Остановка");

    private async Task SendLoadCommand() => await SendCommandAsync($"{Shuttle.ShuttleNumber}dLoad_", "Загрузка");

    private async Task SendLargeLoadCommand() => await SendCommandAsync($"{Shuttle.ShuttleNumber}dLLoad", "Длительная загрузка");

    private async Task SendUnloadCommand() => await SendCommandAsync($"{Shuttle.ShuttleNumber}dUnld_", "Выгрузка");

    private async Task SendLargeUnloadCommand() => await SendCommandAsync($"{Shuttle.ShuttleNumber}dLUnld", "Длительная выгрузка");

    private async Task SendDemoCommand() => await SendCommandAsync($"{Shuttle.ShuttleNumber}dDemo_", "Демо-режим");

    private async Task SendResetCommand() => await SendCommandAsync($"{Shuttle.ShuttleNumber}dReset", "Сброс ошибок");

    private async Task SendSaveSettingCommand() => await SendCommandAsync($"{Shuttle.ShuttleNumber}dSaveC", "Сохранение настроек");

    private async Task SendCalibrateCommand() => await SendCommandAsync($"{Shuttle.ShuttleNumber}dClbr_", "Калибровка шаттла");

    private async Task SendSealingForwardShuttleCommand() => await SendCommandAsync($"{Shuttle.ShuttleNumber}dComFo", "Уплотнение вперед");

    private async Task SendSealingBackShuttleCommand() => await SendCommandAsync($"{Shuttle.ShuttleNumber}dComBa", "Уплотнение назад");

    private bool isReversed = false;

    private async Task SendReverseShuttleCommand()
    {
        if (!isReversed)
        {
            isReversed = true;
            await SendCommandAsync($"{Shuttle.ShuttleNumber}dRevOn", "Реверс режим on");
        }
        else
        {
            await SendCommandAsync($"{Shuttle.ShuttleNumber}dReOff", "Реверс режим off");
            isReversed = false;
        }
    }

    private async Task SendUpShuttleCommand() => await SendCommandAsync($"{Shuttle.ShuttleNumber}dUp___", "Подъём");

    private async Task SendLeftShuttleCommand() => await SendCommandAsync($"{Shuttle.ShuttleNumber}dLeft_", "движение назад");

    private async Task SendRightShuttleCommand() => await SendCommandAsync($"{Shuttle.ShuttleNumber}dRight", "движение вперёд");

    private async Task SendDownShuttleCommand() => await SendCommandAsync($"{Shuttle.ShuttleNumber}dDown_", "Спуск");

    private async Task SendFRMFirmwareCommand() => await SendUpdateCommandAndOpenBrowser();

    private async Task SendUpdateStateShuttleCommand() => await SendUPDCommandAndOpenBrowser();

    private async Task SendRebootShuttleCommand() => await SendCommandAsync("RBT", "Перезагрузка шаттла shuttle");

    private async Task SendSetShuttleNumCommand()
    {
        await SendCommandAsync($"{Shuttle.ShuttleNumber}dNN{_shuttleNumberInput}", $"Установка номера шаттла: {_shuttleNumberInput}");
        Shuttle.ShuttleNumber = shuttleNums[_shuttleNumberInput - 1];
    }

    private async Task SendSetMoveReverseShuttleCommand() => await SendCommandAsync($"{Shuttle.ShuttleNumber}dMf{_moveDistanceBackwardInput}", $"Проезд расстояния назад: {_moveDistanceBackwardInput} мм");

    private async Task SendMoveForwardShuttleCommand() => await SendCommandAsync($"{Shuttle.ShuttleNumber}dMr{_moveDistanceForwardInput}", $"Проезд расстояния вперёд: {_moveDistanceForwardInput} мм");

    private async Task SendSetMaxSpeedShuttleCommand() => await SendCommandAsync($"{Shuttle.ShuttleNumber}dSp{_maxSpeedInput}", $"Установка максимальной скорости: {_maxSpeedInput}%");

    private async Task SendSetMinPowerShuttleCommand() => await SendCommandAsync($"{Shuttle.ShuttleNumber}dBc{_minPowerInput}", $"Установка уровня защиты батареи: {_minPowerInput}%");

    private async Task SendSetPalletBetweenDistanceShuttleCommand() => await SendCommandAsync($"{Shuttle.ShuttleNumber}dDm{_pallentDistance}", $"Установка межпаллетного расстояния: {_pallentDistance}%");

    private async Task SendSetDistanceOfEdgeShuttleCommand()
    {
        if (_distanceOfEdge is > 9 and < 100)
        {
            string resultofDistance = "0" + Convert.ToString(_distanceOfEdge);
            await SendCommandAsync($"{Shuttle.ShuttleNumber}dMс{resultofDistance}", $"Установка расстояния от края: {_distanceOfEdge} мм");
        }
        else
        {
            await SendCommandAsync($"{Shuttle.ShuttleNumber}dMс{_distanceOfEdge}", $"Установка расстояния от края: {_distanceOfEdge} мм");
        }
    }

    private async Task SendSetlenghtOfShuttleCommand()
    {
        if (_lenghtOfShuttle == 800)
        {
            string resultOfLenghtofShuttle = "0" + Convert.ToString(_lenghtOfShuttle / 10);
            await SendCommandAsync($"{Shuttle.ShuttleNumber}dSl{resultOfLenghtofShuttle}", $"Установка длины шаттла: {_lenghtOfShuttle} мм");
        }

        await SendCommandAsync($"{Shuttle.ShuttleNumber}dSl{_lenghtOfShuttle / 10}", $"Установка длины шаттла: {_lenghtOfShuttle} мм");
    }

    private async Task SetTime()
    {
        DateTime currentTime = DateTime.Now;
        string command = $"DT {currentTime:HH:mm:ss dd/MM/yyyy}";
        await SendCommandAsync(command);
    }

    private async Task SendUpdateCommandAndOpenBrowser()
    {
        await SendCommandAsync("FRM", "Прошивка");
        await OpenWebInterfaceAsync("FRM");
    }

    private async Task SendUPDCommandAndOpenBrowser()
    {
        await SendCommandAsync("UPD", "Обновление");
        await OpenWebInterfaceAsync("UPD");
    }

    private async Task OpenWebInterfaceAsync(string argOpenBrowser)
    {
        var url = $"http://{Shuttle.IPAddress}/";
        if (argOpenBrowser == "UPD")
        {
            url += "update";
        }

        try
        {
            var uri = new Uri(url);
            await WebBrowserService.OpenBrowserAsync(uri);
            LogToTerminal($"[INFO] Открыт веб-интерфейс: {url}\n");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Ошибка открытия веб-интерфейса {Url}", url);
            LogToTerminal($"[ERROR] Не удалось открыть браузер: {ex.Message}\n");
        }
    }

    private async Task SendManualCommand()
    {
        if (!string.IsNullOrWhiteSpace(_manualCommand))
        {
            await SendCommandAsync(_manualCommand.Trim(), "Ручная команда");
            _manualCommand = string.Empty;
        }
    }

    private async Task HandleKeyPress(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
        {
            await SendManualCommand();
        }
    }

    private async Task DisconnectThis()
    {
        if (Shuttle.IPAddress == null)
        {
            throw new Exception("ip is null");
        }
        else
        {
            HubClientService.DisconnectFromShuttle(Shuttle.IPAddress);
            LogToTerminal("[INFO] Запрошено отключение\n");
            await OnDisconnected.InvokeAsync(Shuttle.IPAddress);
        }
    }

    private void ClearTerminal()
    {
        Shuttle.ClearTerminalMessage();
        StateHasChanged();
    }

    private void LogToTerminal(string message)
    {
        Shuttle.AddTerminalMessage(message);

        StateHasChanged();
        _ = ScrollTerminalToBottomAsync();
    }

    private async Task ScrollTerminalToBottomAsync()
    {
        try
        {
            var elementId = $"terminalContainer_{_componentId}";
            await JSRuntime.InvokeVoidAsync("scrollToBottomIfNeeded", elementId);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Ошибка скролла терминала");
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await ScrollTerminalToBottomAsync();
        }
    }

    private string GetBatteryColorClass() => BatteryPercentageValue switch
    {
        >= 70 => "text-success",
        >= 30 => "text-warning",
        _ => "text-danger"
    };

    private string GetStatusColorClass() => CurrentStatus.ToLowerInvariant() switch
    {
        string s when s.Contains("ошибка") || s.Contains("error") => "text-danger",
        string s when s.Contains("предупреждение") || s.Contains("warning") => "text-warning",
        string s when s.Contains("работа") || s.Contains("work") => "text-success",
        _ => "text-secondary"
    };

    private async Task ChoiceFile()
    {
        var file = await FilePickerService.PickFileAsync();
        _selectedFile = file?.FilePath;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            Logger.LogInformation("Дисконнект компонента для IP: {IpAddress}", Shuttle?.IPAddress ?? "null");

            _componentCts?.Cancel();
            _componentCts?.Dispose();

            Logger.LogInformation("--- Component DISPOSE: Unsubscribing from events for {IP}", Shuttle?.IPAddress ?? "null");

            HubClientService.Connected -= OnHubConnected;
            HubClientService.Disconnected -= OnHubDisconnected;
            HubClientService.LogReceived -= OnLogReceived;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Ошибка при вызове Dispose");
        }
    }
}