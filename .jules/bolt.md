## 2025-05-14 - [MemoryStream and Blazor Virtualization]
**Learning:** Calling `MemoryStream.ToArray()` in a loop creates massive GC pressure. Blazor's manual string-based terminal rendering is extremely expensive for long logs.
**Action:** Use `GetBuffer()` and `Buffer.BlockCopy()` for efficient stream processing. Use `<Virtualize>` for large lists in Blazor components.

## 2025-05-15 - [Batched Log Processing and Async I/O]
**Learning:** High-frequency log updates in Blazor can saturate the UI thread if StateHasChanged is called per-message. Synchronous File.AppendAllText in event handlers blocks the processing thread.
**Action:** Batch log updates from a Channel, perform parsing and UI state changes once per batch, and offload file logging to File.AppendAllLinesAsync outside the UI thread.
