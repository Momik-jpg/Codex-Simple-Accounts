using CodexAccountTray;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;

namespace CodexAccountTray.Tests;

public sealed class SwitchingWebSocketProxyTests
{
    [Fact]
    public async Task SwitchAsync_WaitsForActiveTurnToComplete()
    {
        var turns = new TurnActivityTracker();
        await using var proxy = new SwitchingWebSocketProxy(turns);
        turns.Observe("{\"method\":\"turn/started\",\"params\":{\"turn\":{\"id\":\"t1\"}}}");

        Task switching = proxy.SwitchAsync(new Uri("ws://127.0.0.1:45000"), CancellationToken.None);
        Assert.False(switching.IsCompleted);

        turns.Observe("{\"method\":\"turn/completed\",\"params\":{\"turn\":{\"id\":\"t1\"}}}");
        await switching;
    }

    [Fact]
    public async Task Proxy_RelaysWebSocketMessagesThroughBackend()
    {
        string root = Path.Combine(Path.GetTempPath(), $"CodexProxy-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            string fakeAssembly = typeof(FakeCodex.Marker).Assembly.Location;
            await using CodexBackend backend = await CodexBackend.StartAsync(
                new CodexCommand("dotnet", [fakeAssembly]), root, CancellationToken.None);
            await using var proxy = new SwitchingWebSocketProxy(new TurnActivityTracker());
            await proxy.StartAsync(ReservePort(), backend.Uri, CancellationToken.None);
            using var client = new ClientWebSocket();
            await client.ConnectAsync(proxy.ListenUri!, CancellationToken.None);
            byte[] sent = Encoding.UTF8.GetBytes("{\"id\":1}");
            await client.SendAsync(sent, WebSocketMessageType.Text, true, CancellationToken.None);
            byte[] received = new byte[256];
            WebSocketReceiveResult result = await client.ReceiveAsync(received, CancellationToken.None);
            Assert.Equal("{\"id\":1}", Encoding.UTF8.GetString(received, 0, result.Count));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static int ReservePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
