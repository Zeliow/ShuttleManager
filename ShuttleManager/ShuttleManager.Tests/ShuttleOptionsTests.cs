using ShuttleManager.Shared.Services.ShuttleClient;

namespace ShuttleManager.Tests;

public class ShuttleOptionsTests
{
    [Theory]
    [InlineData("192.168.40.130", "A1")]
    [InlineData("192.168.40.131", "A1")]
    [InlineData("192.168.40.132", "B2")]
    [InlineData("192.168.40.139", "I9")]
    [InlineData("192.168.40.140", "10")]
    [InlineData("192.168.40.161", "31")]
    [InlineData("192.168.40.162", "32")]
    public void ResolveShuttleId_KnownRange_ReturnsExpectedId(string ip, string expected)
    {
        var options = new ShuttleOptions();

        Assert.Equal(expected, options.ResolveShuttleId(ip));
    }

    [Theory]
    [InlineData("192.168.40.129", "129")]
    [InlineData("192.168.40.200", "200")]
    [InlineData("192.168.40.1", "1")]
    [InlineData("10.0.0.5", "5")]
    public void ResolveShuttleId_OutsideRange_FallsBackToOctet(string ip, string expected)
    {
        var options = new ShuttleOptions();

        Assert.Equal(expected, options.ResolveShuttleId(ip));
    }

    [Fact]
    public void ResolveShuttleId_CustomRule_IsApplied()
    {
        var options = new ShuttleOptions
        {
            IdRules =
            [
                new IpToIdRule
                {
                    BaseIp = "10.1.1",
                    StartOctet = 50,
                    Ids = ["X1", "X2"],
                },
            ],
        };

        Assert.Equal("X1", options.ResolveShuttleId("10.1.1.50"));
        Assert.Equal("X1", options.ResolveShuttleId("10.1.1.51"));
        Assert.Equal("X2", options.ResolveShuttleId("10.1.1.52"));
        Assert.Equal("60", options.ResolveShuttleId("10.1.1.60"));
    }
}
