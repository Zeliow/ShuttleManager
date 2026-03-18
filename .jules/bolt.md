## 2025-05-14 - [MemoryStream and Blazor Virtualization]
**Learning:** Calling `MemoryStream.ToArray()` in a loop creates massive GC pressure. Blazor's manual string-based terminal rendering is extremely expensive for long logs.
**Action:** Use `GetBuffer()` and `Buffer.BlockCopy()` for efficient stream processing. Use `<Virtualize>` for large lists in Blazor components.

## 2025-05-15 - [High-Frequency Log Processing in Blazor]
**Learning:** Synchronous file I/O and frequent `StateHasChanged()` calls in response to high-frequency telemetry can saturate the UI thread and cause significant lag. Fire-and-forget async I/O in a loop can lead to `IOException` due to concurrent access.
**Action:** Use `System.Threading.Channels` for non-blocking message passing. Batch messages in a background loop, await `File.AppendAllLinesAsync` within a try-catch block for safe sequential I/O, and throttle UI updates (e.g., 100ms) to maintain responsiveness. Use `ReadOnlySpan<char>` for fast prefix filtering to reduce allocations.
