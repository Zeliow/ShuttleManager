using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ShuttleManager.Services;
using ShuttleManager.Shared.Interfaces;
using ShuttleManager.Shared.Services.OtaUpdate;
using ShuttleManager.Shared.Services.ShuttleClient;

namespace ShuttleManager.Platforms.Windows;

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

        using Stream appSettings = FileSystem.OpenAppPackageFileAsync("appsettings.json").GetAwaiter().GetResult();
        builder.Configuration.AddJsonStream(appSettings);
        builder.Services.Configure<ShuttleOptions>(builder.Configuration.GetSection(ShuttleOptions.SectionName));

        builder.Services.AddSingleton<IShuttleHubClientService, ShuttleHubClientService>();
        builder.Services.AddSingleton<IFilePickerService, FilePickerService>();
        builder.Services.AddSingleton<IOtaUpdateService, OtaUpdateService>();
        builder.Services.AddSingleton<IWebBrowserService, WebBrowserService>();
        builder.Services.AddMauiBlazorWebView();
#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}