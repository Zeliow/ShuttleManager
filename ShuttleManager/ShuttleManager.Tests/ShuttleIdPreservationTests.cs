using Microsoft.Extensions.Options;
using ShuttleManager.Shared.Models.Protocol;
using ShuttleManager.Shared.Services.Enums;
using ShuttleManager.Shared.Services.ShuttleClient;
using ShuttleManager.Shared.Services.ShuttleClient.BinaryService;
using ShuttleManager.Shared.Services.ShuttleClient.Helpers;
using ShuttleManager.Shared.Services.ShuttleClient.LegacyService;

namespace ShuttleManager.Tests;

public class ShuttleIdPreservationTests
{
    private static ShuttleConnection CreateConnection(FakeTransport transport)
    {
        return new ShuttleConnection
        {
            IpAddress = "192.168.40.132",
            ShuttleId = "B2",
            Transport = transport,
            Protocol = ShuttleProtocolType.Binary,
        };
    }

    private static byte[] BuildAckFrame(byte refSeq)
    {
        var packet = new AckPacket { RefSeq = refSeq, Result = AckResult.ACK_OK };
        return BinaryFrameCodec.BuildFrame(MsgID.MSG_ACK, ProtocolConstants.TARGET_ID_NONE, refSeq, in packet);
    }

    [Fact]
    public async Task Legacy_SendShuttleNumber_SetsLetterIdAndFlag()
    {
        var callbacks = new ProtocolCallbacks { OnMessage = (_, _) => { } };
        var handler = new LegacyProtocolHandler(callbacks);
        var transport = new FakeTransport();
        ShuttleConnection connection = CreateConnection(transport);
        connection.Protocol = ShuttleProtocolType.Legacy;

        bool result = await handler.SendConfigAsync(connection, ShuttleConfigCommand.ShuttleNumber, 3, 1000);

        Assert.True(result);
        Assert.Equal("C3", connection.ShuttleId);
        Assert.True(connection.ShuttleIdFromConfig);
        Assert.Equal("192.168.40.133", connection.PendingIpAddress);
    }

    [Fact]
    public async Task Binary_SendShuttleNumberWithAck_SetsLetterIdAndFlag()
    {
        var callbacks = new ProtocolCallbacks { OnMessage = (_, _) => { } };
        var handler = new BinaryProtocolHandler(callbacks);
        var transport = new FakeTransport();
        ShuttleConnection connection = CreateConnection(transport);

        Task<bool> sendTask = handler.SendConfigAsync(connection, ShuttleConfigCommand.ShuttleNumber, 3, 5000);

        connection.ReceiveBuffer.Write(BuildAckFrame(transport.Written[0][4]));
        handler.ProcessBuffer(connection);

        Assert.True(await sendTask);
        Assert.Equal("C3", connection.ShuttleId);
        Assert.True(connection.ShuttleIdFromConfig);
        Assert.Equal("192.168.40.133", connection.PendingIpAddress);
    }

    [Fact]
    public async Task Binary_SendShuttleNumberWithoutAck_DoesNotSetFlag()
    {
        var callbacks = new ProtocolCallbacks { OnMessage = (_, _) => { } };
        var handler = new BinaryProtocolHandler(callbacks);
        var transport = new FakeTransport();
        ShuttleConnection connection = CreateConnection(transport);

        bool result = await handler.SendConfigAsync(connection, ShuttleConfigCommand.ShuttleNumber, 3, 50);

        Assert.False(result);
        Assert.Equal("B2", connection.ShuttleId);
        Assert.False(connection.ShuttleIdFromConfig);
    }

    [Fact]
    public async Task Binary_SendOtherConfig_DoesNotTouchShuttleId()
    {
        var callbacks = new ProtocolCallbacks { OnMessage = (_, _) => { } };
        var handler = new BinaryProtocolHandler(callbacks);
        var transport = new FakeTransport();
        ShuttleConnection connection = CreateConnection(transport);

        Task<bool> sendTask = handler.SendConfigAsync(connection, ShuttleConfigCommand.MaxSpeed, 120, 5000);

        connection.ReceiveBuffer.Write(BuildAckFrame(transport.Written[0][4]));
        handler.ProcessBuffer(connection);

        Assert.True(await sendTask);
        Assert.Equal("B2", connection.ShuttleId);
        Assert.False(connection.ShuttleIdFromConfig);
    }

    [Fact]
    public async Task CustomOptionsRule_LetterIdUsesConfiguredIds()
    {
        var options = new ShuttleOptions
        {
            IdRules =
            [
                new IpToIdRule
                {
                    BaseIp = "192.168.40",
                    StartOctet = 130,
                    Ids = ["AA1", "BB2", "CC3"],
                },
            ],
        };

        var callbacks = new ProtocolCallbacks { OnMessage = (_, _) => { } };
        var handler = new LegacyProtocolHandler(callbacks, null, Options.Create(options));
        var transport = new FakeTransport();
        ShuttleConnection connection = CreateConnection(transport);
        connection.Protocol = ShuttleProtocolType.Legacy;

        await handler.SendConfigAsync(connection, ShuttleConfigCommand.ShuttleNumber, 2, 1000);

        Assert.Equal("BB2", connection.ShuttleId);
        Assert.True(connection.ShuttleIdFromConfig);
    }
}
