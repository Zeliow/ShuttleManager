## 2025-05-14 - [MemoryStream and Blazor Virtualization]
**Learning:** Calling `MemoryStream.ToArray()` in a loop creates massive GC pressure. Blazor's manual string-based terminal rendering is extremely expensive for long logs.
**Action:** Use `GetBuffer()` and `Buffer.BlockCopy()` for efficient stream processing. Use `<Virtualize>` for large lists in Blazor components.

## 2025-05-15 - [Blazor Log Throttling and Batching]
**Learning:** Invoking the UI thread and triggering re-renders for every individual message in high-frequency telemetry leads to UI saturation. Lock contention on shared models can be significantly reduced by batching collection updates.
**Action:** Offload parsing/cleaning to background threads. Use `AddRange` methods on shared models and update the UI in batches (e.g., via a throttled channel loop).
