## 2025-05-14 - [MemoryStream and Blazor Virtualization]
**Learning:** Calling `MemoryStream.ToArray()` in a loop creates massive GC pressure. Blazor's manual string-based terminal rendering is extremely expensive for long logs.
**Action:** Use `GetBuffer()` and `Buffer.BlockCopy()` for efficient stream processing. Use `<Virtualize>` for large lists in Blazor components.

## 2026-04-13 - [Sequential I/O and Span-based Parsing]
**Learning:** Using `Task.Run` for background I/O inside a consumer loop can lead to race conditions and out-of-order writes. Direct `await` within the background loop ensures sequentiality and thread safety. `ReadOnlySpan<char>` with `AsSpan().Trim()` is significantly more efficient for multi-prefix checks than `string.Trim()`.
**Action:** Always prefer direct `await` in background processing loops for I/O and use Spans for high-frequency string inspections.
