using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace CodexAccountTray;

public sealed record TaskRouterRequest(
    string ProjectDirectory,
    string Task,
    IReadOnlyList<string> Images,
    int MaxSubagents = 2,
    bool AllowWrites = false,
    string? PolicyFile = null,
    string Profile = "auto",
    string Effort = "auto",
    string? AssessmentFile = null);

public sealed record TaskRouterPreferences(
    string Profile = "auto",
    string Effort = "auto",
    int MaxSubagents = 2)
{
    public static readonly string[] Profiles = ["auto", "fast", "balanced", "deep"];
    public static readonly string[] Efforts = ["auto", "minimal", "low", "medium", "high", "xhigh", "max", "ultra"];

    public TaskRouterPreferences Normalize() => new(
        Profiles.Contains(Profile, StringComparer.Ordinal) ? Profile : "auto",
        Efforts.Contains(Effort, StringComparer.Ordinal) ? Effort : "auto",
        Math.Clamp(MaxSubagents, 0, 2));
}

public sealed record TaskRouterResult(int ExitCode, string RunDirectory, string Report);

/// <summary>Launches the bundled router without a shell or changes to account credentials.</summary>
public sealed class TaskRouterLauncher
{
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".webp"];
    private readonly CodexCommand _codex;
    private readonly string _routerDirectory;
    private readonly string _runsDirectory;

    public TaskRouterLauncher(CodexCommand codex, string? routerDirectory = null, string? runsDirectory = null)
    {
        _codex = codex;
        _routerDirectory = routerDirectory ?? Path.Combine(AppContext.BaseDirectory, "TaskRouter");
        _runsDirectory = runsDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexAccountTray", "RouterRuns");
    }

    public static void Validate(TaskRouterRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ProjectDirectory) ||
            !Path.IsPathFullyQualified(request.ProjectDirectory) || !Directory.Exists(request.ProjectDirectory))
            throw new ArgumentException("Bitte einen vorhandenen, absoluten Projektordner wählen.");
        if (string.IsNullOrWhiteSpace(request.Task) || request.Task.Length > 60000)
            throw new ArgumentException("Der Auftrag muss 1 bis 60000 Zeichen enthalten.");
        if (request.MaxSubagents is < 0 or > 2)
            throw new ArgumentException("Die Obergrenze für Subagenten muss 0, 1 oder 2 sein.");
        if (!TaskRouterPreferences.Profiles.Contains(request.Profile, StringComparer.Ordinal))
            throw new ArgumentException("Das Qualitätsprofil muss auto, fast, balanced oder deep sein.");
        if (!TaskRouterPreferences.Efforts.Contains(request.Effort, StringComparer.Ordinal))
            throw new ArgumentException("Die Denkstufe ist unbekannt.");
        if (request.Images is null || request.Images.Count > 6)
            throw new ArgumentException("Höchstens sechs Referenzbilder auswählen.");
        foreach (string image in request.Images)
        {
            if (!File.Exists(image) || !Path.IsPathFullyQualified(image) ||
                !IsSupportedImage(image))
                throw new ArgumentException($"Referenzbild fehlt oder hat ein ungeeignetes Format: {image}");
        }
        if (!string.IsNullOrEmpty(request.PolicyFile) &&
            (!Path.IsPathFullyQualified(request.PolicyFile) || !File.Exists(request.PolicyFile)))
            throw new ArgumentException("Die gewählte Regeldatei existiert nicht.");
        if (!string.IsNullOrEmpty(request.AssessmentFile) &&
            (!Path.IsPathFullyQualified(request.AssessmentFile) || !File.Exists(request.AssessmentFile)))
            throw new ArgumentException("Die wiederzuverwendende Einstufung existiert nicht.");
    }

    public static bool IsSupportedImage(string path) =>
        ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public static string FormatRequest(TaskRouterRequest request, bool execute)
    {
        ArgumentNullException.ThrowIfNull(request);
        string writes = execute && request.AllowWrites
            ? "EIN (nur lokale Änderungen; der Router darf read-only wählen)"
            : "AUS";
        string policy = string.IsNullOrWhiteSpace(request.PolicyFile)
            ? "mitgelieferte Standardregeln"
            : request.PolicyFile!;
        string assessment = string.IsNullOrWhiteSpace(request.AssessmentFile)
            ? "neu erstellen"
            : "vorhandene Einschätzung wiederverwenden; Live-Katalog und Regeln neu prüfen";
        return string.Join("\r\n", new[]
        {
            "Angeforderte Startparameter",
            $"Modus: {(execute ? "Einstufen und neue Codex-Sitzung starten" : "Nur einstufen")}",
            $"Projekt: {request.ProjectDirectory}",
            $"Lokale Schreibfreigabe: {writes}",
            $"Qualitätsprofil: {FormatProfile(request.Profile)}",
            $"Denkaufwand: {FormatEffort(request.Effort)}",
            $"Nebenrollen: höchstens {request.MaxSubagents}",
            $"Referenzbilder: {request.Images.Count}",
            $"Regeln: {policy}",
            $"Einstufung: {assessment}"
        });
    }

    private static string FormatProfile(string profile) => profile switch
    {
        "fast" => "Schnell",
        "balanced" => "Ausgewogen",
        "deep" => "Gründlich",
        _ => "Automatisch"
    };

    private static string FormatEffort(string effort) => effort switch
    {
        "minimal" => "Minimal",
        "low" => "Niedrig",
        "medium" => "Mittel",
        "high" => "Hoch",
        "xhigh" => "Sehr hoch",
        "max" => "Maximum (ausdrücklich gewählt)",
        "ultra" => "Ultra (ausdrücklich gewählt)",
        _ => "Automatisch"
    };

    public static ProcessStartInfo BuildStartInfo(
        string python, bool usePythonLauncher, string routerScript, string codexPath,
        string codexHome, TaskRouterRequest request, string taskFile, string outputDirectory, bool execute)
    {
        Validate(request);
        var info = new ProcessStartInfo
        {
            FileName = python,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(routerScript))!,
            UseShellExecute = false,
            CreateNoWindow = !execute,
            RedirectStandardOutput = !execute,
            RedirectStandardError = !execute
        };
        if (usePythonLauncher) info.ArgumentList.Add("-3");
        foreach (string argument in new[] { "-X", "utf8", routerScript, Path.Combine(Path.GetDirectoryName(taskFile)!, "error.log"), execute ? "run" : "plan",
                     "--project", request.ProjectDirectory, "--task-file", taskFile,
                     "--codex", codexPath, "--max-subagents", request.MaxSubagents.ToString(),
                     "--profile", request.Profile, "--effort", request.Effort,
                     "--out", outputDirectory })
            info.ArgumentList.Add(argument);
        if (execute && request.AllowWrites) info.ArgumentList.Add("--write");
        if (!string.IsNullOrEmpty(request.PolicyFile))
        {
            info.ArgumentList.Add("--policy");
            info.ArgumentList.Add(request.PolicyFile);
        }
        if (!string.IsNullOrEmpty(request.AssessmentFile))
        {
            info.ArgumentList.Add("--assessment-file");
            info.ArgumentList.Add(request.AssessmentFile);
        }
        foreach (string image in request.Images)
        {
            info.ArgumentList.Add("--image");
            info.ArgumentList.Add(image);
        }
        if (!execute)
        {
            info.StandardOutputEncoding = Encoding.UTF8;
            info.StandardErrorEncoding = Encoding.UTF8;
        }
        info.Environment["CODEX_HOME"] = codexHome;
        info.Environment["PYTHONUTF8"] = "1";
        return info;
    }

    public async Task<TaskRouterResult> RunAsync(TaskRouterRequest request, string codexHome,
        bool execute, CancellationToken cancellationToken)
    {
        Validate(request);
        string script = Path.Combine(_routerDirectory, "desktop_entry.py");
        if (!File.Exists(script))
            throw new FileNotFoundException("TaskRouter-Dateien fehlen. Die App vollständig neu bauen/installieren.", script);
        (string python, bool launcher) = FindPython();
        string codexPath = _codex.PrefixArguments.Count == 0 ? _codex.FileName :
            _codex.PrefixArguments.Count == 1 && _codex.PrefixArguments[0].EndsWith(".js", StringComparison.OrdinalIgnoreCase)
                ? _codex.PrefixArguments[0]
                : throw new InvalidOperationException("Dieser Codex-Starter wird vom Router nicht unterstützt.");
        // A local per-user run directory: task text never enters the command line or repository.
        string run = Path.Combine(_runsDirectory, $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(run);
        string taskFile = Path.Combine(run, "task.txt");
        await File.WriteAllTextAsync(taskFile, request.Task, new UTF8Encoding(false), cancellationToken);
        string outputDirectory = Path.Combine(run, "decision"); // The router creates this exclusively.
        using var process = new Process { StartInfo = BuildStartInfo(python, launcher, script,
            codexPath, codexHome, request, taskFile, outputDirectory, execute) };
        cancellationToken.ThrowIfCancellationRequested();
        process.Start();
        Task<string> stdout = execute ? System.Threading.Tasks.Task.FromResult(string.Empty)
            : process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = execute ? System.Threading.Tasks.Task.FromResult(string.Empty)
            : process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(stdout, stderr);
            throw;
        }
        string log = (await stdout) + Environment.NewLine + (await stderr);
        string errorFile = Path.Combine(run, "error.log");
        if (File.Exists(errorFile)) log += Environment.NewLine + await File.ReadAllTextAsync(errorFile, CancellationToken.None);
        if (!string.IsNullOrWhiteSpace(log))
            await File.WriteAllTextAsync(Path.Combine(run, "launcher.log"), log, Encoding.UTF8, CancellationToken.None);
        string report = File.Exists(Path.Combine(outputDirectory, "plan.json"))
            ? FormatPlan(await File.ReadAllTextAsync(Path.Combine(outputDirectory, "plan.json"), CancellationToken.None))
            : "Keine gültige Routing-Entscheidung vorhanden.";
        report += $"\r\n\r\nExit-Code: {process.ExitCode}. " +
            (execute ? "Das ist kein Nachweis fachlich bestandener Tests. Den Codex-Bericht prüfen."
                     : "Nur Einstufung; der Projektauftrag wurde nicht ausgeführt.");
        if (process.ExitCode != 0)
            report += "\r\n" + (string.IsNullOrWhiteSpace(log)
                ? "Details im Terminal und im lokalen Laufordner prüfen. Python 3.10+ und aktuelle Codex CLI erforderlich."
                : log[..Math.Min(log.Length, 6000)]);
        return new TaskRouterResult(process.ExitCode, run, report);
    }

    public static string FormatPlan(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        if (root.GetProperty("status").GetString() == "blocked")
            return "Blockiert: " + root.GetProperty("blocking_reason").GetString();
        var text = new StringBuilder();
        text.AppendLine($"Modell: {root.GetProperty("model").GetString()}");
        text.AppendLine($"Denkaufwand: {root.GetProperty("effort").GetString()}");
        if (root.TryGetProperty("requested_tier", out JsonElement tier))
            text.AppendLine($"Qualitätsprofil: {FormatProfile(tier.GetString() ?? "auto")}");
        text.AppendLine($"Arbeitsumfang: {root.GetProperty("workload").GetString()} (keine Zeitprognose)");
        text.AppendLine($"Subagenten: höchstens {root.GetProperty("max_concurrent_subagents").GetInt32()}");
        if (root.TryGetProperty("reasons", out JsonElement reasons))
            foreach (JsonElement reason in reasons.EnumerateArray()) text.AppendLine("• " + reason.GetString());
        if (root.TryGetProperty("agents", out JsonElement agents))
            foreach (JsonElement agent in agents.EnumerateArray())
                text.AppendLine($"Nebenrolle: {agent.GetProperty("model").GetString()} / " +
                    $"{agent.GetProperty("effort").GetString()} — {agent.GetProperty("objective").GetString()}");
        return text.ToString();
    }

    private static (string Path, bool Launcher) FindPython()
    {
        string[] paths = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string name in new[] { "py.exe", "python.exe", "python3.exe" })
            foreach (string directory in paths)
            {
                if (directory.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase)) continue;
                string path = Path.Combine(directory.Trim('"'), name);
                if (File.Exists(path)) return (Path.GetFullPath(path), name == "py.exe");
            }
        throw new FileNotFoundException("Python 3.10 oder neuer fehlt im PATH. Python installieren und Codex-Konten neu starten.");
    }
}
