using System.Diagnostics;

namespace CodexAccountTray;

public sealed class AccountLoginService
{
    private readonly AppPaths _paths;
    private readonly AccountStore _store;
    private readonly CodexCommand _command;
    private readonly IReadOnlyDictionary<string, string?> _extraEnvironment;
    private readonly bool _showConsole;

    public AccountLoginService(
        AppPaths paths,
        AccountStore store,
        CodexCommand command,
        IReadOnlyDictionary<string, string?>? extraEnvironment = null,
        bool showConsole = false)
    {
        _paths = paths;
        _store = store;
        _command = command;
        _extraEnvironment = extraEnvironment ?? new Dictionary<string, string?>();
        _showConsole = showConsole;
    }

    public async Task LoginAsync(int accountNumber, CancellationToken cancellationToken)
    {
        string loginHome = _paths.LoginHome(accountNumber);
        Directory.CreateDirectory(loginHome);
        string authFile = Path.Combine(loginHome, "auth.json");
        if (File.Exists(authFile))
        {
            File.Delete(authFile);
        }

        string? wrapper = null;
        ProcessStartInfo startInfo;
        if (_showConsole)
        {
            wrapper = CommandWrapper.Create(_paths, _command, loginHome, loginHome, ["login"]);
            startInfo = new ProcessStartInfo
            {
                FileName = wrapper,
                WorkingDirectory = loginHome,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Normal
            };
        }
        else
        {
            startInfo = new ProcessStartInfo
            {
                FileName = _command.FileName,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (string argument in _command.PrefixArguments.Append("login"))
            {
                startInfo.ArgumentList.Add(argument);
            }
            startInfo.Environment["CODEX_HOME"] = loginHome;
            foreach ((string key, string? value) in _extraEnvironment)
            {
                startInfo.Environment[key] = value;
            }
        }

        try
        {
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Codex-Anmeldung konnte nicht gestartet werden.");
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0 || !File.Exists(authFile))
            {
                throw new InvalidOperationException(
                    "Anmeldung nicht abgeschlossen. Schliesse die Anmeldung im Browser vollständig ab.");
            }

            _store.ImportAuthFile(accountNumber, authFile);
        }
        finally
        {
            if (Directory.Exists(loginHome))
            {
                Directory.Delete(loginHome, true);
            }
        }
    }
}
