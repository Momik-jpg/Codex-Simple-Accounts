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
    public void RenamedTextFileIsNotAcceptedAsImage()
    {
        string image = Path.Combine(_root, "fake.png");
        File.WriteAllText(image, "not a PNG image");
        Assert.Throws<ArgumentException>(() => TaskRouterLauncher.Validate(Request() with { Images = [image] }));
    }

    [Fact]
    public void TooManyImagesAreRejected() => Assert.Throws<ArgumentException>(() => TaskRouterLauncher.Validate(Request() with { Images = Enumerable.Repeat("a.png", 7).ToArray() }));

    [Fact]
    public void MissingPolicyIsRejected() => Assert.Throws<ArgumentException>(() => TaskRouterLauncher.Validate(Request() with { PolicyFile = Path.Combine(_root, "missing.json") }));

    [Theory]
    [InlineData("turbo", "auto")]
    [InlineData("auto", "huge")]
    public void UnknownRoutingPreferencesAreRejected(string profile, string effort) =>
        Assert.Throws<ArgumentException>(() => TaskRouterLauncher.Validate(
            Request() with { Profile = profile, Effort = effort }));

    [Fact]
    public void MissingCachedAssessmentIsRejected() => Assert.Throws<ArgumentException>(() =>
        TaskRouterLauncher.Validate(Request() with { AssessmentFile = Path.Combine(_root, "missing-assessment.json") }));

    [Fact]
    public void ExpectedPlanRequiresAnExistingAssessment()
    {
        string plan = Path.Combine(_root, "plan.json");
        File.WriteAllText(plan, "{}");
        Assert.Throws<ArgumentException>(() => TaskRouterLauncher.Validate(
            Request() with { ExpectedPlan = plan }));
    }

    [Fact]
    public void InvalidManualModelIdIsRejected() => Assert.Throws<ArgumentException>(() =>
        TaskRouterLauncher.Validate(Request() with { Model = "--dangerously-bypass-approvals-and-sandbox" }));

    [Fact]
    public void LiveCatalogParsingKeepsOnlySupportedVisibleModels()
    {
        string catalog = """{"data":[{"model":"gpt-test","hidden":false,"inputModalities":["text","image"],"supportedReasoningEfforts":[{"reasoningEffort":"low"},{"reasoningEffort":"high"}]},{"model":"hidden","hidden":true,"supportedReasoningEfforts":[{"reasoningEffort":"low"}]}]}""";
        IReadOnlyList<TaskRouterModel> models = TaskRouterLauncher.ParseModels(catalog);
        Assert.Single(models);
        Assert.Equal("gpt-test", models[0].Id);
        Assert.Equal(["low", "high"], models[0].Efforts);
        Assert.True(models[0].SupportsImages);
    }

    [Fact]
    public void MissingImageCapabilityIsNotAssumed()
    {
        string catalog = """{"data":[{"model":"text-only","supportedReasoningEfforts":[{"reasoningEffort":"low"}]}]}""";
        Assert.False(TaskRouterLauncher.ParseModels(catalog)[0].SupportsImages);
    }

    [Fact]
    public void MalformedLiveCatalogFailsClosed() => Assert.Throws<FormatException>(() =>
        TaskRouterLauncher.ParseModels("{\"data\":[]}"));

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
        string image = Path.Combine(_root, "reference.PNG");
        File.WriteAllBytes(image, [137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 0]);
        string policy = Path.Combine(_root, "rules.json"); File.WriteAllText(policy, "{}");
        ProcessStartInfo info = Info(Request() with { Images = [image], PolicyFile = policy, MaxSubagents = 0 }, false);
        Assert.Contains(image, info.ArgumentList); Assert.Contains(policy, info.ArgumentList);
        Assert.Equal("0", info.ArgumentList[info.ArgumentList.IndexOf("--max-subagents") + 1]);
    }

    [Fact]
    public void RoutingPreferencesAndCachedAssessmentAreForwarded()
    {
        string assessment = Path.Combine(_root, "assessment.json");
        File.WriteAllText(assessment, "{}");
        string plan = Path.Combine(_root, "plan.json");
        File.WriteAllText(plan, "{}");

        ProcessStartInfo info = Info(Request() with
        {
            Profile = "deep",
            Effort = "xhigh",
            AssessmentFile = assessment,
            ExpectedPlan = plan,
            Model = "gpt-6-astra"
        }, true);

        Assert.Equal("deep", info.ArgumentList[info.ArgumentList.IndexOf("--profile") + 1]);
        Assert.Equal("xhigh", info.ArgumentList[info.ArgumentList.IndexOf("--effort") + 1]);
        Assert.Equal("gpt-6-astra", info.ArgumentList[info.ArgumentList.IndexOf("--model") + 1]);
        Assert.Equal(assessment, info.ArgumentList[info.ArgumentList.IndexOf("--assessment-file") + 1]);
        Assert.Equal(plan, info.ArgumentList[info.ArgumentList.IndexOf("--expected-plan") + 1]);
    }

    [Fact]
    public void BlockedPlanDoesNotClaimASelectedModel()
    {
        string report = TaskRouterLauncher.FormatPlan("""{"status":"blocked","blocking_reason":"Reference missing","plan_steps":[{"action":"Ask for image","verification":"Image received"}]}""");
        Assert.Contains("Reference missing", report); Assert.DoesNotContain("Modell:", report);
        Assert.Contains("Vorgeschlagener Klärungsschritt (ungeprüft): Ask for image", report);
    }

    [Fact]
    public void PlanLabelsCountAsAnUpperBoundNotSpawnEvidence()
    {
        string report = TaskRouterLauncher.FormatPlan("""{"status":"ready","model":"fixture-model","effort":"high","workload":"L","max_concurrent_subagents":1,"reasons":["Test"],"agents":[]}""");
        Assert.Contains("fixture-model", report); Assert.Contains("höchstens 1", report);
        Assert.Contains("keine Zeitprognose", report);
    }

    [Fact]
    public void PlanShowsProvisionalActionsAndUnverifiedChecks()
    {
        string report = TaskRouterLauncher.FormatPlan("""{"status":"ready","goal":"Testziel","model":"fixture-model","effort":"high","workload":"M","max_concurrent_subagents":0,"plan_steps":[{"action":"Projekt ansehen","verification":"Dateien bestätigen"}],"acceptance_checks":["Test bestehen"]}""");
        Assert.Contains("Ziel: Testziel", report);
        Assert.Contains("KI-Arbeitsplan · Entwurf vor Projektinspektion", report);
        Assert.Contains("1. Projekt ansehen", report);
        Assert.Contains("Prüfen: Dateien bestätigen", report);
        Assert.Contains("Abschlusskriterien · noch nicht geprüft", report);
        Assert.Contains("Test bestehen", report);
    }

    [Fact]
    public void RequestedStartParametersAreVisibleWithoutClaimingActualExecution()
    {
        string summary = TaskRouterLauncher.FormatRequest(
            Request() with { AllowWrites = true, MaxSubagents = 1 }, execute: true);

        Assert.Contains("neue Codex-Sitzung", summary);
        Assert.Contains("Schreibfreigabe: EIN", summary);
        Assert.Contains("Qualitätsprofil: Automatisch", summary);
        Assert.Contains("Denkaufwand: Automatisch", summary);
        Assert.Contains("höchstens 1", summary);
        Assert.Contains("mitgelieferte Standardregeln", summary);
        Assert.DoesNotContain("ausgeführt", summary.ToLowerInvariant());
    }

    [Fact]
    public void RequestedManualPreferencesAndReusedAssessmentAreVisible()
    {
        string assessment = Path.Combine(_root, "assessment.json");
        File.WriteAllText(assessment, "{}");

        string summary = TaskRouterLauncher.FormatRequest(Request() with
        {
            Profile = "deep",
            Effort = "max",
            AssessmentFile = assessment
        }, execute: true);

        Assert.Contains("Qualitätsprofil: Gründlich", summary);
        Assert.Contains("Maximum (ausdrücklich gewählt)", summary);
        Assert.Contains("vorhandene Einschätzung wiederverwenden", summary);
    }
}
