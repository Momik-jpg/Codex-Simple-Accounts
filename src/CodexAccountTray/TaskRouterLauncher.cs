using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

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
    string? AssessmentFile = null,
    string Model = "auto",
    string? ExpectedPlan = null);

public sealed record TaskRouterModel(string Id, IReadOnlyList<string> Efforts, bool SupportsImages);

public sealed record TaskRouterPreferences(
    string Profile = "auto",
    string Effort = "auto",
    int MaxSubagents = 2,
    string Model = "auto")
{
    public static readonly string[] Profiles = ["auto", "fast", "balanced", "deep"];
    public static readonly string[] Efforts = ["auto", "minimal", "low", "medium", "high", "xhigh", "max", "ultra"];

    public TaskRouterPreferences Normalize() => new(
        Profiles.Contains(Profile, StringComparer.Ordinal) ? Profile : "auto",
        Efforts.Contains(Effort, StringComparer.Ordinal) ? Effort : "auto",
        Math.Clamp(MaxSubagents, 0, 2),
        !string.IsNullOrWhiteSpace(Model) && Regex.IsMatch(Model, @"^[A-Za-z0-9][A-Za-z0-9._:/-]*$")
            ? Model : "auto");
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
        if (string.IsNullOrWhiteSpace(request.Model) ||
            !Regex.IsMatch(request.Model, @"^[A-Za-z0-9][A-Za-z0-9._:/-]*$"))
            throw new ArgumentException("Die Modell-ID ist ungültig. Bitte den Live-Katalog neu laden.");
        if (request.Images is null || request.Images.Count > 6)
            throw new ArgumentException("Höchstens sechs Referenzbilder auswählen.");
        foreach (string image in request.Images)
        {
            if (!File.Exists(image) || !Path.IsPathFullyQualified(image) ||
                !IsSupportedImage(image))
                throw new ArgumentException($"Referenzbild fehlt, ist grösser als 20 MB oder hat kein gültiges PNG-/JPEG-/WebP-Format: {image}");
        }
        if (!string.IsNullOrEmpty(request.PolicyFile) &&
            (!Path.IsPathFullyQualified(request.PolicyFile) || !File.Exists(request.PolicyFile)))
            throw new ArgumentException("Die gewählte Regeldatei existiert nicht.");
        if (!string.IsNullOrEmpty(request.AssessmentFile) &&
            (!Path.IsPathFullyQualified(request.AssessmentFile) || !File.Exists(request.AssessmentFile)))
            throw new ArgumentException("Die wiederzuverwendende Einstufung existiert nicht.");
        if (!string.IsNullOrEmpty(request.ExpectedPlan) &&
            (string.IsNullOrEmpty(request.AssessmentFile) ||
             !Path.IsPathFullyQualified(request.ExpectedPlan) || !File.Exists(request.ExpectedPlan)))
            throw new ArgumentException("Die angezeigte Entscheidung existiert nicht mehr. Bitte neu einstufen.");
    }

    public static bool IsSupportedImage(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (!ImageExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase)) return false;
        try
        {
            using var stream = File.OpenRead(path);
            if (stream.Length is < 12 or > 20 * 1024 * 1024) return false;
            Span<byte> header = stackalloc byte[12];
            if (stream.Read(header) != header.Length) return false;
            return extension switch
            {
                ".png" => header[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
                ".jpg" or ".jpeg" => header[0] == 255 && header[1] == 216 && header[2] == 255,
                ".webp" => header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8),
                _ => false
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

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
            $"Modell: {(request.Model == "auto" ? "Automatisch aus Live-Katalog" : request.Model + " (manuell)")}",
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
                     "--profile", request.Profile, "--model", request.Model, "--effort", request.Effort,
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
        if (execute && !string.IsNullOrEmpty(request.ExpectedPlan))
        {
            info.ArgumentList.Add("--expected-plan");
            info.ArgumentList.Add(request.ExpectedPlan);
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

    public async Task<IReadOnlyList<TaskRouterModel>> LoadModelsAsync(
        string codexHome, CancellationToken cancellationToken)
    {
        string script = Path.Combine(_routerDirectory, "router.py");
        if (!File.Exists(script))
            throw new FileNotFoundException("TaskRouter-Dateien fehlen. Die App vollständig neu bauen/installieren.", script);
        (string python, bool launcher) = FindPython();
        string codexPath = CodexPath();
        var info = new ProcessStartInfo
        {
            FileName = python,
            WorkingDirectory = _routerDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        if (launcher) info.ArgumentList.Add("-3");
        foreach (string argument in new[] { "-X", "utf8", script, "models", "--codex", codexPath })
            info.ArgumentList.Add(argument);
        info.Environment["CODEX_HOME"] = codexHome;
        info.Environment["PYTHONUTF8"] = "1";
        using var process = new Process { StartInfo = info };
        process.Start();
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            KillTreeIfRunning(process);
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(stdout, stderr);
            throw cancellationToken.IsCancellationRequested
                ? new OperationCanceledException(cancellationToken)
                : new TimeoutException("Der Live-Modellkatalog antwortet nicht. Bitte erneut laden.");
        }
        string result = await stdout;
        string error = await stderr;
        if (process.ExitCode != 0)
            throw new InvalidOperationException("Live-Modellkatalog fehlgeschlagen: " +
                (string.IsNullOrWhiteSpace(error) ? "Codex-Anmeldung und CLI prüfen." :
                    error[..Math.Min(error.Length, 600)]));
        return ParseModels(result);
    }

    public static IReadOnlyList<TaskRouterModel> ParseModels(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("data", out JsonElement data) ||
            data.ValueKind != JsonValueKind.Array)
            throw new FormatException("model/list liefert keinen gültigen Modellkatalog.");
        var models = new List<TaskRouterModel>();
        foreach (JsonElement item in data.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                item.TryGetProperty("hidden", out JsonElement hidden) && hidden.ValueKind == JsonValueKind.True ||
                !item.TryGetProperty("model", out JsonElement id) || id.ValueKind != JsonValueKind.String ||
                !item.TryGetProperty("supportedReasoningEfforts", out JsonElement options) ||
                options.ValueKind != JsonValueKind.Array)
                continue;
            string? name = id.GetString();
            if (string.IsNullOrWhiteSpace(name) ||
                !Regex.IsMatch(name, @"^[A-Za-z0-9][A-Za-z0-9._:/-]*$"))
                continue;
            string[] efforts = options.EnumerateArray()
                .Where(option => option.ValueKind == JsonValueKind.Object &&
                    option.TryGetProperty("reasoningEffort", out JsonElement effort) &&
                    effort.ValueKind == JsonValueKind.String)
                .Select(option => option.GetProperty("reasoningEffort").GetString()!)
                .Where(effort => TaskRouterPreferences.Efforts.Contains(effort, StringComparer.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (efforts.Length > 0)
            {
                bool supportsImages = item.TryGetProperty("inputModalities", out JsonElement modalities) &&
                    modalities.ValueKind == JsonValueKind.Array &&
                    modalities.EnumerateArray().Any(value => value.ValueKind == JsonValueKind.String &&
                        value.GetString() == "image");
                models.Add(new TaskRouterModel(name, efforts, supportsImages));
            }
        }
        if (models.Count == 0)
            throw new FormatException("Der Live-Katalog enthält keine nutzbaren Modelle und Denkstufen.");
        return models;
    }

    private string CodexPath() => _codex.PrefixArguments.Count == 0 ? _codex.FileName :
        _codex.PrefixArguments.Count == 1 && _codex.PrefixArguments[0].EndsWith(".js", StringComparison.OrdinalIgnoreCase)
            ? _codex.PrefixArguments[0]
            : throw new InvalidOperationException("Dieser Codex-Starter wird vom Router nicht unterstützt.");

    public async Task<TaskRouterResult> RunAsync(TaskRouterRequest request, string codexHome,
        bool execute, CancellationToken cancellationToken)
    {
        Validate(request);
        string script = Path.Combine(_routerDirectory, "desktop_entry.py");
        if (!File.Exists(script))
            throw new FileNotFoundException("TaskRouter-Dateien fehlen. Die App vollständig neu bauen/installieren.", script);
        (string python, bool launcher) = FindPython();
        string codexPath = CodexPath();
        // A local per-user run directory: task text never enters the command line or repository.
        string run = Path.Combine(_runsDirectory, $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(run);
        string taskFile = Path.Combine(run, "task.txt");
        await File.WriteAllTextAsync(taskFile, request.Task, new UTF8Encoding(false), cancellationToken);
        string outputDirectory = Path.Combine(run, "decision"); // The router creates this exclusively.
        ProcessStartInfo startInfo = BuildStartInfo(python, launcher, script,
            codexPath, codexHome, request, taskFile, outputDirectory, execute);
        using var process = new Process { StartInfo = startInfo };
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            process.Start();
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            await File.WriteAllTextAsync(Path.Combine(run, "launcher.log"),
                "Prozessstart fehlgeschlagen: " + exception.Message, Encoding.UTF8, CancellationToken.None);
            throw new InvalidOperationException($"Router-Prozess konnte nicht gestartet werden. Laufordner: {run}", exception);
        }
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
            KillTreeIfRunning(process);
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(stdout, stderr);
            await File.WriteAllTextAsync(Path.Combine(run, "launcher.log"),
                "Vom Benutzer abgebrochen. Prozessbaum beendet.", Encoding.UTF8, CancellationToken.None);
            return new TaskRouterResult(130, run,
                "Abgebrochen. Der gestartete Router-Prozessbaum wurde beendet. " +
                "Bereits ausgeführte Projektänderungen wurden nicht rückgängig gemacht.");
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
        {
            string blocked = "Blockiert: " + root.GetProperty("blocking_reason").GetString();
            if (root.TryGetProperty("plan_steps", out JsonElement blockedSteps))
                foreach (JsonElement step in blockedSteps.EnumerateArray())
                    blocked += "\r\nVorgeschlagener Klärungsschritt (ungeprüft): " + step.GetProperty("action").GetString();
            return blocked;
        }
        var text = new StringBuilder();
        if (root.TryGetProperty("goal", out JsonElement goal))
            text.AppendLine($"Ziel: {goal.GetString()}");
        if (root.TryGetProperty("task_type", out JsonElement taskType))
            text.AppendLine($"Auftragsart: {taskType.GetString()}");
        if (root.TryGetProperty("score", out JsonElement score))
            text.AppendLine($"Schwierigkeit: {score.GetInt32()} Heuristikpunkte · keine Zeitprognose");
        text.AppendLine($"Modell: {root.GetProperty("model").GetString()}");
        if (root.TryGetProperty("model_preference", out JsonElement preference))
            text.AppendLine($"Modellwahl: {(preference.GetString() == "auto" ? "automatisch" : "manuell")}");
        text.AppendLine($"Denkaufwand: {root.GetProperty("effort").GetString()}");
        if (root.TryGetProperty("effort_floor", out JsonElement floor))
            text.AppendLine($"Mindest-Denkstufe: {floor.GetString()}");
        if (root.TryGetProperty("requested_tier", out JsonElement tier))
            text.AppendLine($"Qualitätsprofil: {FormatProfile(tier.GetString() ?? "auto")}");
        text.AppendLine($"Arbeitsumfang: {root.GetProperty("workload").GetString()} (keine Zeitprognose)");
        if (root.TryGetProperty("confidence", out JsonElement confidence))
            text.AppendLine($"Einstufungssicherheit: {confidence.GetString()} (nur Eingangsprüfung)");
        text.AppendLine($"Subagenten: höchstens {root.GetProperty("max_concurrent_subagents").GetInt32()}");
        if (root.TryGetProperty("reasons", out JsonElement reasons))
            foreach (JsonElement reason in reasons.EnumerateArray()) text.AppendLine("• " + reason.GetString());
        if (root.TryGetProperty("agents", out JsonElement agents))
            foreach (JsonElement agent in agents.EnumerateArray())
                text.AppendLine($"Nebenrolle: {agent.GetProperty("model").GetString()} / " +
                    $"{agent.GetProperty("effort").GetString()} — {agent.GetProperty("objective").GetString()}");
        if (root.TryGetProperty("plan_steps", out JsonElement steps))
        {
            text.AppendLine();
            text.AppendLine("KI-Arbeitsplan · Entwurf vor Projektinspektion:");
            int number = 0;
            foreach (JsonElement step in steps.EnumerateArray())
                text.AppendLine($"{++number}. {step.GetProperty("action").GetString()}\r\n   Prüfen: {step.GetProperty("verification").GetString()}");
        }
        if (root.TryGetProperty("acceptance_checks", out JsonElement checks))
        {
            text.AppendLine();
            text.AppendLine("Abschlusskriterien · noch nicht geprüft:");
            foreach (JsonElement check in checks.EnumerateArray())
                text.AppendLine("• " + check.GetString());
        }
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
                if (File.Exists(path) && SupportsPython310(path, name == "py.exe"))
                    return (Path.GetFullPath(path), name == "py.exe");
            }
        throw new FileNotFoundException("Python 3.10 oder neuer fehlt im PATH. Python installieren und Codex-Konten neu starten.");
    }

    private static void KillTreeIfRunning(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (
            (exception is InvalidOperationException or System.ComponentModel.Win32Exception) && process.HasExited)
        {
            // The child exited between the HasExited check and the kill request.
        }
    }

    private static bool SupportsPython310(string path, bool launcher)
    {
        var info = new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        if (launcher) info.ArgumentList.Add("-3");
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add("import sys; sys.exit(0 if sys.version_info >= (3, 10) else 1)");
        try
        {
            using var process = Process.Start(info);
            if (process is null) return false;
            if (!process.WaitForExit(5000))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
                return false;
            }
            return process.ExitCode == 0;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return false;
        }
    }
}
