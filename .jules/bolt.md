## 2025-05-14 - [MemoryStream and Blazor Virtualization]
**Learning:** Calling `MemoryStream.ToArray()` in a loop creates massive GC pressure. Blazor's manual string-based terminal rendering is extremely expensive for long logs.
**Action:** Use `GetBuffer()` and `Buffer.BlockCopy()` for efficient stream processing. Use `<Virtualize>` for large lists in Blazor components.

## 2025-05-15 - [Blazor Virtualization and Type Inference]
**Learning:** Blazor's `<Virtualize>` component may fail to infer types when the data source is complex or provided by an expression. Additionally, it requires the `Items` parameter to implement `ICollection<TItem>` (not `IReadOnlyList<TItem>`) for some optimizations.
**Action:** Explicitly specify `TItem` in `<Virtualize>` tags. Ensure data source models return `ICollection<TItem>` for terminal-like components.
