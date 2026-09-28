using System.Diagnostics;
using CodexAccountTray;

namespace CodexAccountTray.Tests;

public sealed class TaskRouterLauncherTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "router-tests-" + Guid.NewGuid().ToString("N"));

    public TaskRouterLauncherTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, recursive: true);
    private TaskRouterRequest Request() => new(_root, "Prüfe die Änderung; keine Veröffentlichung.", []);
    private ProcessStartInfo Info(TaskRouterRequest request, bool execute) => TaskRouterLauncher.BuildStartInfo(
        "python.exe", false, Path.Combine(_root, "router.py"), "codex.exe", Path.Combine(_root, "home"),
        request, Path.Combine(_root, "task.txt"), Path.Combine(_root, "decision"), execute);

    [Fact]
    public void BlankTaskIsRejected() => Assert.Throws<ArgumentException>(() => TaskRouterLauncher.Validate(Request() with { Task = " " }));

    [Fact]
    public void ExcessiveTaskIsRejected() => Assert.Throws<ArgumentException>(() => TaskRouterLauncher.Validate(Request() with { Task = new string('x', 60001) }));

    [Fact]
    public void MissingProjectIsRejected() => Assert.Throws<ArgumentException>(() => TaskRouterLauncher.Validate(Request() with { ProjectDirectory = Path.Combine(_root, "missing") }));

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void InvalidAgentCapIsRejected(int cap) => Assert.Throws<ArgumentException>(() => TaskRouterLauncher.Validate(Request() with { MaxSubagents = cap }));

    [Fact]
    public void NonImageIsRejected()
    {
        string file = Path.Combine(_root, "file.exe"); File.WriteAllText(file, "not an image");
        Assert.Throws<ArgumentException>(() => TaskRouterLauncher.Validate(Request() with { Images = [file] }));
    }

    [Fact]
    public void TooManyImagesAreRejected() => Assert.Throws<ArgumentException>(() => TaskRouterLauncher.Validate(Request() with { Images = Enumerable.Repeat("a.png", 7).ToArray() }));

    [Fact]
    public void MissingPolicyIsRejected() => Assert.Throws<ArgumentException>(() => TaskRouterLauncher.Validate(Request() with { PolicyFile = Path.Combine(_root, "missing.json") }));

    [Fact]
    public void PromptDoesNotEnterArgumentsAndNoShellIsUsed()
    {
        string injection = "hello & whoami | more; $(calc) \" test";
        ProcessStartInfo info = Info(Request() with { Task = injection }, true);
        Assert.False(info.UseShellExecute);
        Assert.DoesNotContain(injection, info.ArgumentList);
        Assert.Contains("--task-file", info.ArgumentList);
        Assert.DoesNotContain("--yolo", info.ArgumentList);
        Assert.DoesNotContain("--dangerously-bypass-approvals-and-sandbox", info.ArgumentList);
    }

    [Fact]
    public void PathsAreDistinctArgumentsEvenWithSpacesAndMetacharacters()
    {
        string project = Path.Combine(_root, "UMR & other project"); Directory.CreateDirectory(project);
        ProcessStartInfo info = Info(Request() with { ProjectDirectory = project }, false);
        Assert.Contains(project, info.ArgumentList);
        Assert.Equal(project, info.ArgumentList[info.ArgumentList.IndexOf("--project") + 1]);
    }

    [Fact]
    public void PreviewNeverEnablesWrites()
    {
        ProcessStartInfo info = Info(Request() with { AllowWrites = true }, false);
        Assert.DoesNotContain("--write", info.ArgumentList);
        Assert.Contains("plan", info.ArgumentList);
        Assert.True(info.CreateNoWindow);
        Assert.True(info.RedirectStandardOutput);
        Assert.True(info.RedirectStandardError);
    }

    [Fact]
    public void ExecutionRequiresExplicitWriteFlag()
    {
        Assert.DoesNotContain("--write", Info(Request(), true).ArgumentList);
        ProcessStartInfo info = Info(Request() with { AllowWrites = true }, true);
        Assert.Contains("--write", info.ArgumentList);
        Assert.Contains("run", info.ArgumentList);
        Assert.False(info.CreateNoWindow);
        Assert.False(info.RedirectStandardOutput);
    }

    [Fact]
    public void ManagedCodexHomeIsPassedWithoutCopyingCredentials()
    {
        ProcessStartInfo info = Info(Request(), false);
        Assert.Equal(Path.Combine(_root, "home"), info.Environment["CODEX_HOME"]);
        Assert.Empty(Directory.GetFiles(_root, "auth.json", SearchOption.AllDirectories));
    }

    [Fact]
    public void ImagesAndPolicyAreForwarded()
    {
        string image = Path.Combine(_root, "reference.PNG"); File.WriteAllText(image, "fixture");
        string policy = Path.Combine(_root, "rules.json"); File.WriteAllText(policy, "{}");
        ProcessStartInfo info = Info(Request() with { Images = [image], PolicyFile = policy, MaxSubagents = 0 }, false);
        Assert.Contains(image, info.ArgumentList); Assert.Contains(policy, info.ArgumentList);
        Assert.Equal("0", info.ArgumentList[info.ArgumentList.IndexOf("--max-subagents") + 1]);
    }

    [Fact]
    public void BlockedPlanDoesNotClaimASelectedModel()
    {
        string report = TaskRouterLauncher.FormatPlan("""{"status":"blocked","blocking_reason":"Reference missing"}""");
        Assert.Contains("Reference missing", report); Assert.DoesNotContain("Modell:", report);
    }

    [Fact]
    public void PlanLabelsCountAsAnUpperBoundNotSpawnEvidence()
    {
        string report = TaskRouterLauncher.FormatPlan("""{"status":"ready","model":"fixture-model","effort":"high","workload":"L","max_concurrent_subagents":1,"reasons":["Test"],"agents":[]}""");
        Assert.Contains("fixture-model", report); Assert.Contains("höchstens 1", report);
        Assert.Contains("keine Zeitprognose", report);
    }

    [Fact]
    public void RequestedStartParametersAreVisibleWithoutClaimingActualExecution()
    {
        string summary = TaskRouterLauncher.FormatRequest(
            Request() with { AllowWrites = true, MaxSubagents = 1 }, execute: true);

        Assert.Contains("neue Codex-Sitzung", summary);
        Assert.Contains("Schreibfreigabe: EIN", summary);
        Assert.Contains("höchstens 1", summary);
        Assert.Contains("mitgelieferte Standardregeln", summary);
        Assert.DoesNotContain("ausgeführt", summary.ToLowerInvariant());
    }
}
