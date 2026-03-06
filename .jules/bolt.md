## 2025-05-14 - [MemoryStream and Blazor Virtualization]
**Learning:** Calling `MemoryStream.ToArray()` in a loop creates massive GC pressure. Blazor's manual string-based terminal rendering is extremely expensive for long logs.
**Action:** Use `GetBuffer()` and `Buffer.BlockCopy()` for efficient stream processing. Use `<Virtualize>` for large lists in Blazor components.

## 2025-05-20 - [Telemetry Parsing and UI Throttling]
**Learning:** High-frequency telemetry parsing and logging can saturate the UI thread and create massive GC pressure due to repeated regex allocations and frequent `StateHasChanged` calls.
**Action:** Use `[GeneratedRegex]` in partial classes for telemetry parsing, combine with fast `StartsWith` prefix checks, and implement `Channel<string>`-based throttling for UI log updates (max 10Hz).
