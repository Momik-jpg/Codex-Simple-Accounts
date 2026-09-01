using System.Text.Json;

namespace CodexAccountTray;

public sealed class TurnActivityTracker
{
    private readonly object _sync = new();
    private readonly HashSet<string> _activeTurns = [];
    private TaskCompletionSource _idle = Completed();

    public bool HasActiveTurns
    {
        get
        {
            lock (_sync)
            {
                return _activeTurns.Count > 0;
            }
        }
    }

    public void Observe(string message)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(message);
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("method", out JsonElement methodElement) ||
                !root.TryGetProperty("params", out JsonElement parameters) ||
                !TryReadTurnId(parameters, out string? turnId))
            {
                return;
            }

            lock (_sync)
            {
                switch (methodElement.GetString())
                {
                    case "turn/started":
                        if (_activeTurns.Count == 0)
                        {
                            _idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        }
                        _activeTurns.Add(turnId);
                        break;
                    case "turn/completed":
                    case "turn/aborted":
                        _activeTurns.Remove(turnId);
                        if (_activeTurns.Count == 0)
                        {
                            _idle.TrySetResult();
                        }
                        break;
                }
            }
        }
        catch (JsonException)
        {
        }
    }

    public Task WaitForIdleAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            return _idle.Task.WaitAsync(cancellationToken);
        }
    }

    private static bool TryReadTurnId(JsonElement parameters, out string turnId)
    {
        turnId = string.Empty;
        if (parameters.TryGetProperty("turn", out JsonElement turn) &&
            turn.TryGetProperty("id", out JsonElement nestedId) &&
            nestedId.GetString() is { Length: > 0 } nested)
        {
            turnId = nested;
            return true;
        }
        if (parameters.TryGetProperty("turnId", out JsonElement directId) &&
            directId.GetString() is { Length: > 0 } direct)
        {
            turnId = direct;
            return true;
        }
        return false;
    }

    private static TaskCompletionSource Completed()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        completion.SetResult();
        return completion;
    }
}
