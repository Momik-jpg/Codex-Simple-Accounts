using System.Text;

namespace CodexAccountTray;

public static class CommandWrapper
{
    public static string Create(
        AppPaths paths,
        CodexCommand command,
        string workingDirectory,
        string codexHome,
        IEnumerable<string> arguments)
    {
        string directory = Path.Combine(paths.Root, "Commands");
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, $"codex-{Guid.NewGuid():N}.cmd");
        var lines = new List<string>
        {
            "@echo off",
            $"set \"CODEX_HOME={codexHome}\"",
            $"cd /d \"{workingDirectory}\""
        };
        string commandLine = string.Join(" ",
            new[] { command.FileName }
                .Concat(command.PrefixArguments)
                .Concat(arguments)
                .Select(Quote));
        lines.Add(commandLine);
        lines.Add("exit /b %errorlevel%");
        File.WriteAllLines(file, lines, new UTF8Encoding(true));
        return file;
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
}
