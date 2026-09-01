namespace CodexAccountTray;

public sealed record CodexCommand(string FileName, IReadOnlyList<string> PrefixArguments);

public static class CodexLocator
{
    public static CodexCommand Find()
    {
        string[] pathEntries = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        string? executable = pathEntries
            .Select(path => Path.Combine(path, "codex.exe"))
            .FirstOrDefault(File.Exists);
        if (executable is not null)
        {
            return new CodexCommand(executable, []);
        }

        string? commandFile = pathEntries
            .Select(path => Path.Combine(path, "codex.cmd"))
            .FirstOrDefault(File.Exists);
        string? node = pathEntries
            .Select(path => Path.Combine(path, "node.exe"))
            .FirstOrDefault(File.Exists);
        if (commandFile is not null && node is not null)
        {
            string script = Path.Combine(
                Path.GetDirectoryName(commandFile)!,
                "node_modules",
                "@openai",
                "codex",
                "bin",
                "codex.js");
            if (File.Exists(script))
            {
                return new CodexCommand(node, [script]);
            }
        }

        throw new FileNotFoundException("Codex CLI wurde nicht gefunden.");
    }
}
