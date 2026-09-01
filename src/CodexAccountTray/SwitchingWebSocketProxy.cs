using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CodexAccountTray;

public sealed class SwitchingWebSocketProxy : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly TurnActivityTracker _turns;
    private readonly ConcurrentDictionary<Guid, WebSocket> _clients = new();
    private WebApplication? _application;
    private Uri? _backend;

    public SwitchingWebSocketProxy(TurnActivityTracker turns)
    {
        _turns = turns;
    }

    public Uri? ListenUri { get; private set; }

    public bool IsRunning => _application is not null;

    public async Task StartAsync(int port, Uri backend, CancellationToken cancellationToken)
    {
        if (_application is not null)
        {
            return;
        }

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel(options => options.ListenLocalhost(port));
        WebApplication application = builder.Build();
        application.UseWebSockets();
        application.Run(HandleAsync);

        lock (_sync)
        {
            _backend = backend;
        }
        await application.StartAsync(cancellationToken);
        _application = application;
        ListenUri = new Uri($"ws://127.0.0.1:{port}/");
    }

    public async Task SwitchAsync(Uri backend, CancellationToken cancellationToken)
    {
        await _turns.WaitForIdleAsync(cancellationToken);
        lock (_sync)
        {
            _backend = backend;
        }

        WebSocket[] clients = _clients.Values.ToArray();
        foreach (WebSocket client in clients)
        {
            try
            {
                if (client.State == WebSocketState.Open)
                {
                    await client.CloseOutputAsync(
                        WebSocketCloseStatus.EndpointUnavailable,
                        "Konto gewechselt",
                        cancellationToken);
                }
            }
            catch (WebSocketException)
            {
            }
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        WebApplication? application = _application;
        _application = null;
        ListenUri = null;
        if (application is null)
        {
            return;
        }
        foreach (WebSocket client in _clients.Values)
        {
            client.Abort();
        }
        await application.StopAsync(cancellationToken);
        await application.DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
    }

    private async Task HandleAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        Uri backend;
        lock (_sync)
        {
            backend = _backend ?? throw new InvalidOperationException("Kein Backend aktiv.");
        }

        using WebSocket client = await context.WebSockets.AcceptWebSocketAsync();
        using var server = new ClientWebSocket();
        await server.ConnectAsync(backend, context.RequestAborted);
        Guid id = Guid.NewGuid();
        _clients[id] = client;
        try
        {
            Task upstream = RelayAsync(client, server, observeTurns: false, context.RequestAborted);
            Task downstream = RelayAsync(server, client, observeTurns: true, context.RequestAborted);
            await Task.WhenAny(upstream, downstream);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
        }
        catch (WebSocketException)
        {
        }
        finally
        {
            _clients.TryRemove(id, out _);
            await CloseQuietlyAsync(client);
            await CloseQuietlyAsync(server);
        }
    }

    private async Task RelayAsync(
        WebSocket source,
        WebSocket destination,
        bool observeTurns,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[64 * 1024];
        using var text = new MemoryStream();
        while (!cancellationToken.IsCancellationRequested && source.State == WebSocketState.Open)
        {
            WebSocketReceiveResult result = await source.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                break;
            }
            await destination.SendAsync(
                buffer.AsMemory(0, result.Count),
                result.MessageType,
                result.EndOfMessage,
                cancellationToken);
            if (observeTurns && result.MessageType == WebSocketMessageType.Text)
            {
                text.Write(buffer, 0, result.Count);
                if (result.EndOfMessage)
                {
                    _turns.Observe(Encoding.UTF8.GetString(text.ToArray()));
                    text.SetLength(0);
                }
            }
        }
    }

    private static async Task CloseQuietlyAsync(WebSocket socket)
    {
        try
        {
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
            }
        }
        catch (WebSocketException)
        {
        }
    }
}
