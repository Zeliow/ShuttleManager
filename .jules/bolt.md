## 2025-05-14 - [MemoryStream and Blazor Virtualization]
**Learning:** Calling `MemoryStream.ToArray()` in a loop creates massive GC pressure. Blazor's manual string-based terminal rendering is extremely expensive for long logs.
**Action:** Use `GetBuffer()` and `Buffer.BlockCopy()` for efficient stream processing. Use `<Virtualize>` for large lists in Blazor components.

## 2025-05-15 - [Batched UI and I/O Processing]
**Learning:** High-frequency UI updates and synchronous file I/O in event handlers (like `OnLogReceived`) can saturate the UI thread and cause disk latency issues.
**Action:** Use `System.Threading.Channels` to buffer logs. Process the channel in a background loop by draining messages into batches, performing `File.AppendAllLinesAsync` outside the UI thread, and calling `StateHasChanged` once per batch to maintain responsiveness.
