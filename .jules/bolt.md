## 2025-05-14 - [MemoryStream and Blazor Virtualization]
**Learning:** Calling `MemoryStream.ToArray()` in a loop creates massive GC pressure. Blazor's manual string-based terminal rendering is extremely expensive for long logs.
**Action:** Use `GetBuffer()` and `Buffer.BlockCopy()` for efficient stream processing. Use `<Virtualize>` for large lists in Blazor components.

## 2025-05-15 - [Batched Log Processing and Non-blocking I/O]
**Learning:** High-frequency logging with synchronous file I/O and per-message UI updates saturates the UI thread and blocks networking tasks.
**Action:** Use `System.Threading.Channels` to buffer logs, drain them in batches, use `File.AppendAllLinesAsync` for non-blocking I/O, and wrap batch updates in a single `InvokeAsync` call to minimize UI churn.
