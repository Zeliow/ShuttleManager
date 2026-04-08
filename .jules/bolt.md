## 2025-05-14 - [MemoryStream and Blazor Virtualization]
**Learning:** Calling `MemoryStream.ToArray()` in a loop creates massive GC pressure. Blazor's manual string-based terminal rendering is extremely expensive for long logs.
**Action:** Use `GetBuffer()` and `Buffer.BlockCopy()` for efficient stream processing. Use `<Virtualize>` for large lists in Blazor components.

## 2025-05-15 - [Blazor Terminal Batching & Virtualization]
**Learning:** High-frequency log streams saturate the UI thread if updated line-by-line. Standard `@foreach` rendering of 900+ lines causes massive layout thrashing.
**Action:** Use `System.Threading.Channels` to buffer logs, drain and batch updates in a background loop (throttle via `Task.Delay`), and use `<Virtualize>` for rendering. Ensure the container has `overflow-y: auto` and a fixed height.
