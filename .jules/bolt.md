## 2025-05-14 - [MemoryStream and Blazor Virtualization]
**Learning:** Calling `MemoryStream.ToArray()` in a loop creates massive GC pressure. Blazor's manual string-based terminal rendering is extremely expensive for long logs.
**Action:** Use `GetBuffer()` and `Buffer.BlockCopy()` for efficient stream processing. Use `<Virtualize>` for large lists in Blazor components.

## 2025-05-15 - [Blazor UI Thread Offloading and Batching]
**Learning:** Performing expensive log parsing (regex) and multiple model updates inside `InvokeAsync` blocks can saturate the Blazor UI thread, leading to stuttering during high-frequency log bursts.
**Action:** Offload parsing, regex matching, and string formatting to the background processing loop. Batch model updates using centralized pruning and only use `InvokeAsync` for final state updates and `StateHasChanged()`.
