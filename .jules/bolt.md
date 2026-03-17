## 2025-05-14 - [MemoryStream and Blazor Virtualization]
**Learning:** Calling `MemoryStream.ToArray()` in a loop creates massive GC pressure. Blazor's manual string-based terminal rendering is extremely expensive for long logs.
**Action:** Use `GetBuffer()` and `Buffer.BlockCopy()` for efficient stream processing. Use `<Virtualize>` for large lists in Blazor components.

## 2025-05-15 - [Batched Log Processing and Thread Safety]
**Learning:** High-frequency log processing in Blazor components can saturate the UI thread if handled per-message. Synchronous file I/O on the network path blocks message reception.
**Action:** Use `System.Threading.Channels` to decouple reception from processing. Drain the channel in batches to minimize `StateHasChanged` calls. Perform heavy file I/O (using `File.AppendAllLinesAsync`) sequentially on the background loop, and marshal ONLY UI state updates to the UI thread via `InvokeAsync`.
