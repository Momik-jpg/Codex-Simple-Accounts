namespace CodexAccountTray;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        using var singleInstance = new Mutex(true, "Local\\CodexAccountTray", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("Codex-Konten läuft bereits.", "Codex-Konten");
            return;
        }

        try
        {
            LegacyWebSocketCleanup.Remove();
            var paths = new AppPaths();
            paths.EnsureCreated();
            var accountStore = new AccountStore(paths);
            var settingsStore = new AppSettingsStore(paths);
            CodexCommand command = CodexLocator.Find();
            string codexHome = Path.Combine(WindowsUserProfile.ProfilePath(), ".codex");
            var desktopRuntime = new ProxyDesktopRuntime();
            using var processManager = new SmoothCodexProcessManager(
                accountStore,
                command,
                codexHome,
                desktopRuntime);
            var protocolClient = new CodexProtocolClient(command);
            var loginService = new AccountLoginService(
                paths,
                accountStore,
                command,
                showConsole: false);
            using var monitor = new LimitMonitor(
                paths,
                accountStore,
                protocolClient,
                processManager,
                TimeSpan.FromSeconds(30),
                () => settingsStore.Load().AutoSwitchEnabled);
            using var context = new TrayApplicationContext(
                accountStore,
                loginService,
                processManager,
                monitor,
                settingsStore,
                isChatGptRunning: () => desktopRuntime.IsRunning,
                showOnStart: LaunchMode.ShouldShow(args));
            Application.Run(context);
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Codex-Konten", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
