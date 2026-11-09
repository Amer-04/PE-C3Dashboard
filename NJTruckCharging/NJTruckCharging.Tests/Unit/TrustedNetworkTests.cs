using System.Net;
using NJTruckCharging.Security;

namespace NJTruckCharging.Tests.Unit;

public class TrustedNetworkTests
{
    [Theory]
    [InlineData("127.0.0.1", "127.0.0.0/8", true)]
    [InlineData("::1", "::1/128", true)]
    [InlineData("::ffff:127.0.0.1", "127.0.0.0/8", true)]   // IPv4-mapped loopback (Kestrel dual-stack)
    [InlineData("10.20.30.40", "10.0.0.0/8", true)]
    [InlineData("192.168.1.77", "192.168.1.0/24", true)]
    [InlineData("192.168.2.77", "192.168.1.0/24", false)]
    [InlineData("172.16.5.1", "172.16.0.0/12", true)]
    [InlineData("172.32.0.1", "172.16.0.0/12", false)]
    [InlineData("203.0.113.7", "127.0.0.0/8", false)]
    [InlineData("10.0.0.5", "10.0.0.5", true)]               // bare address = exact host
    [InlineData("10.0.0.6", "10.0.0.5", false)]
    [InlineData("127.0.0.1", "not-a-network", false)]
    public void Cidr_matching(string ip, string cidr, bool expected) =>
        Assert.Equal(expected, RequireMethodologyAdminAttribute.InNetwork(IPAddress.Parse(ip), cidr));
}
