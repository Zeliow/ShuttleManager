## 2025-05-14 - [MemoryStream and Blazor Virtualization]
**Learning:** Calling `MemoryStream.ToArray()` in a loop creates massive GC pressure. Blazor's manual string-based terminal rendering is extremely expensive for long logs.
**Action:** Use `GetBuffer()` and `Buffer.BlockCopy()` for efficient stream processing. Use `<Virtualize>` for large lists in Blazor components.
## 2025-05-15 - [Blazor UI Thread Offloading and Batching]
**Learning:** Processing high-frequency logs inside `InvokeAsync` blocks saturates the Blazor UI thread, causing sluggish responsiveness. Direct individual updates to model collections under lock create excessive contention.
**Action:** Offload log parsing and batching to a background loop. Use `InvokeAsync` only for triggering `StateHasChanged` once per batch. Implement batch-specific methods in models to perform atomic updates and maintenance (like pruning) within a single lock.
