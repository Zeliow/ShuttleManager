namespace ShuttleManager.Shared.Services.OtaUpdate;

public interface IOtaUpdateService
{
    public Task<OtaResult> RunAsync(string ip, string filePath, OtaTarget target, IProgress<OtaProgress>? progress, CancellationToken token, bool fullErase = false);
}