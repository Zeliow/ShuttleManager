## 2025-05-14 - [MemoryStream and Blazor Virtualization]
**Learning:** Calling `MemoryStream.ToArray()` in a loop creates massive GC pressure. Blazor's manual string-based terminal rendering is extremely expensive for long logs.
**Action:** Use `GetBuffer()` and `Buffer.BlockCopy()` for efficient stream processing. Use `<Virtualize>` for large lists in Blazor components.

## 2025-05-15 - [Batched Log Processing and I/O]
**Learning:** Synchronous file I/O (`File.AppendAllText`) in high-frequency event handlers blocks the communication thread and causes UI stutter. Updating Blazor state for every individual message leads to excessive re-renders.
**Action:** Use `System.Threading.Channels` to decouple message receipt from processing. Batch UI updates and file I/O (using `File.AppendAllLinesAsync`) in a background loop with a throttle (e.g., 100ms) to maintain responsiveness.
