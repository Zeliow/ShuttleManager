using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;

namespace ShuttleManager.Shared.Models;

public struct ShuttleTelemetry
{
    public string Ip { get; set; }
    public string Vatt { get; set; }
    public string Volt { get; set; }
}

public class Shuttle
{
    public string ShuttleNumber { get; set; } = "";
    public string? IPAddress { get; set; } = string.Empty;
    public int BatteryPercentage { get; set; } = 0;
    public bool IsAutoLogging { get; set; }
    public string? CurrentCommand { get; set; } // Возможно, не используется в новом парсинге
    public bool? Inverse { get; set; } // Старое поле, может переопределить
    public TcpClient? Client { get; }
    public bool LoggingStatus { get; set; }
    public bool IsFormOpened { get; set; }
    public int MaxSpeed { get; set; } // Текущая max speed из статуса, отличается от настройки
    public int InterPalleteDistance { get; set; } // Используем как MPR
    public int ShuttleLength { get; set; }
    public int BatteryLimit { get; set; } // Новое поле
    public double BatteryVoltage { get; set; } = 0.0;
    public bool IsConnected { get; set; } = false;
    public string? LastReceivedData { get; set; }
    public string CurrentStatus { get; set; } = "Неизвестно"; // Статус из лога
    public int ErrorCode { get; set; } = 0;
    public int WarningCode { get; set; } = 0;
    public DateTime ConnectionTime { get; set; } = DateTime.Now;
    public DateTime LastActivity { get; set; } = DateTime.Now;

    // --- Новые поля из лога ---
    public double Temperature { get; set; } = 0.0;
    public int Angle { get; set; } = 0;
    public int Length { get; set; } = 0; // Проверьте конфликт с ShuttleLength
    public int Position { get; set; } = 0;
    public string FifoLifoMode { get; set; } = "Unknown";
    public int ForwardDistance { get; set; } = 0;
    public int ReverseDistance { get; set; } = 0;
    public int ForwardPalletDistance { get; set; } = 0;
    public int ReversePalletDistance { get; set; } = 0;
    public int PalletDetectorFront1 { get; set; } = 0;
    public int PalletDetectorFront2 { get; set; } = 0;
    public int PalletDetectorRear1 { get; set; } = 0;
    public int PalletDetectorRear2 { get; set; } = 0;
    public bool IsInChannel { get; set; } = false;
    public bool IsLifterUp { get; set; } = false;
    public bool IsLifterDown { get; set; } = false;
    public int BumperForward { get; set; } = 0;
    public int BumperReverse { get; set; } = 0;
    public int ZeroPointMpr { get; set; } = 0;
    public int ChannelOffset { get; set; } = 0;
    public int StatusCode { get; set; } = 0; // Код статуса в скобках
    public int WaitTimeUnload { get; set; } = 0;
    public string ConnectionStatus { get; set; } = "Offline"; // "online...", "CB XX"
    public DateTime LastConnectionCheck { get; set; } = DateTime.MinValue;

    // Поле для хранения полного блока статуса (опционально)
    public string FullStatusBlock { get; set; } = "";

    // Новое: история сообщений для терминала
    private List<string> _terminalMessages = new();

    private readonly object _lock = new object();

    public void AddTerminalMessage(string message)
    {
        lock (_lock)
        {
            _terminalMessages.Add(message);
        }
    }

    public void ClearTerminalMessage()
    {
        lock (_lock)
        {
            _terminalMessages.Clear();
        }
    }

    public void RemoveTerminalMessage()
    {
        lock (_lock)
        {
            _terminalMessages.RemoveRange(0, _terminalMessages.Count - 500);
        }
    }


    public List<string> GetTerminalMessages()
    {
        lock (_lock)
        {
            return new List<string>(_terminalMessages);
        }
    }

    // Метод для обновления статуса соединения
    //public void SetConnectionStatus(bool isConnected)
    //{
    //    IsConnected = isConnected;
    //}
}