namespace CodexAccountTray;

public sealed class AccountManagerForm : Form
{
    private static readonly Color Background = Color.FromArgb(17, 20, 24);
    private static readonly Color Card = Color.FromArgb(27, 32, 38);
    private static readonly Color Muted = Color.FromArgb(151, 162, 174);
    private static readonly Color Accent = Color.FromArgb(64, 132, 214);
    private static readonly Color Success = Color.FromArgb(108, 201, 145);

    private readonly AccountStore _accountStore;
    private readonly AccountLoginService _loginService;
    private readonly ICodexProcessManager _processManager;
    private readonly LimitMonitor _monitor;
    private readonly AppSettingsStore _settingsStore;
    private readonly Dictionary<int, Label> _statusLabels = [];
    private readonly Dictionary<int, Label> _nameLabels = [];
    private readonly Dictionary<int, Label> _limitLabels = [];
    private readonly Dictionary<int, RoundedButton> _startButtons = [];
    private readonly Dictionary<int, RoundedButton> _loginButtons = [];
    private readonly Dictionary<int, RoundedButton> _logoutButtons = [];
    private readonly Dictionary<int, Panel> _activeIndicators = [];
    private readonly Dictionary<int, AccountCardPanel> _accountCards = [];
    private readonly CheckBox _autoSwitch = new();
    private readonly Panel _accountList = new();

    public AccountManagerForm(
        AccountStore accountStore,
        AccountLoginService loginService,
        ICodexProcessManager processManager,
        LimitMonitor monitor,
        AppSettingsStore settingsStore,
        Icon icon)
    {
        _accountStore = accountStore;
        _loginService = loginService;
        _processManager = processManager;
        _monitor = monitor;
        _settingsStore = settingsStore;

        Text = "Codex Konten";
        Icon = icon;
        ClientSize = new Size(1040, 790);
        MinimumSize = new Size(1060, 830);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Background;
        ForeColor = Color.White;
        Font = new Font("Segoe UI Variable Text", 10);

        BuildLayout();
        FormClosing += HideInsteadOfClose;
        _monitor.LimitsUpdated += (_, _) => SafeRefresh();
        _processManager.ProcessExited += (_, _) => SafeRefresh();
        RefreshView();
    }

    public void ShowWindow()
    {
        RefreshView();
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    protected override void OnHandleCreated(EventArgs eventArgs)
    {
        base.OnHandleCreated(eventArgs);
        NativeTheme.ApplyDarkTitleBar(Handle, Background);
    }

    public void RefreshView()
    {
        int[] accounts = _accountStore.AccountNumbers.ToArray();
        if (!_accountCards.Keys.Order().SequenceEqual(accounts))
        {
            RebuildAccountCards(accounts);
        }
        foreach (int account in accounts)
        {
            bool loggedIn = _accountStore.IsLoggedIn(account);
            bool active = _processManager.ActiveAccount == account;
            bool pending = _processManager.PendingAccount == account;
            _statusLabels[account].Text = pending
                ? "ChatGPT wird neu gestartet"
                : active ? "Aktiv" : loggedIn ? "Angemeldet" : "Nicht angemeldet";
            _statusLabels[account].ForeColor = active || pending ? Success : Muted;
            _activeIndicators[account].Visible = active || pending;
            _accountCards[account].IsActive = active || pending;
            _nameLabels[account].Text = _accountStore.DisplayName(account);
            _loginButtons[account].Text = loggedIn ? "Neu anmelden" : "Anmelden";
            _logoutButtons[account].Enabled = loggedIn && !_processManager.IsRunning;
            _startButtons[account].Text = pending ? "Bereit" : active ? "Öffnen" : "Wechseln";
            _startButtons[account].Enabled = loggedIn && !_processManager.IsRunning &&
                                             _processManager.PendingAccount is null;

            _monitor.Current.TryGetValue(account, out AccountLimits? limits);
            string stale = limits?.IsStale == true ? "  · veraltet" : string.Empty;
            _limitLabels[account].Text =
                LimitTextFormatter.Format(limits?.Primary, "5 h", TimeZoneInfo.Local) + Environment.NewLine +
                LimitTextFormatter.Format(limits?.Secondary, "Woche", TimeZoneInfo.Local) + stale;
        }

        _autoSwitch.Checked = _settingsStore.Load().AutoSwitchEnabled;
    }

    private void BuildLayout()
    {
        var title = new Label
        {
            Text = "Codex Konten",
            Font = new Font("Segoe UI Variable Display Semib", 24),
            AutoSize = true,
            Location = new Point(30, 18)
        };
        Controls.Add(title);

        var subtitle = new Label
        {
            Text = "Konten sicher verwalten und automatisch wechseln.",
            ForeColor = Muted,
            AutoSize = true,
            Location = new Point(33, 72)
        };
        Controls.Add(subtitle);

        var addAccount = CreateButton("Konto hinzufügen", new Point(850, 50), new Size(160, 42), true);
        addAccount.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        addAccount.Click += async (_, _) =>
        {
            int account = _accountStore.NextAccountNumber();
            await LoginAsync(account, addAccount);
        };
        Controls.Add(addAccount);

        _accountList.Location = new Point(28, 106);
        _accountList.Size = new Size(984, 500);
        _accountList.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        _accountList.AutoScroll = true;
        _accountList.BackColor = Background;
        _accountList.Resize += (_, _) => ResizeAccountCards();
        Controls.Add(_accountList);
        RebuildAccountCards(_accountStore.AccountNumbers);

        var refresh = CreateButton("Limits prüfen", new Point(30, 612), new Size(170, 44), true);
        refresh.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
        refresh.Click += async (_, _) => await RefreshLimitsAsync();
        Controls.Add(refresh);

        var hint = new Label
        {
            Text = "Alle 30 Sek.  ·  Strg + Alt + R",
            ForeColor = Muted,
            AutoSize = true,
            Location = new Point(220, 625),
            Anchor = AnchorStyles.Left | AnchorStyles.Bottom
        };
        Controls.Add(hint);

        _autoSwitch.Text = "Auto-Swap: 5 h 1 % · Woche 0 %";
        _autoSwitch.AutoSize = false;
        _autoSwitch.Size = new Size(310, 30);
        _autoSwitch.ForeColor = Color.White;
        _autoSwitch.Location = new Point(610, 620);
        _autoSwitch.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
        _autoSwitch.CheckedChanged += (_, _) =>
        {
            AppSettings current = _settingsStore.Load();
            if (current.AutoSwitchEnabled != _autoSwitch.Checked)
            {
                _settingsStore.Save(current with { AutoSwitchEnabled = _autoSwitch.Checked });
            }
        };
        Controls.Add(_autoSwitch);

        var emergency = CreateButton("Notfall AUS", new Point(840, 720), new Size(170, 44), false);
        emergency.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
        emergency.BackColor = Color.FromArgb(151, 55, 61);
        emergency.Click += async (_, _) => await EmergencyStopAsync();
        Controls.Add(emergency);

        var emergencyHint = new Label
        {
            Text = "Stoppt den Kontowechsel und Auto-Swap. Konten bleiben gespeichert.",
            ForeColor = Muted,
            AutoSize = true,
            Location = new Point(30, 733),
            Anchor = AnchorStyles.Left | AnchorStyles.Bottom
        };
        Controls.Add(emergencyHint);
    }

    private void RebuildAccountCards(IEnumerable<int> accounts)
    {
        _accountList.SuspendLayout();
        _accountList.Controls.Clear();
        _statusLabels.Clear();
        _nameLabels.Clear();
        _limitLabels.Clear();
        _startButtons.Clear();
        _loginButtons.Clear();
        _logoutButtons.Clear();
        _activeIndicators.Clear();
        _accountCards.Clear();
        int index = 0;
        foreach (int account in accounts.Order())
        {
            AccountCardPanel card = CreateAccountCard(account);
            card.Location = new Point(0, index++ * 98);
            _accountList.Controls.Add(card);
        }
        _accountList.AutoScrollMinSize = new Size(0, index * 98);
        ResizeAccountCards();
        _accountList.ResumeLayout();
    }

    private void ResizeAccountCards()
    {
        int width = Math.Max(600, _accountList.ClientSize.Width - 20);
        foreach (AccountCardPanel card in _accountCards.Values)
        {
            card.Width = width;
        }
    }

    private AccountCardPanel CreateAccountCard(int account)
    {
        var panel = new AccountCardPanel
        {
            Size = new Size(950, 86),
            BackColor = Card,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        _accountCards[account] = panel;
        var indicator = new Panel
        {
            Location = new Point(0, 0),
            Size = new Size(4, 86),
            BackColor = Accent,
            Visible = false
        };
        _activeIndicators[account] = indicator;
        panel.Controls.Add(indicator);

        var name = new Label
        {
            Text = _accountStore.DisplayName(account),
            Font = new Font("Segoe UI Variable Text Semibold", 11),
            AutoSize = false,
            Location = new Point(20, 13),
            Size = new Size(170, 28)
        };
        _nameLabels[account] = name;
        panel.Controls.Add(name);

        var status = new Label
        {
            AutoSize = false,
            Location = new Point(20, 46),
            Size = new Size(200, 25),
            ForeColor = Muted
        };
        _statusLabels[account] = status;
        panel.Controls.Add(status);

        var limits = new Label
        {
            AutoSize = false,
            Location = new Point(210, 13),
            Size = new Size(390, 58),
            ForeColor = Color.FromArgb(228, 230, 229),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        _limitLabels[account] = limits;
        panel.Controls.Add(limits);

        var login = CreateButton("Anmelden", new Point(620, 23), new Size(130, 40), false);
        login.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        login.Click += async (_, _) => await LoginAsync(account, login);
        _loginButtons[account] = login;
        panel.Controls.Add(login);

        var logout = CreateButton("Abmelden", new Point(760, 23), new Size(98, 40), false);
        logout.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        logout.Click += async (_, _) => await LogoutAsync(account);
        _logoutButtons[account] = logout;
        panel.Controls.Add(logout);

        var start = CreateButton("Öffnen", new Point(868, 23), new Size(96, 40), true);
        start.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        start.Click += async (_, _) => await StartAccountAsync(account);
        _startButtons[account] = start;
        panel.Controls.Add(start);
        void ApplyLayout()
        {
            AccountCardPositions positions = AccountCardLayout.Calculate(panel.ClientSize.Width);
            limits.Location = new Point(positions.LimitX, 13);
            limits.Width = positions.LimitWidth;
            login.Left = positions.LoginX;
            logout.Left = positions.LogoutX;
            start.Left = positions.StartX;
        }
        panel.Resize += (_, _) => ApplyLayout();
        ApplyLayout();
        return panel;
    }

    private static RoundedButton CreateButton(string text, Point location, Size size, bool accent)
    {
        return new RoundedButton
        {
            Text = text,
            Location = location,
            Size = size,
            BackColor = accent ? Accent : Color.FromArgb(43, 50, 58),
            ForeColor = Color.White,
            Font = new Font("Segoe UI Variable Text Semibold", 9.5f),
            CornerRadius = 11
        };
    }

    private async Task LoginAsync(int account, RoundedButton button)
    {
        button.Enabled = false;
        try
        {
            await _loginService.LoginAsync(account, CancellationToken.None);
            await RefreshLimitsAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Anmeldung", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            button.Enabled = true;
            RefreshView();
        }
    }

    private async Task StartAccountAsync(int account)
    {
        foreach (RoundedButton button in _startButtons.Values)
        {
            button.Enabled = false;
        }
        _startButtons[account].Text = "Wechsle…";
        try
        {
            await _processManager.StartAsync(account, resumeLast: false, CancellationToken.None);
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Konto wechseln", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            RefreshView();
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
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Konto abmelden", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            RefreshView();
        }
    }

    private async Task RefreshLimitsAsync()
    {
        await _monitor.RefreshAsync(CancellationToken.None);
        RefreshView();
    }

    private async Task EmergencyStopAsync()
    {
        if (_processManager is not ISmoothSwitchControl control)
        {
            return;
        }
        if (MessageBox.Show(
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
        RefreshView();
    }

    private void SafeRefresh()
    {
        if (IsHandleCreated)
        {
            BeginInvoke(RefreshView);
        }
    }

    private void HideInsteadOfClose(object? sender, FormClosingEventArgs eventArgs)
    {
        if (eventArgs.CloseReason == CloseReason.UserClosing)
        {
            eventArgs.Cancel = true;
            Hide();
        }
    }
}
