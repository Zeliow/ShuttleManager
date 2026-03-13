## 2025-05-23 - Batching High-Frequency UI Updates
**Learning:** High-frequency logging (heartbeats, telemetry) in Blazor Hybrid can saturate the UI thread if `InvokeAsync` and `StateHasChanged` are called for every message.
**Action:** Use `System.Threading.Channels` for log ingestion. In the consumer loop, drain the channel completely on a background thread to batch messages, perform expensive parsing/formatting outside `InvokeAsync`, and only then update the UI state in a single batch.
