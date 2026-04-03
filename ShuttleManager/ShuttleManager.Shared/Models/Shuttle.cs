using System.Net.Sockets;

namespace ShuttleManager.Shared.Models;

public class Shuttle
{
    private readonly object _lock = new object();

    public string ShuttleNumber { get; set; } = string.Empty;
    public string? IPAddress { get; set; } = string.Empty;
    public int BatteryPercentage { get; set; } = 0;
    public bool? Inverse { get; set; }
    public int MaxSpeed { get; set; }
    public int InterPalleteDistance { get; set; }
    public int ShuttleLength { get; set; }
    public int BatteryLimit { get; set; }
    public double BatteryVoltage { get; set; } = 0.0;
    public bool IsConnected { get; set; } = false;
    public string CurrentStatus { get; set; } = "Неизвестно";
    public int ErrorCode { get; set; } = 0;
    public int WarningCode { get; set; } = 0;
    public DateTime ConnectionTime { get; set; } = DateTime.Now;
    public DateTime LastActivity { get; set; } = DateTime.Now;

    public double Temperature { get; set; } = 0.0;
    public int Angle { get; set; } = 0;
    public int Length { get; set; } = 0;
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
    public int StatusCode { get; set; } = 0;
    public int WaitTimeUnload { get; set; } = 0;

    public string FullStatusBlock { get; set; } = string.Empty;
    private List<string> _terminalMessages = [];

    public void AddTerminalMessage(string message)
    {
        lock (_lock)
        {
            _terminalMessages.Add(message);
            this.TruncateMessages();
        }
    }

    public void AddRangeTerminalMessages(IEnumerable<string> messages)
    {
        lock (_lock)
        {
            _terminalMessages.AddRange(messages);
            this.TruncateMessages();
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
            this.TruncateMessages();
        }
    }

    private void TruncateMessages()
    {
        if (_terminalMessages.Count > 900)
        {
            _terminalMessages.RemoveRange(0, _terminalMessages.Count - 500);
        }
    }

    public int TerminalMessageCount
    {
        get
        {
            lock (_lock)
            {
                return _terminalMessages.Count;
            }
        }
    }

    public ICollection<string> GetTerminalMessages()
    {
        lock (_lock)
        {
            return _terminalMessages;
        }
    }
}