using System.Diagnostics;
using System.Text.Json;

namespace CodexAccountTray;

public interface ICodexProtocolClient
{
    Task<AccountLimits> ReadLimitsAsync(string codexHome, CancellationToken cancellationToken);
}

public sealed class CodexProtocolClient : ICodexProtocolClient
{
    private readonly CodexCommand _command;
    private readonly TimeSpan _timeout;

    public CodexProtocolClient(CodexCommand command, TimeSpan? timeout = null)
    {
        _command = command;
        _timeout = timeout ?? TimeSpan.FromSeconds(30);
    }

    public async Task<AccountLimits> ReadLimitsAsync(string codexHome, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(codexHome);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);

        var startInfo = new ProcessStartInfo
        {
            FileName = _command.FileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in _command.PrefixArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        startInfo.ArgumentList.Add("app-server");
        startInfo.ArgumentList.Add("--listen");
        startInfo.ArgumentList.Add("stdio://");
        startInfo.Environment["CODEX_HOME"] = codexHome;

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        Task stderrDrain = process.StandardError.ReadToEndAsync(timeout.Token);

        try
        {
            await process.StandardInput.WriteLineAsync(
                "{\"id\":1,\"method\":\"initialize\",\"params\":{\"clientInfo\":{\"name\":\"codex-account-tray\",\"version\":\"1.0\"}}}");
            await process.StandardInput.WriteLineAsync("{\"method\":\"initialized\"}");
            await process.StandardInput.WriteLineAsync(
                "{\"id\":2,\"method\":\"account/rateLimits/read\",\"params\":null}");
            await process.StandardInput.FlushAsync(timeout.Token);

            while (await process.StandardOutput.ReadLineAsync(timeout.Token) is { } line)
            {
                using JsonDocument message = JsonDocument.Parse(line);
                JsonElement root = message.RootElement;
                if (!root.TryGetProperty("id", out JsonElement id) || id.GetInt32() != 2)
                {
                    continue;
                }
                if (root.TryGetProperty("error", out _))
                {
                    throw new InvalidOperationException("Codex konnte die Limits nicht lesen.");
                }

                JsonElement result = root.GetProperty("result");
                JsonElement snapshot = result.GetProperty("rateLimits");
                if (result.TryGetProperty("rateLimitsByLimitId", out JsonElement byId) &&
                    byId.ValueKind == JsonValueKind.Object &&
                    byId.TryGetProperty("codex", out JsonElement codexSnapshot))
                {
                    snapshot = codexSnapshot;
                }

                return new AccountLimits(
                    ReadWindow(snapshot, "primary"),
                    ReadWindow(snapshot, "secondary"),
                    DateTimeOffset.Now);
            }

            throw new InvalidOperationException("Codex beendete die Limitabfrage ohne Antwort.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Die Codex-Limitabfrage dauerte zu lange.");
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(true);
            }
            try
            {
                await stderrDrain;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private static RateLimitWindow? ReadWindow(JsonElement snapshot, string propertyName)
    {
        if (!snapshot.TryGetProperty(propertyName, out JsonElement window) ||
            window.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        int used = window.GetProperty("usedPercent").GetInt32();
        long? resetsAt = ReadNullableInt64(window, "resetsAt");
        long? duration = ReadNullableInt64(window, "windowDurationMins");
        return new RateLimitWindow(used, resetsAt, duration);
    }

    private static long? ReadNullableInt64(JsonElement parent, string propertyName)
    {
        return parent.TryGetProperty(propertyName, out JsonElement value) &&
               value.ValueKind == JsonValueKind.Number
            ? value.GetInt64()
            : null;
    }
}
