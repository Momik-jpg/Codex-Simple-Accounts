using System.Text.Json;
using System.Net.WebSockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace FakeCodex;

public static class Marker;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        if (args.Contains("login"))
        {
            string home = Environment.GetEnvironmentVariable("CODEX_HOME")!;
            Directory.CreateDirectory(home);
            File.WriteAllText(
                Path.Combine(home, "auth.json"),
                Environment.GetEnvironmentVariable("FAKE_LOGIN_AUTH") ?? "{\"fake\":true}");
            return;
        }

        if (!args.Contains("app-server"))
        {
            string? log = Environment.GetEnvironmentVariable("FAKE_CODEX_LOG");
            if (!string.IsNullOrWhiteSpace(log))
            {
                File.WriteAllLines(log, args);
            }
            int delay = int.TryParse(Environment.GetEnvironmentVariable("FAKE_CODEX_DELAY_MS"), out int parsed)
                ? parsed
                : 50;
            Thread.Sleep(delay);
            return;
        }

        int listenIndex = Array.IndexOf(args, "--listen");
        if (listenIndex >= 0 && listenIndex + 1 < args.Length &&
            Uri.TryCreate(args[listenIndex + 1], UriKind.Absolute, out Uri? listenUri) &&
            listenUri.Scheme == "ws")
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseKestrel(options => options.ListenLocalhost(listenUri.Port));
            WebApplication app = builder.Build();
            app.UseWebSockets();
            app.Run(async context =>
            {
                using WebSocket socket = await context.WebSockets.AcceptWebSocketAsync();
                byte[] buffer = new byte[4096];
                while (socket.State == WebSocketState.Open)
                {
                    WebSocketReceiveResult result = await socket.ReceiveAsync(buffer, context.RequestAborted);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }
                    await socket.SendAsync(buffer.AsMemory(0, result.Count), result.MessageType,
                        result.EndOfMessage, context.RequestAborted);
                }
            });
            await app.RunAsync();
            return;
        }

        while (Console.ReadLine() is { } line)
        {
            using JsonDocument request = JsonDocument.Parse(line);
            int id = request.RootElement.TryGetProperty("id", out JsonElement value)
                ? value.GetInt32()
                : 0;

            if (id == 1)
            {
                Console.WriteLine("{\"id\":1,\"result\":{}}");
            }
            else if (id == 2)
            {
                int used = int.TryParse(Environment.GetEnvironmentVariable("FAKE_CODEX_USED_PERCENT"), out int parsed)
                    ? parsed
                    : 99;
                Console.WriteLine($"{{\"id\":2,\"result\":{{\"rateLimits\":{{\"primary\":{{\"usedPercent\":{used},\"resetsAt\":1893456000,\"windowDurationMins\":300}},\"secondary\":{{\"usedPercent\":40,\"resetsAt\":1894060800,\"windowDurationMins\":10080}}}}}}}}");
            }
        }
    }
}
