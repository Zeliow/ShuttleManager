## 2025-05-14 - [MemoryStream and Blazor Virtualization]
**Learning:** Calling `MemoryStream.ToArray()` in a loop creates massive GC pressure. Blazor's manual string-based terminal rendering is extremely expensive for long logs.
**Action:** Use `GetBuffer()` and `Buffer.BlockCopy()` for efficient stream processing. Use `<Virtualize>` for large lists in Blazor components.

## 2025-05-15 - [High-frequency Log Batching]
**Learning:** High-frequency logging with synchronous file I/O and immediate UI updates can saturate the UI thread and block networking loops.
**Action:** Use `System.Threading.Channels` to buffer logs with timestamps. Process in batches (e.g., 100 entries or 100ms) to offload file I/O to background tasks and throttle UI re-renders. Use centralized pruning logic in models to reduce lock contention.
