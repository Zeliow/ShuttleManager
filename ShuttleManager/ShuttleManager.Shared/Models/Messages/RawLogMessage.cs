using ShuttleManager.Shared.Models.Protocol;

namespace ShuttleManager.Shared.Models.Messages;

public class RawLogMessage : ShuttleMessageBase
{
    public LogLevel Level { get; set; }
    public string Text { get; set; } = string.Empty;

    public override string ToFormattedTerminalString()
    {
        return $"[{Level}] {Text}";
    }
}