## 2025-05-14 - [MemoryStream and Blazor Virtualization]
**Learning:** Calling `MemoryStream.ToArray()` in a loop creates massive GC pressure. Blazor's manual string-based terminal rendering is extremely expensive for long logs.
**Action:** Use `GetBuffer()` and `Buffer.BlockCopy()` for efficient stream processing. Use `<Virtualize>` for large lists in Blazor components.

## 2025-05-15 - [Concurrency and Resource Management in Network Scanning]
**Learning:** Unbounded task creation in network loops (e.g., scanning a subnet) can lead to thread pool pressure and socket exhaustion. Failure to dispose of `CancellationTokenSource` creates minor memory and timer leaks.
**Action:** Use `Parallel.ForEachAsync` with a controlled `MaxDegreeOfParallelism` (e.g., 32) for network-bound concurrent operations. Always use `using` blocks for `CancellationTokenSource`.
