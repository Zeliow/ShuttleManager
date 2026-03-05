## 2025-05-14 - [MemoryStream and Blazor Virtualization]
**Learning:** Calling `MemoryStream.ToArray()` in a loop creates massive GC pressure. Blazor's manual string-based terminal rendering is extremely expensive for long logs.
**Action:** Use `GetBuffer()` and `Buffer.BlockCopy()` for efficient stream processing. Use `<Virtualize>` for large lists in Blazor components.

## 2025-05-15 - [High-Frequency Log Parsing and UI Throttling]
**Learning:** Creating Regex instances inside parsing loops is extremely expensive. Throttling Blazor's `StateHasChanged` during high-frequency updates prevents UI thread saturation.
**Action:** Cache compiled `Regex` instances as `static readonly`. Use `StartsWith` guards for fast bypass. Implement a background processing loop using `System.Threading.Channels` for throttled UI updates (e.g., 100ms interval).
