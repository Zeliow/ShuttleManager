using Microsoft.Extensions.Logging;
using ShuttleManager.Services;
using ShuttleManager.Shared.Interfaces;
using ShuttleManager.Shared.Services;
using ShuttleManager.Shared.Services.TcpOfClient;

namespace ShuttleManager.Platforms.Android;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        builder.Services.AddSingleton<ITcpClientService, TcpClientService>();
        builder.Services.AddSingleton<IShuttleHubClientService, ShuttleHubClientService>();
        builder.Services.AddSingleton<IFilePickerService, FilePickerService>();
        builder.Services.AddSingleton<IWebBrowserService, WebBrowserService>();
        builder.Services.AddSingleton<IOtaUpdateService, OtaUpdateService>();
        builder.Services.AddMauiBlazorWebView();
#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}