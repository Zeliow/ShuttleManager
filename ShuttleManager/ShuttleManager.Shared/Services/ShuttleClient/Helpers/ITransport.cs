namespace ShuttleManager.Shared.Services.ShuttleClient.Helpers;

/// <summary>Абстракция транспорта соединения с шаттлом. Позволяет подменять реальный TCP на фейковый в тестах.</summary>
public interface ITransport : IDisposable
{
    bool IsConnected { get; }

    ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct);

    ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct);

    Task FlushAsync(CancellationToken ct);
}
