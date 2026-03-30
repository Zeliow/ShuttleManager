## 2025-05-14 - [MemoryStream and Blazor Virtualization]
**Learning:** Calling `MemoryStream.ToArray()` in a loop creates massive GC pressure. Blazor's manual string-based terminal rendering is extremely expensive for long logs.
**Action:** Use `GetBuffer()` and `Buffer.BlockCopy()` for efficient stream processing. Use `<Virtualize>` for large lists in Blazor components.

## 2025-05-15 - [Batched Log Processing and Async I/O]
**Learning:** High-frequency logging with synchronous `File.AppendAllText` and individual `InvokeAsync` calls can starve the UI and network threads.
**Action:** Use `System.Threading.Channels` to decouple log reception from processing. Drain the channel to batch disk writes with `File.AppendAllLinesAsync` and consolidate UI state updates into a single `InvokeAsync/StateHasChanged` call.
