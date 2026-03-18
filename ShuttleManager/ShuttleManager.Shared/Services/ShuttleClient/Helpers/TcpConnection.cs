using System.Net.Sockets;

namespace ShuttleManager.Shared.Services.ShuttleClient.Helpers;

public class TcpConnection : IDisposable
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;

    public TcpConnection(TcpClient client)
    {
        _client = client;
        _stream = client.GetStream();
    }

    public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct)
        => _stream.ReadAsync(buffer, ct);

    public ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
        => _stream.WriteAsync(data, ct);

    public Task FlushAsync(CancellationToken ct)
       => _stream.FlushAsync(ct);

    public bool IsConnected => _client.Connected;

    public void Dispose()
    {
        try
        {
            _stream.Dispose();
        }
        catch
        {
        }

        try
        {
            _client.Close();
        }
        catch
        {
        }
    }
}