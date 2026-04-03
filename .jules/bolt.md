## 2025-05-14 - [MemoryStream and Blazor Virtualization]
**Learning:** Calling `MemoryStream.ToArray()` in a loop creates massive GC pressure. Blazor's manual string-based terminal rendering is extremely expensive for long logs.
**Action:** Use `GetBuffer()` and `Buffer.BlockCopy()` for efficient stream processing. Use `<Virtualize>` for large lists in Blazor components.

## 2026-04-03 - [Synchronous I/O in Event Handlers]
**Learning:** Performing synchronous 'File.AppendAllText' inside 'OnLogReceived' (triggered by network events) blocks the receiving thread and can lead to UI freezes or dropped packets during high-frequency telemetry.
**Action:** Always decouple I/O and UI updates from network event handlers using 'Channel' and a background processing loop with batching.
