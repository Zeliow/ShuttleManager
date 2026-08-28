using ShuttleManager.Shared.Services.ShuttleClient.Helpers;

namespace ShuttleManager.Tests;

public sealed class FakeTransport : ITransport
{
    public List<byte[]> Written { get; } = [];

    public bool IsConnected { get; set; } = true;

    public bool ThrowOnWrite { get; set; }

    public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct) => ValueTask.FromResult(0);

    public ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        if (ThrowOnWrite)
        {
            throw new IOException("simulated write failure");
        }

        Written.Add(data.ToArray());
        return ValueTask.CompletedTask;
    }

    public Task FlushAsync(CancellationToken ct) => Task.CompletedTask;

    public void Dispose()
    {
    }
}
