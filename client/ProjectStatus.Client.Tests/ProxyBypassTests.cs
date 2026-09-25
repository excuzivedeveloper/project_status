using ProjectStatus.Client;
using Xunit;

namespace ProjectStatus.Client.Tests;

// A VPN client that sets a Windows system proxy can intercept and stall requests to
// the private server while direct access works. Server traffic must bypass the proxy;
// update/internet traffic keeps the normal Windows networking behavior.
public class ProxyBypassTests
{
    [Fact]
    public void Server_transport_disables_the_system_proxy()
    {
        using var handler = ApiClient.CreateServerHandler();

        Assert.False(handler.UseProxy);
    }
}
