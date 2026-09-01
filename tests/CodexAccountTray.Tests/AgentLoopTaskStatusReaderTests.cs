using System.Text.Json;
using CodexAccountTray;

namespace CodexAccountTray.Tests;

public sealed class AgentLoopTaskStatusReaderTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"agentloop-status-{Guid.NewGuid():N}");

    [Fact]
    public void Read_ReturnsWaitingTaskWithoutPromptContent()
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "task-status.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            taskId = "run-42",
            status = "waiting",
            reasonCode = "usage_limit",
            reason = "Kontingent wartet auf Reset",
            lastSuccessfulRound = 2,
            updatedAt = "2030-01-02T03:04:05Z"
        }));

        AgentLoopTaskStatus? status = new AgentLoopTaskStatusReader(path).Read();

        Assert.NotNull(status);
        Assert.Equal("run-42", status.TaskId);
        Assert.Equal("Kontingent wartet auf Reset", status.Reason);
        Assert.Equal("Task wartet · Runde 2 · Kontingent wartet auf Reset", AgentLoopTaskStatusFormatter.Format(status));
    }

    [Fact]
    public void Read_InvalidOrMissingFileDoesNotBreakTheTrayApp()
    {
        string path = Path.Combine(_directory, "missing.json");
        var reader = new AgentLoopTaskStatusReader(path);
        Assert.Null(reader.Read());

        Directory.CreateDirectory(_directory);
        File.WriteAllText(path, "not json");
        Assert.Null(reader.Read());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
