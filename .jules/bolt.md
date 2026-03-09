## 2025-05-14 - [MemoryStream and Blazor Virtualization]
**Learning:** Calling `MemoryStream.ToArray()` in a loop creates massive GC pressure. Blazor's manual string-based terminal rendering is extremely expensive for long logs.
**Action:** Use `GetBuffer()` and `Buffer.BlockCopy()` for efficient stream processing. Use `<Virtualize>` for large lists in Blazor components.

## 2025-05-15 - [Blazor UI Thread Contention and Network Scanning]
**Learning:** Processing high-frequency logs inside `InvokeAsync` blocks the UI thread, causing stuttering. Unrestricted `Task.Run` loops during network scanning can lead to socket exhaustion.
**Action:** Move parsing and string manipulation outside `InvokeAsync` and batch UI updates. Use `Parallel.ForEachAsync` with `MaxDegreeOfParallelism` to control network concurrency and resource usage.
