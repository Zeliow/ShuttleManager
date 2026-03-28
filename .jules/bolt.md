## 2025-05-14 - [MemoryStream and Blazor Virtualization]
**Learning:** Calling `MemoryStream.ToArray()` in a loop creates massive GC pressure. Blazor's manual string-based terminal rendering is extremely expensive for long logs.
**Action:** Use `GetBuffer()` and `Buffer.BlockCopy()` for efficient stream processing. Use `<Virtualize>` for large lists in Blazor components.

## 2026-03-28 - [Batched Async I/O for Logs]
**Learning:** Synchronous `File.AppendAllText` in high-frequency event handlers blocks the calling thread (e.g., network receiver), causing drops or latency. Blazor UI updates via `InvokeAsync` for every message saturate the UI thread.
**Action:** Use `System.Threading.Channels` to decouple production from consumption. Drain the channel into batches, use `File.AppendAllLinesAsync` for background persistence, and call `InvokeAsync` + `StateHasChanged` once per batch.
