using ShuttleManager.Shared.Services;

namespace ShuttleManager.Shared.Interfaces
{
    public interface IOtaUpdateService
    {
        Task<OtaResult> RunAsync(string ip, string filePath, OtaTarget target, IProgress<OtaProgress>? progress, CancellationToken token, bool fullErase = false);
    }
}