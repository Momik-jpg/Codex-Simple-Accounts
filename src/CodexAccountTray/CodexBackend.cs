using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;

namespace CodexAccountTray;

public sealed class CodexBackend : IAsyncDisposable
{
    private readonly Process _process;

    private CodexBackend(Process process, Uri uri)
    {
        _process = process;
        Uri = uri;
    }

    public Uri Uri { get; }

    public static async Task<CodexBackend> StartAsync(
        CodexCommand command,
        string codexHome,
        CancellationToken cancellationToken)
    {
        int port = ReservePort();
        var info = new ProcessStartInfo
        {
            FileName = command.FileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in command.PrefixArguments)
        {
            info.ArgumentList.Add(argument);
        }
        info.ArgumentList.Add("app-server");
        info.ArgumentList.Add("--listen");
        info.ArgumentList.Add($"ws://127.0.0.1:{port}");
        info.Environment["CODEX_HOME"] = codexHome;

        var process = new Process { StartInfo = info };
        process.Start();
        _ = process.StandardOutput.ReadToEndAsync();
        _ = process.StandardError.ReadToEndAsync();
        var backend = new CodexBackend(process, new Uri($"ws://127.0.0.1:{port}/"));
        try
        {
            await backend.WaitUntilReadyAsync(cancellationToken);
            return backend;
        }
        catch
        {
            await backend.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!_process.HasExited)
        {
            _process.Kill(true);
            await _process.WaitForExitAsync();
        }
        _process.Dispose();
    }

    private async Task WaitUntilReadyAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (!timeout.IsCancellationRequested)
        {
            if (_process.HasExited)
            {
                throw new InvalidOperationException("Der Codex-App-Server wurde unerwartet beendet.");
            }
            try
            {
                using var socket = new ClientWebSocket();
                await socket.ConnectAsync(Uri, timeout.Token);
                socket.Abort();
                return;
            }
            catch (WebSocketException)
            {
                await Task.Delay(100, timeout.Token);
            }
        }
        throw new TimeoutException("Der Codex-App-Server ist nicht bereit.");
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
