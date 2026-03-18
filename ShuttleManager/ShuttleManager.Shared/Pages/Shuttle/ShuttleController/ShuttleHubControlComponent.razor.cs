using System.Diagnostics;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using ShuttleManager.Shared.Interfaces;
using ShuttleManager.Shared.Models.Messages;
using ShuttleManager.Shared.Models.Protocol;
using ShuttleManager.Shared.Services.Enums;
using ShuttleManager.Shared.Services.OtaUpdate;

namespace ShuttleManager.Shared.Pages.Shuttle.ShuttleController;

public partial class ShuttleHubControlComponent : ComponentBase, IAsyncDisposable
{
    [Inject]
    protected IJSRuntime JSRuntime { get; set; } = default!;

    [Inject]
    protected ILogger<ShuttleHubControlComponent> Logger { get; set; } = default!;

    [Inject]
    protected IShuttleHubClientService HubClientService { get; set; } = default!;

    [Inject]
    protected IFilePickerService FilePickerService { get; set; } = default!;

    [Inject]
    protected IOtaUpdateService OtaService { get; set; } = default!;

    [Inject]
    protected IWebBrowserService WebBrowserService { get; set; } = default!;

    [Parameter]
    public Models.Shuttle Shuttle { get; set; } = null!;

    [Parameter]
    public EventCallback<string> OnDisconnected { get; set; }

    private readonly SemaphoreSlim _logLock = new(1, 1);

    private int _shuttleNumberInput = 1;
    private int _moveDistanceBackwardInput = 0;
    private int _moveDistanceForwardInput = 0;
    private int _distanceOfEdge = 0;
    private int _lenghtOfShuttle = 800;
    private int _maxSpeedInput = 0;
    private int _minPowerInput = 0;
    private int _pallentDistance = 0;
    private bool IsAndroid => OperatingSystem.IsAndroid();

    private string _terminalOutputHtml = string.Empty;
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
    private string TerminalOutputHtml => _terminalOutputHtml;
    private string ManualCommand { get => _manualCommand; set => _manualCommand = value; }

    private string? _selectedFile;
    private int _otaPercent;
    private bool _isOtaRunning;
    private string _otaStatus = string.Empty;
    private CancellationTokenSource? _otaCts;

    private int _displayPercent;
    private string _otaPhaseText = string.Empty;
    private bool _isCancelling;

    private bool _showSensorsView = false;
    private bool _statsView = false;

    private bool isReversed = false;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            Directory.CreateDirectory(
            Path.Combine(AppContext.BaseDirectory, "logs"));
            HubClientService.Connected += OnHubConnected;
            HubClientService.Disconnected += OnHubDisconnected;
            HubClientService.LogReceived += OnLogReceived;
            Logger.LogInformation("Компонент инициализирован для {IpAddress}", Shuttle.IPAddress);
            StateHasChanged();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Ошибка инициализации компонента для {IpAddress}", Shuttle.IPAddress);
            LogToTerminal($"[ERROR] Ошибка инициализации: {ex.Message}\n");
        }
    }

    private void UpdatePhaseText(int percent)
    {
        if (percent < 70)
            _otaPhaseText = "Передача прошивки...";
        else if (percent < 95)
            _otaPhaseText = "Запись во flash...";
        else if (percent < 100)
            _otaPhaseText = "Финализация...";
        else
            _otaPhaseText = "Готово";
    }

    private async Task AnimateProgressAsync(int target)
    {
        if (target < _displayPercent)
            return;

        while (_displayPercent < target)
        {
            _displayPercent++;
            await InvokeAsync(StateHasChanged);
            await Task.Delay(15);
        }
    }

    private async Task OnAraseModeChanged()
    {
        _isFullErased = !_isFullErased;
    }

    private void SensorView()
    {
        _showSensorsView = !_showSensorsView;
        _statsView = false;
    }

    private void StatsView()
    {
        _statsView = !_statsView;
        _showSensorsView = false;
    }

    private string SwitchLabelView()
    {
        if (_showSensorsView)
        {
            return "Сенсоры";
        }
        else if (_statsView)
        {
            return "Статистика";
        }
        else
        {
            return "Терминал";
        }
    }

    private async Task StartOta(OtaTarget target)
    {
        await ChoiceFile();

        if (_selectedFile == null)
            return;

        _isOtaRunning = true;
        _otaPercent = 0;
        _otaStatus = "Инициализация...";
        _otaPhaseText = "Подготовка...";
        _otaCts = new CancellationTokenSource();

        StateHasChanged();

        var progress = new Progress<OtaProgress>(p =>
        {
            _otaPercent = p.Percent;
            UpdatePhaseText(_otaPercent);
            _ = AnimateProgressAsync(_otaPercent);
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

            if (result.IsSuccess)
            {
                _otaStatus = "OTA завершено успешно";
                _otaPhaseText = "Завершено";
                await AnimateProgressAsync(100);
            }
            else
            {
                _otaStatus = $"Ошибка: {result.Error}";
            }
        }
        catch (OperationCanceledException)
        {
            _otaStatus = "OTA отменено";
        }
        catch (Exception ex)
        {
            _otaStatus = $"Ошибка: {ex.Message}";
            Logger.LogError(ex, "Ошибка OTA для {IpAddress}", Shuttle.IPAddress);
        }
        finally
        {
            _isOtaRunning = false;
            _isCancelling = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private void CancelOta()
    {
        if (_otaCts == null)
            return;

        _isCancelling = true;
        _otaStatus = "Отмена...";
        _otaCts.Cancel();
    }

    private bool IsEventForThisShuttle(string ipAddress)
    {
        return ipAddress == Shuttle.IPAddress;
    }

    private async void OnHubConnected(string ip, string id)
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

    private static string GetMessageFolder(ShuttleMessageBase msg) =>
        msg switch
        {
            TelemetryMessage => "telemetry",
            SensorMessage => "sensors",
            StatsMessage => "stats",
            RawLogMessage => "raw",
            ConfigMessage => "config",
            AckMessage => "ack",
            _ => "unknown"
        };

    private string GetLogPath(string ip, ShuttleMessageBase msg)
    {
        var baseDir = Path.Combine(AppContext.BaseDirectory, "logs", ip);

        var typeFolder = GetMessageFolder(msg);

        var dir = Path.Combine(baseDir, typeFolder);

        Directory.CreateDirectory(dir);

        var fileName = $"{DateTime.Now:yyyy_MM_dd}.txt";

        return Path.Combine(dir, fileName);
    }

    private string GetAllLogPath(string ip)
    {
        var baseDir = Path.Combine(AppContext.BaseDirectory, "logs", ip, "all");

        Directory.CreateDirectory(baseDir);

        var fileName = $"{DateTime.Now:yyyy_MM_dd}.txt";

        return Path.Combine(baseDir, fileName);
    }

    private void OnLogReceived(string ip, ShuttleMessageBase msg)
    {
        _ = ProcessLogAsync(ip, msg);
    }

    private async Task ProcessLogAsync(string ip, ShuttleMessageBase msg)
    {
        if (!IsEventForThisShuttle(ip))
            return;

        var formattedString = msg.ToFormattedTerminalString();
        var timestamp = $"[{DateTime.Now:HH:mm:ss}] ";

        var typePath = GetLogPath(ip, msg);
        var allPath = GetAllLogPath(ip);

        await _logLock.WaitAsync();
        try
        {
            var line = $"{timestamp}{formattedString}\n";
            await File.AppendAllTextAsync(typePath, line);
            await File.AppendAllTextAsync(allPath, line);
        }
        finally
        {
            _logLock.Release();
        }

        await InvokeAsync(() =>
        {
            if (msg is TelemetryMessage tm)
                UpdateTelemetry(tm.Data);
            else if (msg is SensorMessage sm)
                UpdateSensors(sm.Data);
            else if (msg is StatsMessage stm)
                UpdateStats(stm.Data);
            else if (msg is ConfigMessage cm)
                UpdateConfig(cm.Data);

            if (msg is RawLogMessage or AckMessage or ConfigMessage)
            {
                LogToTerminal($"{timestamp}{formattedString}\n");
            }

            StateHasChanged();
        });
    }

    private void UpdateTelemetry(TelemetryPacket data)
    {
        Shuttle.LastActivity = DateTime.Now;
        Shuttle.ErrorCode = (ShuttleFault)data.ErrorCode;
        Shuttle.WarningCode = data.WaringCode;
        Shuttle.CurrentStatus = ((ShuttleState)data.ShuttleStatus).ToString();
        Shuttle.Position = data.CurrentPosition;
        Shuttle.CurrentSpeed = data.Speed;
        Shuttle.BatteryPercentage = data.BatteryCharge;
        Shuttle.BatteryVoltage = data.BatteryVoltage_mV / 1000.0;
        Shuttle.StateFlags = data.StateFlags;
        Shuttle.ShuttleNumber = Convert.ToString(data.ShuttleNumber);
        Shuttle.IsLifterUp = (data.StateFlags & 1) != 0;
        Shuttle.IsInChannel = (data.StateFlags & 16) != 0;
        Shuttle.Inverse = (data.StateFlags & 8) != 0;
    }

    private void UpdateSensors(SensorPacket data)
    {
        Shuttle.LastActivity = DateTime.Now;
        Shuttle.ForwardDistance = data.DistanceF;
        Shuttle.ReverseDistance = data.DistanceR;
        Shuttle.ForwardPalletDistance = data.DistancePltF;
        Shuttle.ReversePalletDistance = data.DistancePltR;
        Shuttle.Angle = data.Angle * (360.0 / 4096.0);
        Shuttle.Temperature = data.Temperature_dC / 10.0;

        // Map Hardware Flags
        // Bit 0: detectPalleteF1, Bit 1: F2, Bit 2: R1, Bit 3: R2
        Shuttle.PalletDetectorFront1 = (data.HardwareFlags & 1) != 0 ? 1 : 0;
        Shuttle.PalletDetectorFront2 = (data.HardwareFlags & 2) != 0 ? 1 : 0;
        Shuttle.PalletDetectorRear1 = (data.HardwareFlags & 4) != 0 ? 1 : 0;
        Shuttle.PalletDetectorRear2 = (data.HardwareFlags & 8) != 0 ? 1 : 0;

        // Bit 4: BUMPER_F, Bit 5: BUMPER_R
        Shuttle.BumperForward = (data.HardwareFlags & 16) != 0 ? 1 : 0;
        Shuttle.BumperReverse = (data.HardwareFlags & 32) != 0 ? 1 : 0;

        // Bit 6: DL_UP, Bit 7: DL_DOWN
        Shuttle.IsLifterUp = (data.HardwareFlags & 64) != 0;
        Shuttle.IsLifterDown = (data.HardwareFlags & 128) != 0;
    }

    private void UpdateStats(StatsPacket data)
    {
        Shuttle.LastActivity = DateTime.Now;
        Shuttle.TotalDist = data.TotalDist / 1000;
        Shuttle.LoadCounter = data.LoadCounter;
        Shuttle.UnloadCounter = data.UnloadCounter;
        Shuttle.CompactCounter = data.CompactCounter;
        Shuttle.LiftUpCounter = data.LiftUpCounter;
        Shuttle.LiftDownCounter = data.LiftDownCounter;
        Shuttle.LifetimePalletsDetected = data.LifetimePalletsDetected;
        Shuttle.TotalUptimeMinutes = data.TotalUptimeMinutes / 60;
        Shuttle.MotorStallCount = data.MotorStallCount;
        Shuttle.LifterOverloadCount = data.LifterOverloadCount;
        Shuttle.CrashCount = data.CrashCount;
        Shuttle.WatchdogResets = data.WatchdogResets;
        Shuttle.LowBatteryEvents = data.LowBatteryEvents;
    }

    private void UpdateConfig(ConfigPacket data)
    {
        switch ((ConfigParamID)data.ParamID)
        {
            case ConfigParamID.CFG_MAX_SPEED:
                Shuttle.MaxSpeed = data.Value;
                break;

            case ConfigParamID.CFG_INTER_PALLET:
                Shuttle.InterPalleteDistance = data.Value;
                break;

            case ConfigParamID.CFG_SHUTTLE_LEN:
                Shuttle.ShuttleLength = data.Value;
                break;

            case ConfigParamID.CFG_MIN_BATT:
                Shuttle.BatteryLimit = data.Value;
                break;

            case ConfigParamID.CFG_WAIT_TIME:
                Shuttle.WaitTimeUnload = data.Value;
                break;

            case ConfigParamID.CFG_CHNL_OFFSET:
                Shuttle.ChannelOffset = data.Value;
                break;

            case ConfigParamID.CFG_MPR_OFFSET:
                Shuttle.ZeroPointMpr = data.Value;
                break;

            case ConfigParamID.CFG_FIFO_LIFO:
                Shuttle.FifoLifoMode = data.Value == 1 ? "LIFO" : "FIFO"; // Assuming mapping? Or just string?
                break;
        }
    }

    // NEW METHOW TWICE PROTOCOL
    private async Task SendCommand(
    ShuttleCommand cmd,
    string description,
    int arg1 = 0,
    int arg2 = 0)
    {
        if (!Shuttle.IsConnected)
        {
            LogToTerminal("[ERROR] Нет подключения\n");
            return;
        }

        if (Shuttle.IPAddress == null)
            return;

        _isCommandInProgress = true;

        try
        {
            LogToTerminal($"[CMD] {description}\n");

            await HubClientService.SendCommandAsync(
                Shuttle.IPAddress,
                cmd,
                arg1,
                arg2);
        }
        finally
        {
            _isCommandInProgress = false;
            StateHasChanged();
        }
    }

    // NEW TWICE Метод для конфигурационных команд
    private async Task SendConfigCommand(
        ShuttleConfigCommand param,
        int value,
        string description)
    {
        if (!Shuttle.IsConnected)
        {
            LogToTerminal("[ERROR] Нет подключения\n");
            return;
        }

        if (Shuttle.IPAddress == null)
            return;

        _isCommandInProgress = true;

        try
        {
            LogToTerminal($"[CONFIG] {description}\n");
            await HubClientService.SendConfigAsync(
                Shuttle.IPAddress, param, value);
        }
        finally
        {
            _isCommandInProgress = false;
            StateHasChanged();
        }
    }

    //TEST TWICE METHOD
    private async Task SendStopCommand()
        => await SendCommand(ShuttleCommand.Stop, "Остановка");

    private async Task SendLoadCommand()
        => await SendCommand(ShuttleCommand.Load, "Загрузка");

    private async Task SendLargeLoadCommand()
        => await SendCommand(ShuttleCommand.LongLoad, "Длительная загрузка");

    private async Task SendUnloadCommand()
        => await SendCommand(ShuttleCommand.Unload, "Выгрузка");

    private async Task SendLargeUnloadCommand()
        => await SendCommand(ShuttleCommand.LongUnload, "Длительная выгрузка");

    private async Task SendDemoCommand()
        => await SendCommand(ShuttleCommand.Demo, "Демо-режим");

    private async Task SendResetCommand()
        => await SendCommand(ShuttleCommand.Reset, "Сброс ошибок");

    private async Task SendSaveSettingCommand()
        => await SendCommand(ShuttleCommand.SaveConfig, "Сохранение настроек");

    private async Task SendCalibrateCommand()
        => await SendCommand(ShuttleCommand.Calibrate, "Калибровка шаттла");

    private async Task SendGoHomeCommand()
        => await SendCommand(ShuttleCommand.Home, "Возвращение домой");

    private async Task SendSealingForwardShuttleCommand()
        => await SendCommand(ShuttleCommand.SealForward, "Уплотнение вперед");

    private async Task SendSealingBackShuttleCommand()
        => await SendCommand(ShuttleCommand.SealBackward, "Уплотнение назад");

    private async Task SendUpShuttleCommand()
        => await SendCommand(ShuttleCommand.LiftUp, "Подъём");

    private async Task SendLeftShuttleCommand()
        => await SendCommand(ShuttleCommand.Left, "Назад");

    private async Task SendRightShuttleCommand()
        => await SendCommand(ShuttleCommand.Right, "Вперёд");

    private async Task SendDownShuttleCommand()
        => await SendCommand(ShuttleCommand.LiftDown, "Спуск");

    private async Task SendRebootShuttleCommand()
        => await SendCommand(ShuttleCommand.SystemReset, "Reboot shuttle");

    // config button with param
    private async Task SendSetMoveReverseShuttleCommand()
        => await SendCommand(
            ShuttleCommand.MoveDistanceBackward, $"Проезд расстояния назад: {_moveDistanceBackwardInput} мм", _moveDistanceBackwardInput);

    private async Task SendMoveForwardShuttleCommand()
        => await SendCommand(
            ShuttleCommand.MoveDistanceForward, $"Проезд расстояния вперёд: {_moveDistanceForwardInput} мм", _moveDistanceForwardInput);

    // === Конфигурационные команды ===
    private async Task SendUseReverseModeShuttleCommand()
    {
        if (!isReversed)
        {
            await SendConfigCommand(ShuttleConfigCommand.ReverseMode, 1, "Реверс режим ON");
            isReversed = !isReversed;
        }
        else
        {
            await SendConfigCommand(ShuttleConfigCommand.ReverseMode, 0, "Реверс режим OFF");
            isReversed = !isReversed;
        }
    }

    private async Task SendSetMaxSpeedShuttleCommand()
        => await SendConfigCommand(
            ShuttleConfigCommand.MaxSpeed, _maxSpeedInput, $"Установка максимальной скорости: {_maxSpeedInput}%");

    private async Task SendSetMinPowerShuttleCommand()
        => await SendConfigCommand(
            ShuttleConfigCommand.MinBattery, _minPowerInput, $"Установка уровня защиты батареи: {_minPowerInput}%");

    private async Task SendSetPalletBetweenDistanceShuttleCommand()
        => await SendConfigCommand(
            ShuttleConfigCommand.InterPalletDistance, _pallentDistance, $"Установка межпаллетного расстояния: {_pallentDistance}%");

    private async Task SendSetDistanceOfEdgeShuttleCommand()
        => await SendConfigCommand(
            ShuttleConfigCommand.DistOfEdge, _distanceOfEdge, $"Установка расстояния от края: {_distanceOfEdge} мм");

    private async Task SendSetlenghtOfShuttleCommand()
        => await SendConfigCommand(
            ShuttleConfigCommand.ShuttleLength, _lenghtOfShuttle, $"Установка длины шаттла: {_lenghtOfShuttle} мм");

    private async Task SendSetShuttleNumCommand()
        => await SendConfigCommand(
            ShuttleConfigCommand.ShuttleNumber, _shuttleNumberInput, $"Установка номера шаттла: {_shuttleNumberInput}");

    private async Task SetTime()
    {
        if (!Shuttle.IsConnected || Shuttle.IPAddress is null)
        {
            LogToTerminal("[ERROR] Нет подключения\n");
            return;
        }

        _isCommandInProgress = true;

        try
        {
            StateHasChanged();

            LogToTerminal("[CMD] Установка времени\n");

            await HubClientService.SendDateTimeAsync(
                Shuttle.IPAddress,
                DateTime.UtcNow);

            LogToTerminal("[WARNING] Установка времени\n");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Ошибка установки времени");
            LogToTerminal($"[ERROR] {ex.Message}\n");
        }
        finally
        {
            _isCommandInProgress = false;
            StateHasChanged();
        }
    }

    private async Task SendManualCommand()
    {
        if (string.IsNullOrWhiteSpace(_manualCommand))
            return;

        var cmd = _manualCommand.Trim();

        if (Enum.TryParse(cmd, true, out CmdType result))
        {
            // ✅ NEW PROTOCOL: Typed command
            LogToTerminal($"[NEW PROTOCOL] Отправка бинарной команды: {result}\n");
            await HubClientService.SendManualCommandAsync(Shuttle.IPAddress!, cmd);
        }
        else
        {
            // ⚠️ LEGACY PROTOCOL: Raw string fallback
            LogToTerminal($"[ERROR] Неизвестная команда '{_manualCommand}'. Введите имя команды (например, CMD_STOP)\n");
            LogToTerminal($"[LEGACY PROTOCOL] Команда не найдена в энумах. Отправка raw-строки: '{cmd}'\n");

            // Важно: передаём информацию, что это legacy, если это возможно через аргументы
            await HubClientService.SendManualCommandAsync(Shuttle.IPAddress!, cmd);
        }

        _manualCommand = string.Empty;
    }

    private async Task HandleKeyPress(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
        {
            await SendManualCommand();
        }
    }

    private async Task SendFRMFirmwareCommand() => await SendUpdateCommandAndOpenBrowser();

    private async Task SendUpdateStateShuttleCommand() => await SendUPDCommandAndOpenBrowser();

    private async Task SendUpdateCommandAndOpenBrowser()
    {
        await SendCommand(ShuttleCommand.FRM, "Обновление экрана");
        await OpenWebInterfaceAsync("FRM");
    }

    private async Task SendUPDCommandAndOpenBrowser()
    {
        await SendCommand(ShuttleCommand.UPD, "Обновление контроллера");
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
        _terminalOutputHtml = string.Empty;
        StateHasChanged();
    }

    private void LogToTerminal(string message)
    {
        var lines = message.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);

        Shuttle.AddTerminalMessage(message);

        if (Shuttle.GetTerminalMessages().Count > 1000)
        {
            Shuttle.RemoveTerminalMessage();
        }

        _terminalOutputHtml = string.Join(
            string.Empty,
            Shuttle.GetTerminalMessages().Select(line =>
                $"<div class=\"terminal-line\">{System.Net.WebUtility.HtmlEncode(line)}</div>"));
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
        Logger.LogInformation("Дисконнект компонента для IP: {IpAddress}", Shuttle.IPAddress);

        _componentCts.Cancel();
        _componentCts.Dispose();

        if (_otaCts == null)
        {
        }
        else
        {
            _otaCts.Cancel();
            _otaCts.Dispose();
        }

        HubClientService.Connected -= OnHubConnected;
        HubClientService.Disconnected -= OnHubDisconnected;
        HubClientService.LogReceived -= OnLogReceived;
    }

    private async Task OpenLogsShuttle()
    {
        if (IsAndroid)
            return;

        string path = Path.Combine(
            AppContext.BaseDirectory,
            "logs",
            Shuttle.IPAddress!);

        if (!Directory.Exists(path))
            Directory.CreateDirectory(path);

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer",
            Arguments = path,
            UseShellExecute = true,
        });
    }
}