## 2025-05-15 - Async File I/O in High-Frequency Loops
**Learning:** Using fire-and-forget async file I/O (like `_ = File.AppendAllLinesAsync(...)`) in a high-frequency loop causes concurrent access attempts to the same file, leading to `IOException`.
**Action:** Always `await` file I/O operations within processing loops or use a serialized synchronization mechanism (like a dedicated logging actor or semaphore) to ensure sequential access. Additionally, always wrap file I/O in try-catch blocks to prevent local I/O failures from crashing the main processing loop.

## 2025-05-15 - Blazor UI Virtualization Constraints
**Learning:** The Blazor `<Virtualize>` component requires its container to have a fixed or calculated height and `overflow-y: auto` to function correctly. Without these, it may render all items at once or nothing at all.
**Action:** Ensure the parent container of a `<Virtualize>` component has explicit height and overflow styles.
