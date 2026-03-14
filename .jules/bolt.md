## 2025-05-14 - [MemoryStream and Blazor Virtualization]
**Learning:** Calling `MemoryStream.ToArray()` in a loop creates massive GC pressure. Blazor's manual string-based terminal rendering is extremely expensive for long logs.
**Action:** Use `GetBuffer()` and `Buffer.BlockCopy()` for efficient stream processing. Use `<Virtualize>` for large lists in Blazor components.

## 2025-05-15 - [Batched Log Processing and Off-Thread I/O]
**Learning:** Updating the UI for every single log message in high-frequency streams causes massive UI churn and blocks the main thread. Synchronous file I/O in event handlers further degrades performance.
**Action:** Use batched processing (draining Channels) and update UI state once per batch. Offload heavy I/O to background tasks and ensure UI state updates happen within the appropriate thread context (e.g., InvokeAsync).
