## 2025-05-14 - [MemoryStream and Blazor Virtualization]
**Learning:** Calling `MemoryStream.ToArray()` in a loop creates massive GC pressure. Blazor's manual string-based terminal rendering is extremely expensive for long logs.
**Action:** Use `GetBuffer()` and `Buffer.BlockCopy()` for efficient stream processing. Use `<Virtualize>` for large lists in Blazor components.

## 2025-05-15 - [Batch Log Processing and Thread-Safe Virtualization]
**Learning:** High-frequency log updates in Blazor can saturate the UI thread if handled per-message. `<Virtualize>` requires the data source to be stable during rendering to avoid "Collection was modified" exceptions.
**Action:** Use `System.Threading.Channels` to batch logs. Drain the channel in a background loop, update the model in batches, and return a thread-safe snapshot (e.g., `ToArray()`) from the model for the `Virtualize` component.
