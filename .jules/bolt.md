## 2025-05-14 - [MemoryStream and Blazor Virtualization]
**Learning:** Calling `MemoryStream.ToArray()` in a loop creates massive GC pressure. Blazor's manual string-based terminal rendering is extremely expensive for long logs.
**Action:** Use `GetBuffer()` and `Buffer.BlockCopy()` for efficient stream processing. Use `<Virtualize>` for large lists in Blazor components.

## 2025-05-15 - [Concurrency and Timestamp Integrity in Log Pipelines]
**Learning:** Offloading file I/O to background tasks using a shared mutable list (like a batching list) causes race conditions when the list is cleared or modified by the main thread. Additionally, calculating timestamps late in the pipeline (during I/O) leads to inaccuracies.
**Action:** Always pass a thread-safe copy (e.g., `.ToList()` or `.ToArray()`) to background tasks. Capture timestamps as soon as the event occurs (e.g., using tuples in the channel) to ensure data integrity across asynchronous boundaries.
