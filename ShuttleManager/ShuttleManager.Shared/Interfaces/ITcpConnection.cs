using System;
using System.Threading;
using System.Threading.Tasks;

namespace ShuttleManager.Shared.Interfaces;

public interface ITcpConnection : IDisposable
{
    ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct);
    ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct);
    Task FlushAsync(CancellationToken ct);
    bool IsConnected { get; }
}
