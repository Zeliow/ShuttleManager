using System.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using ShuttleManager.Shared.Interfaces;

namespace ShuttleManager.Shared.Pages.Shuttle.Scaner;

public partial class ShuttlesPage : IAsyncDisposable
{
    [Inject]
    private IJSRuntime JSRuntime { get; set; } = default!;

    [Inject]
    private IShuttleHubClientService HubClientService { get; set; } = default!;

    [Parameter] public Models.Shuttle Shuttle { get; set; } = null!;

    private string _baseIp = "192.168.40";
    private int _port = 23;
    private bool _isScanning = false;
    private bool _scanCompleted = false;
    private List<IPAddress> _foundDevices = new();
    private List<Models.Shuttle> _connectedShuttles = new();
    private int _activeTabIndex = -1;
    private Timer? _cleanupTimer;
    private string[] shuttleNums = { "A1", "B2", "C3", "D4", "E5", "F6", "G7", "H8", "I9", "10", "11", "12", "13", "14", "15", "16", "17", "18", "19", "20", "21", "22", "23", "24", "25", "26", "27", "28", "29", "30", "31", "32" };

    // omponent for dark theme
    private ElementReference toggleElement;

    private bool IsDarkMode { get; set; } = false;

    protected override async Task OnInitializedAsync()
    {
        await StartScan();
        HubClientService.Connected += OnShuttleConnected;
        HubClientService.Disconnected += OnShuttleDisconnected;
        await RefreshConnectedShuttles();
        _cleanupTimer = new Timer(async _ => await CleanupDisconnectedShuttles(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    private async Task HandleConnectClickIfNotConnected(string ipAddress)
    {
        {
            await HubClientService.ConnectToShuttleAsync(ipAddress, _port);
            StateHasChanged();
        }
    }

    private async Task RefreshConnectedShuttles()
    {
        var connectedShuttles = HubClientService.GetConnectedShuttles();
        _connectedShuttles = connectedShuttles.ToList();
        if (_connectedShuttles.Any() && _activeTabIndex == -1)
        {
            _activeTabIndex = 0;
        }

        StateHasChanged();
    }

    private string SeeCorrentNumberShuttle(string numberShuttle)
    {
        if (char.IsDigit(numberShuttle[0]))
        {
            return numberShuttle;
        }
        else
        {
            return numberShuttle.Substring(1);
        }
    }

    private async Task CleanupDisconnectedShuttles()
    {
        await InvokeAsync(async () =>
        {
            var currentShuttles = HubClientService.GetConnectedShuttles();
            var currentIpAddresses = currentShuttles.Select(s => s.IPAddress).ToHashSet();

            var shuttlesToRemove = _connectedShuttles
                .Where(s => !currentIpAddresses.Contains(s.IPAddress))
                .ToList();

            if (shuttlesToRemove.Count != 0)
            {
                foreach (var shuttle in shuttlesToRemove)
                {
                    _connectedShuttles.Remove(shuttle);
                }

                if (_activeTabIndex >= _connectedShuttles.Count && _connectedShuttles.Any())
                {
                    _activeTabIndex = _connectedShuttles.Count - 1;
                }
                else if (!_connectedShuttles.Any())
                {
                    _activeTabIndex = -1;
                }

                StateHasChanged();
            }
        });
    }

    private async Task StartScan()
    {
        if (_isScanning)
            return;
        _isScanning = true;
        _scanCompleted = false;
        _foundDevices.Clear();
        StateHasChanged();

        try
        {
            _foundDevices = await HubClientService.ScanNetworkAsync(_baseIp, _port, 1000);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Ошибка сканирования: {ex.Message}");
            await JSRuntime.InvokeVoidAsync("alert", $"Ошибка сканирования: {ex.Message}");
        }
        finally
        {
            _isScanning = false;
            _scanCompleted = true;
            StateHasChanged();
        }
    }

    private void OnShuttleConnected(string ipAddress, int shuttleId)
    {
        InvokeAsync(() =>
        {
            string correctNum;
            try
            {
                correctNum = shuttleNums[int.Parse(ipAddress.Remove(0, ipAddress.Length - 3)) - 131];
            }
            catch (Exception)
            {
                correctNum = shuttleNums[0];
            }

            if (!_connectedShuttles.Any(s => s.IPAddress == ipAddress))
            {
                var newShuttle = new Models.Shuttle
                {
                    IPAddress = ipAddress,
                    IsConnected = true,
                    ConnectionTime = DateTime.Now,
                    LastActivity = DateTime.Now,
                    ShuttleNumber = correctNum,
                };
                _connectedShuttles.Add(newShuttle);

                _activeTabIndex = _connectedShuttles.Count - 1;
                StateHasChanged();
                Debug.WriteLine($"[SUCCESS] Подключен шаттл {ipAddress} (ID: {newShuttle.ShuttleNumber})\n");
            }
        });
    }

    private void OnShuttleDisconnected(string ipAddress)
    {
        InvokeAsync(() =>
        {
            int index = _connectedShuttles.FindIndex(s => s.IPAddress == ipAddress);
            if (index >= 0)
            {
                _connectedShuttles.RemoveAt(index);

                if (_activeTabIndex >= _connectedShuttles.Count && _connectedShuttles.Any())
                {
                    _activeTabIndex = _connectedShuttles.Count - 1;
                }
                else if (!_connectedShuttles.Any())
                {
                    _activeTabIndex = -1;
                }

                StateHasChanged();
            }
        });
    }

    private void SelectShuttle(int tabIndex)
    {
        if (tabIndex >= 0 && tabIndex < _connectedShuttles.Count)
            _activeTabIndex = tabIndex;
    }

    private bool IsAlreadyConnected(string ipAddress) => _connectedShuttles.Any(s => s.IPAddress == ipAddress);

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            var savedTheme = await JSRuntime.InvokeAsync<string>("eval", "localStorage.getItem('user-theme-preference');");

            if (savedTheme != null)
            {
                IsDarkMode = savedTheme == "dark";
            }
            else
            {
                var systemPrefersDark = await JSRuntime.InvokeAsync<bool>("eval", "window.matchMedia('(prefers-color-scheme: dark)').matches;");
                IsDarkMode = systemPrefersDark;
                await JSRuntime.InvokeVoidAsync("eval", $"localStorage.setItem('user-theme-preference', '{(IsDarkMode ? "dark" : "light")}');");
            }

            await ApplyThemeAsync(IsDarkMode);
            StateHasChanged();
        }
    }

    // Метод для установки темы
    private async Task ApplyThemeAsync(bool isDark)
    {
        var themeValue = isDark ? "dark" : string.Empty;
        await JSRuntime.InvokeVoidAsync("eval", $"document.documentElement.setAttribute('data-theme', '{themeValue}');");
        await JSRuntime.InvokeVoidAsync("eval", $"localStorage.setItem('user-theme-preference', '{(isDark ? "dark" : "light")}');");
        Console.WriteLine($"[DEBUG] ApplyThemeAsync called, isDark={isDark}, themeValue='{themeValue}'");
    }

    private async Task OnDarkModeChanged(ChangeEventArgs e)
    {
        var newValue = e.Value is bool b ? b : Convert.ToBoolean(e.Value);
        Console.WriteLine($"[DEBUG] OnDarkModeChanged fired, raw value: {e.Value}, parsed bool: {newValue}");

        if (IsDarkMode != newValue)
        {
            IsDarkMode = newValue;
            await ApplyThemeAsync(newValue);
        }
    }

    public async ValueTask DisposeAsync()
    {
        HubClientService.Connected -= OnShuttleConnected;
        HubClientService.Disconnected -= OnShuttleDisconnected;
        _cleanupTimer?.Dispose();
    }
}