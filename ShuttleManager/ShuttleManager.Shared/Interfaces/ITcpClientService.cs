using System.Buffers;

namespace ShuttleManager.Shared.Interfaces;

public interface ITcpClientService
{
    Task<bool> ConnectAsync(string host, int port);

    Task SendAsync(ReadOnlyMemory<byte> data);

    Task<ReadOnlySequence<byte>> ReceiveAsync(int length);

    Task<string?> ReceiveStringAsync(CancellationToken cancellationToken = default);

    void Disconnect();
}