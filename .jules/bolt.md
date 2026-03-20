## 2025-05-14 - [MemoryStream and Blazor Virtualization]
**Learning:** Calling `MemoryStream.ToArray()` in a loop creates massive GC pressure. Blazor's manual string-based terminal rendering is extremely expensive for long logs.
**Action:** Use `GetBuffer()` and `Buffer.BlockCopy()` for efficient stream processing. Use `<Virtualize>` for large lists in Blazor components.

## 2025-05-15 - [Batched UI Updates and Background I/O]
**Learning:** High-frequency log processing in Blazor components can saturate the UI thread if StateHasChanged is called per message. Synchronous file I/O on the receive thread blocks network processing.
**Action:** Use `System.Threading.Channels` to batch logs. Offload file I/O to background tasks using `File.AppendAllLinesAsync`. Perform UI updates once per batch within `InvokeAsync` and throttle the loop with `Task.Delay`.
