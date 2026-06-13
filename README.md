# Shuttle Manager

## Обзор проекта

Shuttle Manager — это система WMS/WCS (Warehouse Management System / Warehouse Control System) для управления роботизированными шаттлами. Это кроссплатформенное standalone-приложение, разработанное для мониторинга, управления оборудованием и сбора телеметрии в реальном времени.

## Архитектура UI

Проект построен на базе **.NET MAUI Blazor Hybrid**. Это означает, что:

*   **Нативный контейнер:** Приложение использует нативный контейнер MAUI (`MainPage.xaml`), который обеспечивает запуск приложения как обычного оконного/мобильного приложения на целевых платформах.
*   **Рендеринг:** В качестве основного окна отображения используется `BlazorWebView`, который загружает и рендерит веб-контент.
*   **UI Слой:** Пользовательский интерфейс реализован через Razor-компоненты (находятся в `ShuttleManager.Shared`), используя чистые HTML и CSS. Приложение не использует тяжелые сторонние UI-фреймворки (типа MudBlazor или Radzen), что позволяет сохранить высокую производительность и полный контроль над стилями.

## Сетевой слой

Сетевое взаимодействие (отправка команд, сбор телеметрии) реализовано с использованием **TCP/UDP** протоколов. Основная логика работы с сетью вынесена в библиотеку классов `ShuttleManager.Shared`.

*   **Класс `ShuttleHubClientService`** (`ShuttleManager/ShuttleManager.Shared/Services/ShuttleClient/ShuttleHubClientService.cs`): Является центральным узлом для управления подключениями к шаттлам.
*   **Управление соединениями:** Реализует TCP-соединения (`TcpClient`) для прямого подключения к шаттлам и сетевого сканирования.
*   **Обработка протоколов:** Поддерживает несколько протоколов (Binary V2, Legacy) для обмена данными (папка `BinaryService/` и `LegacyService/`). Данные считываются в цикле `ReceiveLoopAsync` и обрабатываются соответствующим `IShuttleProtocolHandler`.

## Сборка и запуск

Приложение поддерживает целевые платформы **Windows** и **Android**.

### Требования
*   Установленный .NET 10.0 SDK.
*   Установленные MAUI workload (`maui-windows`, `maui-android`).
*   (Рекомендуется) Visual Studio 2022 с рабочими нагрузками для разработки .NET MAUI.

### Запуск через Visual Studio
1. Откройте файл решения `ShuttleManager.slnx`.
2. В панели инструментов выберите целевую платформу:
   *   Для Windows: `Windows Machine`.
   *   Для Android: выберите подключенное устройство или эмулятор (Android Emulator).
3. Нажмите `F5` для запуска с отладкой или `Ctrl+F5` для запуска без отладки.

### Запуск через CLI (Command Line Interface)

**Для Windows:**
```bash
dotnet build ShuttleManager/ShuttleManager/ShuttleManager.csproj -f net10.0-windows10.0.19041.0
dotnet run --project ShuttleManager/ShuttleManager/ShuttleManager.csproj -f net10.0-windows10.0.19041.0
```

**Для Android:**
*Для сборки под Android необходимо наличие настроенного Android SDK.*
```bash
dotnet build ShuttleManager/ShuttleManager/ShuttleManager.csproj -f net10.0-android
dotnet run --project ShuttleManager/ShuttleManager/ShuttleManager.csproj -f net10.0-android
```

*(Опционально) Сборка только общей библиотеки `ShuttleManager.Shared`:*
```bash
dotnet build ShuttleManager/ShuttleManager.Shared/ShuttleManager.Shared.csproj -p:TargetFrameworks=net10.0
```

## Структура проекта

Репозиторий состоит из двух основных проектов:

*   **`ShuttleManager/`** — Основной проект .NET MAUI.
    *   `App.xaml` / `App.xaml.cs` — Точка входа в MAUI приложение.
    *   `MainPage.xaml` — Главная страница MAUI, содержащая компонент `BlazorWebView` для хостинга Razor-интерфейса.
    *   `wwwroot/` — Статические файлы хоста (например, `index.html`).

*   **`ShuttleManager.Shared/`** — Разделяемая библиотека классов (Razor Class Library). Здесь содержится вся бизнес-логика и пользовательский интерфейс.
    *   `Pages/` — Razor-страницы приложения (например, страницы управления шаттлами, логами).
    *   `Layout/` — Компоненты макетов страниц (например, `MainLayout.razor`).
    *   `Services/` — Бизнес-сервисы, включая:
        *   `ShuttleClient/` — Логика сетевого взаимодействия по TCP/UDP, обработка протоколов (Binary, Legacy). Включает `ShuttleHubClientService.cs`.
    *   `Models/` — Модели данных.
    *   `Interfaces/` — Интерфейсы для сервисов (например, `IShuttleHubClientService`).
    *   `wwwroot/` — Статические веб-ресурсы (CSS, изображения и т.д.), специфичные для компонентов Blazor.
