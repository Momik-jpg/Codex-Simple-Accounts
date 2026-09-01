using System.Diagnostics;

namespace CodexAccountTray;

public sealed class TrayApplicationContext : ApplicationContext, IDisposable
{
    private static readonly Color MenuBackground = Color.FromArgb(36, 39, 42);
    private static readonly Color MenuForeground = Color.FromArgb(245, 245, 242);

    private readonly AccountStore _accountStore;
    private readonly AccountLoginService _loginService;
    private readonly ICodexProcessManager _processManager;
    private readonly LimitMonitor _monitor;
    private readonly SynchronizationContext _uiContext;
    private readonly Icon _icon;
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly AccountManagerForm _managerForm;
    private readonly HotkeyWindow _hotkey;
    private readonly AppSettingsStore _settingsStore;
    private readonly AgentLoopTaskStatusReader _taskStatusReader;
    private readonly ChatGptStartDetector _chatGptDetector;
    private readonly System.Windows.Forms.Timer _chatGptTimer;
    private string _lastTaskStatusKey = string.Empty;
    private bool _disposed;

    public TrayApplicationContext(
        AccountStore accountStore,
        AccountLoginService loginService,
        ICodexProcessManager processManager,
        LimitMonitor monitor,
        AppSettingsStore settingsStore,
        Func<bool>? isChatGptRunning = null,
        bool showOnStart = false,
        AgentLoopTaskStatusReader? taskStatusReader = null)
    {
        _accountStore = accountStore;
        _loginService = loginService;
        _processManager = processManager;
        _monitor = monitor;
        _settingsStore = settingsStore;
        _taskStatusReader = taskStatusReader ?? new AgentLoopTaskStatusReader();
        _chatGptDetector = new ChatGptStartDetector(isChatGptRunning ?? IsChatGptRunning);
        _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _icon = IconFactory.CreateTrayIcon();
        _menu = new ContextMenuStrip
        {
            BackColor = MenuBackground,
            ForeColor = MenuForeground,
            ShowImageMargin = false,
            Font = new Font("Segoe UI", 9.5f),
            Renderer = new DarkMenuRenderer()
        };
        _menu.Opening += (_, _) => RebuildMenu();

        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            Text = "Codex-Konten",
            ContextMenuStrip = _menu,
            Visible = true
        };
        _managerForm = new AccountManagerForm(
            accountStore,
            loginService,
            processManager,
            monitor,
            settingsStore,
            _icon);
        MainForm = _managerForm;
        _notifyIcon.DoubleClick += (_, _) => _managerForm.ShowWindow();
        _monitor.LimitsUpdated += OnLimitsUpdated;
        _monitor.Notice += OnNotice;
        _hotkey = new HotkeyWindow(() => _ = RefreshLimitsAsync());
        if (!_hotkey.IsRegistered)
        {
            ShowNotice("Strg+Alt+R ist bereits durch ein anderes Programm belegt.");
        }

        RebuildMenu();
        _monitor.Start();
        _chatGptTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _chatGptTimer.Tick += OnChatGptCheck;
        _chatGptTimer.Start();
        if (showOnStart)
        {
            _managerForm.ShowWindow();
        }
    }

    public new void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _monitor.LimitsUpdated -= OnLimitsUpdated;
        _monitor.Notice -= OnNotice;
        _chatGptTimer.Stop();
        _chatGptTimer.Tick -= OnChatGptCheck;
        _chatGptTimer.Dispose();
        _hotkey.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _managerForm.Dispose();
        _icon.Dispose();
        base.Dispose();
    }

    private void RebuildMenu()
    {
        _menu.Items.Clear();
        var heading = new ToolStripMenuItem("Codex-Konten")
        {
            Enabled = false,
            Font = new Font("Segoe UI Semibold", 10.5f)
        };
        _menu.Items.Add(heading);
        _menu.Items.Add(new ToolStripSeparator());

        foreach (int account in _accountStore.AccountNumbers)
        {
            _menu.Items.Add(CreateAccountItem(account));
        }
        var addAccount = new ToolStripMenuItem("Konto hinzufügen");
        addAccount.Click += async (_, _) => await LoginAsync(_accountStore.NextAccountNumber());
        _menu.Items.Add(addAccount);

        AgentLoopTaskStatus? taskStatus = _taskStatusReader.Read();
        _lastTaskStatusKey = TaskStatusKey(taskStatus);
        if (taskStatus is not null)
        {
            _menu.Items.Add(new ToolStripSeparator());
            var waitingTask = new ToolStripMenuItem(AgentLoopTaskStatusFormatter.Format(taskStatus))
            {
                Enabled = false,
                AutoSize = true,
                ToolTipText = $"AgentLoop-Task {taskStatus.TaskId} wartet. Fortsetzung erfolgt ausschliesslich in AgentLoop."
            };
            _menu.Items.Add(waitingTask);
        }

        _menu.Items.Add(new ToolStripSeparator());
        var refresh = new ToolStripMenuItem("Limits jetzt aktualisieren    Strg+Alt+R");
        refresh.Click += async (_, _) => await RefreshLimitsAsync();
        _menu.Items.Add(refresh);

        var autoSwap = new ToolStripMenuItem("Auto-Swap: 5 h 1 % · Woche 0 %")
        {
            Checked = _settingsStore.Load().AutoSwitchEnabled,
            CheckOnClick = true
        };
        autoSwap.CheckedChanged += (_, _) =>
        {
            AppSettings current = _settingsStore.Load();
            if (current.AutoSwitchEnabled != autoSwap.Checked)
            {
                _settingsStore.Save(current with { AutoSwitchEnabled = autoSwap.Checked });
                _managerForm.RefreshView();
            }
        };
        _menu.Items.Add(autoSwap);

        var manage = new ToolStripMenuItem("Konten verwalten");
        manage.Click += (_, _) => _managerForm.ShowWindow();
        _menu.Items.Add(manage);

        if (_processManager is ISmoothSwitchControl)
        {
            var emergency = new ToolStripMenuItem("Notfall AUS");
            emergency.ForeColor = Color.FromArgb(235, 105, 110);
            emergency.Click += async (_, _) => await EmergencyStopAsync();
            _menu.Items.Add(emergency);
        }

        var exit = new ToolStripMenuItem("Beenden");
        exit.Click += (_, _) => ExitApplication();
        _menu.Items.Add(exit);
    }

    private ToolStripMenuItem CreateAccountItem(int account)
    {
        bool loggedIn = _accountStore.IsLoggedIn(account);
        bool active = _processManager.ActiveAccount == account;
        bool pending = _processManager.PendingAccount == account;
        _monitor.Current.TryGetValue(account, out AccountLimits? limits);
        string status = pending
            ? "ChatGPT wird neu gestartet"
            : active ? "Aktiv" : loggedIn ? "Angemeldet" : "Nicht angemeldet";
        string text = $"{_accountStore.DisplayName(account)}  ·  {status}\n" +
                      LimitTextFormatter.Format(limits?.Primary, "5 h", TimeZoneInfo.Local) + "\n" +
                      LimitTextFormatter.Format(limits?.Secondary, "Woche", TimeZoneInfo.Local);
        var item = new ToolStripMenuItem(text)
        {
            AutoSize = true,
            Padding = new Padding(8, 6, 8, 6)
        };

        if (loggedIn)
        {
            var start = new ToolStripMenuItem("Codex-App öffnen")
            {
                Enabled = true
            };
            start.Click += async (_, _) => await StartAccountAsync(account);
            item.DropDownItems.Add(start);
        }

        var login = new ToolStripMenuItem(loggedIn ? "Neu anmelden" : "Anmelden");
        login.Click += async (_, _) => await LoginAsync(account);
        item.DropDownItems.Add(login);
        if (loggedIn)
        {
            var logout = new ToolStripMenuItem("Abmelden");
            logout.Click += async (_, _) => await LogoutAsync(account);
            item.DropDownItems.Add(logout);
        }
        return item;
    }

    private async Task RefreshLimitsAsync()
    {
        try
        {
            await _monitor.RefreshAsync(CancellationToken.None);
            _uiContext.Post(_ =>
            {
                RebuildMenu();
                _managerForm.RefreshView();
            }, null);
        }
        catch (Exception exception)
        {
            ShowNotice($"Limitabfrage fehlgeschlagen: {exception.Message}");
        }
    }

    private async Task StartAccountAsync(int account)
    {
        try
        {
            await _processManager.StartAsync(account, resumeLast: false, CancellationToken.None);
            RebuildMenu();
        }
        catch (Exception exception)
        {
            ShowNotice(exception.Message);
        }
    }

    private async Task LoginAsync(int account)
    {
        try
        {
            await _loginService.LoginAsync(account, CancellationToken.None);
            await RefreshLimitsAsync();
        }
        catch (Exception exception)
        {
            ShowNotice(exception.Message);
        }
    }

    private async Task LogoutAsync(int account)
    {
        if (MessageBox.Show(
                $"{_accountStore.DisplayName(account)} abmelden? Die lokal gespeicherten Anmeldedaten werden gelöscht.",
                "Konto abmelden",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            if (_processManager is ISmoothSwitchControl control)
            {
                await control.LogoutAsync(account, CancellationToken.None);
            }
            else
            {
                _accountStore.Remove(account);
            }
            RebuildMenu();
            _managerForm.RefreshView();
        }
        catch (Exception exception)
        {
            ShowNotice(exception.Message);
        }
    }

    private async Task EmergencyStopAsync()
    {
        if (_processManager is not ISmoothSwitchControl control ||
            MessageBox.Show(
                "Kontowechsel abbrechen und Auto-Swap ausschalten? ChatGPT bleibt offen.",
                "Notfall AUS",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }
        AppSettings settings = _settingsStore.Load();
        _settingsStore.Save(settings with { AutoSwitchEnabled = false });
        await control.EmergencyStopAsync(CancellationToken.None);
        RebuildMenu();
        _managerForm.RefreshView();
    }

    private void OnLimitsUpdated(object? sender, EventArgs eventArgs)
    {
        _uiContext.Post(_ => RebuildMenu(), null);
    }

    private void OnChatGptCheck(object? sender, EventArgs eventArgs)
    {
        AgentLoopTaskStatus? taskStatus = _taskStatusReader.Read();
        string taskStatusKey = TaskStatusKey(taskStatus);
        if (!string.Equals(taskStatusKey, _lastTaskStatusKey, StringComparison.Ordinal))
        {
            RebuildMenu();
        }
        if (_chatGptDetector.Observe())
        {
            if (_processManager is ISmoothSwitchControl control)
            {
                control.SynchronizeActiveAccount();
            }
            _managerForm.ShowWindow();
        }
    }

    private static string TaskStatusKey(AgentLoopTaskStatus? status) => status is null
        ? string.Empty
        : $"{status.TaskId}|{status.ReasonCode}|{status.LastSuccessfulRound}|{status.UpdatedAt:O}";

    private static bool IsChatGptRunning()
    {
        Process[] processes = Process.GetProcessesByName("ChatGPT");
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (Process process in processes)
            {
                process.Dispose();
            }
        }
    }

    private void OnNotice(object? sender, string message)
    {
        ShowNotice(message);
    }

    private void ShowNotice(string message)
    {
        _uiContext.Post(_ => _notifyIcon.ShowBalloonTip(5000, "Codex-Konten", message, ToolTipIcon.Info), null);
    }

    private void ExitApplication()
    {
        _notifyIcon.Visible = false;
        ExitThread();
    }

    private sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColorTable())
        {
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs eventArgs)
        {
            eventArgs.TextColor = eventArgs.Item.Enabled ? MenuForeground : Color.FromArgb(145, 150, 153);
            base.OnRenderItemText(eventArgs);
        }
    }

    private sealed class DarkColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => MenuBackground;
        public override Color MenuItemSelected => Color.FromArgb(48, 52, 56);
        public override Color MenuItemBorder => Color.FromArgb(70, 75, 79);
        public override Color ImageMarginGradientBegin => MenuBackground;
        public override Color ImageMarginGradientMiddle => MenuBackground;
        public override Color ImageMarginGradientEnd => MenuBackground;
        public override Color SeparatorDark => Color.FromArgb(60, 64, 68);
        public override Color SeparatorLight => Color.FromArgb(60, 64, 68);
    }
}
