using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexAccountTray;

public sealed record AgentLoopTaskStatus(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("taskId")] string TaskId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("reasonCode")] string ReasonCode,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("lastSuccessfulRound")] int LastSuccessfulRound,
    [property: JsonPropertyName("updatedAt")] DateTimeOffset UpdatedAt);

public sealed class AgentLoopTaskStatusReader
{
    public AgentLoopTaskStatusReader(string? path = null)
    {
        Path = path ?? System.IO.Path.Combine(
            WindowsUserProfile.LocalAppData(),
            "AgentLoop",
            "task-status.json");
    }

    public string Path { get; }

    public AgentLoopTaskStatus? Read()
    {
        try
        {
            if (!File.Exists(Path))
            {
                return null;
            }
            AgentLoopTaskStatus? status = JsonSerializer.Deserialize<AgentLoopTaskStatus>(
                File.ReadAllText(Path));
            return status is
            {
                SchemaVersion: 1,
                Status: "waiting",
                TaskId.Length: > 0,
                Reason.Length: > 0
            }
                ? status
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }
}

public static class AgentLoopTaskStatusFormatter
{
    public static string Format(AgentLoopTaskStatus? status)
    {
        if (status is null)
        {
            return "Kein wartender AgentLoop-Task";
        }
        string round = status.LastSuccessfulRound > 0
            ? $" · Runde {status.LastSuccessfulRound}"
            : string.Empty;
        return $"Task wartet{round} · {status.Reason}";
    }
}
