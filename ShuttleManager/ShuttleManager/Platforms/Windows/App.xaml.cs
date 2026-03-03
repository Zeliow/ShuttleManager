using Microsoft.UI;
using Microsoft.UI.Xaml;
using ShuttleManager.Platforms.Windows;
using WinRT.Interop;

namespace ShuttleManager.WinUI
{
    public partial class App : MauiWinUIApplication
    {
        public App()
        {
            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                var exception = args.ExceptionObject as Exception;
                Console.WriteLine($"[CRITICAL] Unhandled Exception: {exception?.Message}");
            };

            this.InitializeComponent();
        }

        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            base.OnLaunched(args);

            var window = Application.Windows.FirstOrDefault() as Microsoft.Maui.Controls.Window;
            if (window != null)
            {
                window.Title = "Менеджер шаттлов S.V.3.5";

                var nativeWindow = window.Handler.PlatformView as Microsoft.UI.Xaml.Window;
                if (nativeWindow != null)
                {
                    var hWnd = WindowNative.GetWindowHandle(nativeWindow);
                    var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
                    var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);

                    if (appWindow != null)
                    {
                        appWindow.Title = "Менеджер шаттлов";
                        var presenter = appWindow.Presenter as Microsoft.UI.Windowing.OverlappedPresenter;
                        if (presenter != null)
                        {
                            presenter.IsResizable = true;
                            presenter.IsMaximizable = true;
                            presenter.IsMinimizable = true;
                        }
                    }
                }
            }
        }
    }
}