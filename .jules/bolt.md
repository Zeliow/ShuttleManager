## 2025-05-14 - [MemoryStream and Blazor Virtualization]
**Learning:** Calling `MemoryStream.ToArray()` in a loop creates massive GC pressure. Blazor's manual string-based terminal rendering is extremely expensive for long logs.
**Action:** Use `GetBuffer()` and `Buffer.BlockCopy()` for efficient stream processing. Use `<Virtualize>` for large lists in Blazor components.

## 2025-05-15 - [Batching and Offloading Log Processing]
**Learning:** High-frequency log processing in Blazor components can saturate the UI thread if parsing, formatting, and file I/O are done synchronously or within `InvokeAsync`.
**Action:** Drain log channels into batches. Perform non-UI tasks (regex parsing, file I/O) in background loops outside `InvokeAsync`. Batch UI updates (`StateHasChanged`) to reduce rendering churn.
