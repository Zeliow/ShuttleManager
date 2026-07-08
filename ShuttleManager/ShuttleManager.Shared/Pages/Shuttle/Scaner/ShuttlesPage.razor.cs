using System.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using ShuttleManager.Shared.Interfaces;

namespace ShuttleManager.Shared.Pages.Shuttle.Scaner;

public partial class ShuttlesPage : ComponentBase, IAsyncDisposable
{
    [Inject]
    protected IJSRuntime JSRuntime { get; set; } = default!;

    [Inject]
    protected IShuttleHubClientService HubClientService { get; set; } = default!;

    protected string baseIp = "192.168.40";
    protected int port = 23;
    protected int startRange = 130;
    protected int endRange = 250;
    protected bool isScanning = false;
    protected bool scanCompleted = false;
    protected List<IPAddress> foundDevices = new();
    protected List<Models.Shuttle> _connectedShuttles = new();
    protected int activeTabIndex = -1;
    protected Timer? _cleanupTimer;

    protected string[] shuttleNums = ["A1", "B2", "C3", "D4", "E5", "F6", "G7", "H8", "I9", "10", "11", "12", "13", "14", "15", "16", "17", "18", "19", "20", "21", "22", "23", "24", "25", "26", "27", "28", "29", "30", "31", "32"];

    // Dark theme
    protected ElementReference toggleElement;

    protected bool IsDarkMode { get; set; } = false;

    protected override async Task OnInitializedAsync()
    {
        await StartScan();
        HubClientService.Connected += OnShuttleConnected;
        HubClientService.Disconnected += OnShuttleDisconnected;
        await RefreshConnectedShuttles();

        _cleanupTimer = new Timer(
            async _ => await CleanupDisconnectedShuttles(),
            null,
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(5));

        // Start mock shuttle
        _ = HubClientService.ConnectToShuttleAsync("127.0.0.1", port);
    }

    protected async Task CleanupDisconnectedShuttles()
    {
        await InvokeAsync(() =>
        {
            var currentShuttles = HubClientService.GetConnectedShuttles();
            var currentIpAddresses = currentShuttles.Select(s => s.IPAddress).ToHashSet();

            var shuttlesToRemove = _connectedShuttles
                .Where(s => !currentIpAddresses.Contains(s.IPAddress))
                .ToList();

            if (shuttlesToRemove.Any())
            {
                foreach (var shuttle in shuttlesToRemove)
                    _connectedShuttles.Remove(shuttle);

                if (activeTabIndex >= _connectedShuttles.Count && _connectedShuttles.Any())
                    activeTabIndex = _connectedShuttles.Count - 1;
                else if (!_connectedShuttles.Any())
                    activeTabIndex = -1;

                StateHasChanged();
            }
        });
    }

    protected async Task HandleConnectClickIfNotConnected(string ipAddress)
    {
        await HubClientService.ConnectToShuttleAsync(ipAddress, port);
        StateHasChanged();
    }

    protected async Task RefreshConnectedShuttles()
    {
        var connectedShuttles = HubClientService.GetConnectedShuttles();
        _connectedShuttles = connectedShuttles.ToList();

        if (_connectedShuttles.Any() && activeTabIndex == -1)
            activeTabIndex = 0;

        StateHasChanged();
    }

    protected async Task StartScan()
    {
        if (isScanning)
            return;

        isScanning = true;
        scanCompleted = false;
        foundDevices.Clear();

        StateHasChanged();

        try
        {
            foundDevices = await HubClientService.ScanNetworkAsync(baseIp, startRange, endRange, port, 1000);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Ошибка сканирования: {ex.Message}");
            await JSRuntime.InvokeVoidAsync("alert", $"Ошибка сканирования: {ex.Message}");
        }
        finally
        {
            isScanning = false;
            scanCompleted = true;
            StateHasChanged();
        }
    }

    protected void OnShuttleConnected(string ipAddress, string shuttleId)
    {
        InvokeAsync(() =>
        {
            string correctNum;
            try
            {
                correctNum = shuttleNums[int.Parse(ipAddress[^3..]) - 131];
            }
            catch
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
                activeTabIndex = _connectedShuttles.Count - 1;

                StateHasChanged();
            }
        });
    }

    protected void OnShuttleDisconnected(string ipAddress)
    {
        InvokeAsync(() =>
        {
            int index = _connectedShuttles.FindIndex(s => s.IPAddress == ipAddress);

            if (index >= 0)
            {
                _connectedShuttles.RemoveAt(index);

                if (activeTabIndex >= _connectedShuttles.Count && _connectedShuttles.Any())
                    activeTabIndex = _connectedShuttles.Count - 1;
                else if (!_connectedShuttles.Any())
                    activeTabIndex = -1;

                StateHasChanged();
            }
        });
    }

    protected void SelectShuttle(int tabIndex)
    {
        if (tabIndex >= 0 && tabIndex < _connectedShuttles.Count)
            activeTabIndex = tabIndex;
    }

    protected bool IsAlreadyConnected(string ipAddress)
        => _connectedShuttles.Any(s => s.IPAddress == ipAddress);

    public async ValueTask DisposeAsync()
    {
        HubClientService.Connected -= OnShuttleConnected;
        HubClientService.Disconnected -= OnShuttleDisconnected;
        _cleanupTimer?.Dispose();
    }

    protected async Task OnDarkModeChanged(ChangeEventArgs e)
    {
        var newValue = e.Value is bool b ? b : Convert.ToBoolean(e.Value);

        if (IsDarkMode != newValue)
        {
            IsDarkMode = newValue;
            await ApplyThemeAsync(newValue);
        }
    }

    protected async Task ApplyThemeAsync(bool isDark)
    {
        var themeValue = isDark ? "dark" : string.Empty;

        await JSRuntime.InvokeVoidAsync(
            "eval",
            $"document.documentElement.setAttribute('data-theme', '{themeValue}');");

        await JSRuntime.InvokeVoidAsync(
            "eval",
            $"localStorage.setItem('user-theme-preference', '{(isDark ? "dark" : "light")}');");
    }

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
}