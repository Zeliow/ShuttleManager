using ShuttleManager.Shared.Intefraces;

namespace ShuttleManager.Platforms.Windows;

public class WindowService : IWindowService
{
    public void MinimizeToTray()
    {
        // Получаем главное окно
        var window = GetMainWindow();
        if (window == null) return;

        // Сворачиваем окно
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        ShowWindow(hwnd, 2); // SW_MINIMIZE = 2

        // Скрываем с панели задач (опционально)
        // ShowWindow(hwnd, 0); // SW_HIDE = 0
    }

    public void RestoreFromTray()
    {
        var window = GetMainWindow();
        if (window == null) return;

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        ShowWindow(hwnd, 9); // SW_RESTORE = 9
        ShowWindow(hwnd, 5); // SW_SHOW = 5
        SetForegroundWindow(hwnd);
    }

    public void HideToSystemTray()
    {
        var window = GetMainWindow();
        if (window == null) return;

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);

        // Полностью скрываем окно
        ShowWindow(hwnd, 0); // SW_HIDE

        // Можно добавить иконку в системный трей
        // См. реализацию ниже
    }

    private Microsoft.UI.Xaml.Window GetMainWindow()
    {
        // Способ 1: Через MauiContext
        var window = Application.Current?.Windows
            .FirstOrDefault()?.Handler?.PlatformView as Microsoft.UI.Xaml.Window;

        // Способ 2: Через AppWindow (альтернативный)
        // var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(
        //     Microsoft.UI.WindowId.FromInt32(hwnd.ToInt32()));

        return window;
    }

    // WinAPI импорты
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
