namespace CodexAccountTray;

public sealed class AccountManagerForm : ThemedAccountWindow
{
    private AccountPalette _palette = AccountPalette.Dark;
    private bool _lightTheme;
    private readonly Dictionary<int, QuotaMeter[]> _meters = [];
    private readonly Dictionary<int, Label> _avatars = [];
    private readonly List<Label> _mutedLabels = [];
    private RoundedButton _themeButton = null!;
    private bool _refreshing;

    private readonly AccountStore _accountStore;
    private readonly AccountLoginService _loginService;
    private readonly ICodexProcessManager _processManager;
    private readonly LimitMonitor _monitor;
    private readonly AppSettingsStore _settingsStore;
    private readonly TaskRouterActivity _taskRouterActivity;
    private readonly Dictionary<int, Label> _statusLabels = [];
    private readonly Dictionary<int, Label> _nameLabels = [];
    private readonly Dictionary<int, RoundedButton> _startButtons = [];
    private readonly Dictionary<int, ToolStripMenuItem> _loginButtons = [];
    private readonly Dictionary<int, ToolStripMenuItem> _logoutButtons = [];
    private readonly Dictionary<int, Panel> _activeIndicators = [];
    private readonly Dictionary<int, AccountCardPanel> _accountCards = [];
    private readonly AccountToggle _autoSwitch = new();
    private readonly ThemedAccountList _accountList = new();
    private readonly Label _activityStatus = new();
    private RoundedButton _addAccountButton = null!;
    private bool _operationInProgress;
    private string? _operationDescription;

    public AccountManagerForm(
        AccountStore accountStore,
        AccountLoginService loginService,
        ICodexProcessManager processManager,
        LimitMonitor monitor,
        AppSettingsStore settingsStore,
        Icon icon,
        TaskRouterActivity? taskRouterActivity = null)
    {
        _accountStore = accountStore;
        _loginService = loginService;
        _processManager = processManager;
        _monitor = monitor;
        _settingsStore = settingsStore;
        _taskRouterActivity = taskRouterActivity ?? new TaskRouterActivity();

        Text = "Codex Konten";
        Icon = icon;
        ClientSize = new Size(1200, 800);
        MinimumSize = new Size(1040, 680);
        MaximizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = _palette.Background;
        ForeColor = Color.White;
        Font = new Font("Segoe UI Variable Text", 10);

        _lightTheme = _settingsStore.Load().Theme == "light";
        _palette = _lightTheme ? AccountPalette.Light : AccountPalette.Dark;
        BuildLayout();
        ApplyTheme();
        FormClosing += HideInsteadOfClose;
        _monitor.LimitsUpdated += OnTaskRouterActivityChanged;
        _processManager.ProcessExited += OnTaskRouterActivityChanged;
        _taskRouterActivity.Changed += OnTaskRouterActivityChanged;
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
        NativeTheme.ApplyTitleBar(Handle, _palette.Background, _palette.Text, !_lightTheme);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _taskRouterActivity.Changed -= OnTaskRouterActivityChanged;
            _monitor.LimitsUpdated -= OnTaskRouterActivityChanged;
            _processManager.ProcessExited -= OnTaskRouterActivityChanged;
        }
        base.Dispose(disposing);
    }

    public void RefreshView()
    {
        _refreshing = true;
        try
        {
        bool routerActive = _taskRouterActivity.IsActive;
        bool accountActivity = _operationInProgress || _taskRouterActivity.IsBusy;
        bool autoSwitchEnabled = _settingsStore.Load().AutoSwitchEnabled;
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
            bool waiting = autoSwitchEnabled && _monitor.WaitingForCloseAccount == account;
            _statusLabels[account].Text = pending
                ? "Kontowechsel läuft …"
                : waiting ? "Wechsel nach Schliessen"
                : active ? "Aktiv" : loggedIn ? "Angemeldet" : "Nicht angemeldet";
            _statusLabels[account].ForeColor = active || pending || waiting ? _palette.Success : _palette.Muted;
            _activeIndicators[account].Visible = active || pending;
            _accountCards[account].IsActive = active || pending;
            _nameLabels[account].Text = _accountStore.DisplayName(account);
            _loginButtons[account].Text = loggedIn ? "Neu anmelden" : "Anmelden";
            _loginButtons[account].Enabled = !_processManager.IsRunning && !accountActivity && _processManager.PendingAccount is null;
            _logoutButtons[account].Enabled = loggedIn && !_processManager.IsRunning && !accountActivity && _processManager.PendingAccount is null;
            _startButtons[account].Text = pending ? "Wechselt …" : active ? "Codex öffnen" : loggedIn ? "Wechseln" : "Anmelden";
            _startButtons[account].Enabled = !_processManager.IsRunning &&
                                             _processManager.PendingAccount is null && !accountActivity;

            _monitor.Current.TryGetValue(account, out AccountLimits? limits);
            _meters[account][0].SetLimit(loggedIn ? limits?.Primary : null, limits?.IsStale == true);
            _meters[account][1].SetLimit(loggedIn ? limits?.Secondary : null, limits?.IsStale == true);
            if (limits?.IsStale == true) _statusLabels[account].Text += " · Limits veraltet";

        }

        _addAccountButton.Enabled = !_processManager.IsRunning && _processManager.PendingAccount is null && !accountActivity;
        _autoSwitch.Checked = autoSwitchEnabled;
        _autoSwitch.Enabled = !accountActivity;
        _autoSwitch.Text = routerActive
            ? "Auto-Swap gesperrt · Auto-Aufgabe läuft"
            : accountActivity
                ? "Auto-Swap pausiert · Kontoaktion läuft"
                : "Automatischer Wechsel nach Schliessen";

        int? pendingAccount = _processManager.PendingAccount;
        _activityStatus.Text = routerActive
            ? "Auto-Aufgabe läuft · Kontowechsel und Auto-Swap sind geschützt."
            : _operationDescription
              ?? (pendingAccount is not null
                  ? $"Wechsel zu {_accountStore.DisplayName(pendingAccount.Value)} läuft · ChatGPT wird neu gestartet."
                  : autoSwitchEnabled && _monitor.WaitingForCloseAccount is int waitingAccount
                    ? $"Limit erreicht · {_accountStore.DisplayName(waitingAccount)} startet nach dem Schliessen der Codex-App."
                  : string.Empty);
        _activityStatus.Visible = _activityStatus.Text.Length > 0;
        _activityStatus.ForeColor = routerActive ? _palette.Success : _palette.Muted;
        }
        finally { _refreshing = false; }
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(28), ColumnCount = 1, RowCount = 5,
            BackColor = _palette.Background
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 98));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        Controls.Add(root);
        var header = new Panel { Dock = DockStyle.Fill };
        header.Controls.Add(new Label { Text = "Deine Konten.", AutoSize = true,
            Font = new Font("Segoe UI", 27, FontStyle.Bold), Location = new Point(0, 0) });
        header.Controls.Add(MutedLabel("Einfach wechseln. Klarer Überblick.", new Point(2, 57), new Size(470, 24)));
        _themeButton = CreateButton("Hell", Point.Empty, new Size(100, 40), false);
        _themeButton.Name = "ThemeToggle";
        _themeButton.AccessibleName = "Zwischen schwarzem und weissem Theme wechseln";
        _themeButton.Click += (_, _) =>
        {
            _lightTheme = !_lightTheme;
            AppSettings current = _settingsStore.Load();
            _settingsStore.Save(current with { Theme = _lightTheme ? "light" : "dark" });
            ApplyTheme();
            RefreshView();
        };
        _addAccountButton = CreateButton("+ Konto hinzufügen", Point.Empty, new Size(180, 40), false);
        _addAccountButton.AccessibleName = "Neues Konto hinzufügen und anmelden";
        _addAccountButton.Click += async (_, _) => await LoginAsync(_accountStore.NextAccountNumber());
        header.Controls.Add(_themeButton);
        header.Controls.Add(_addAccountButton);
        header.Resize += (_, _) =>
        {
            _themeButton.Location = new Point(header.Width - _themeButton.Width, 18);
            _addAccountButton.Location = new Point(_themeButton.Left - _addAccountButton.Width - 12, 18);
        };
        root.Controls.Add(header, 0, 0);
        _activityStatus.Dock = DockStyle.Fill;
        _activityStatus.AutoEllipsis = true;
        _activityStatus.TextAlign = ContentAlignment.MiddleLeft;
        root.Controls.Add(_activityStatus, 0, 1);
        _accountList.Dock = DockStyle.Fill;
        _accountList.Viewport.Resize += (_, _) => ResizeAccountCards();
        _accountList.Resize += (_, _) => ResizeAccountCards();
        root.Controls.Add(_accountList, 0, 2);
        RebuildAccountCards(_accountStore.AccountNumbers);
        var automation = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 10, 0, 0) };
        _autoSwitch.Text = "Automatischer Wechsel nach Schliessen";
        _autoSwitch.AutoSize = true;
        _autoSwitch.Location = new Point(4, 15);
        _autoSwitch.AccessibleName = "Automatischen Kontowechsel aktivieren";
        _autoSwitch.CheckedChanged += (_, _) =>
        {
            if (_refreshing) return;
            AppSettings current = _settingsStore.Load();
            if (_taskRouterActivity.IsBusy) { _autoSwitch.Checked = current.AutoSwitchEnabled; return; }
            if (current.AutoSwitchEnabled != _autoSwitch.Checked)
                _settingsStore.Save(current with { AutoSwitchEnabled = _autoSwitch.Checked });
        };
        automation.Controls.Add(_autoSwitch);
        automation.Controls.Add(MutedLabel("5 Stunden: höchstens 1 % frei · Woche: 0 % frei", new Point(4, 44), new Size(500, 24)));
        var refresh = CreateButton("Limits prüfen", Point.Empty, new Size(145, 40), false);
        refresh.AccessibleName = "Kontingente jetzt prüfen";
        refresh.Click += async (_, _) => await RefreshLimitsAsync(refresh);
        automation.Controls.Add(refresh);
        automation.Resize += (_, _) => refresh.Location = new Point(automation.Width - refresh.Width, 20);
        root.Controls.Add(automation, 0, 3);
        var footer = new Panel { Dock = DockStyle.Fill };
        footer.Controls.Add(MutedLabel("Aufgaben direkt in Codex: gg / $gg", new Point(4, 5), new Size(600, 24)));
        footer.Controls.Add(MutedLabel("Plan und Ergebnisse bleiben im Chat. Einmalige Einrichtung erforderlich.", new Point(4, 30), new Size(650, 24)));
        var emergency = CreateButton("Notfall-Stopp", Point.Empty, new Size(155, 40), false);
        emergency.Name = "EmergencyStop";
        emergency.AccessibleName = "Kontowechsel stoppen und automatischen Wechsel ausschalten";
        emergency.Click += async (_, _) => await EmergencyStopAsync();
        footer.Controls.Add(emergency);
        footer.Resize += (_, _) => emergency.Location = new Point(footer.Width - emergency.Width, 12);
        root.Controls.Add(footer, 0, 4);
    }

    private Label MutedLabel(string text, Point location, Size size)
    {
        var label = new Label { Text = text, Location = location, Size = size, AutoEllipsis = true };
        _mutedLabels.Add(label);
        return label;
    }

    private void ApplyTheme()
    {
        _palette = _lightTheme ? AccountPalette.Light : AccountPalette.Dark;
        void PaintControls(Control parent)
        {
            parent.BackColor = _palette.Background;
            parent.ForeColor = _palette.Text;
            foreach (Control child in parent.Controls) PaintControls(child);
        }
        PaintControls(this);
        foreach (Label label in _mutedLabels) label.ForeColor = _palette.Muted;
        foreach (var pair in _accountCards)
        {
            AccountCardPanel card = pair.Value;
            card.BackColor = _palette.Surface;
            card.BorderColor = _palette.Border;
            card.ActiveBorderColor = _palette.Success;
            foreach (Control child in card.Controls) child.BackColor = _palette.Surface;
            _avatars[pair.Key].BackColor = _palette.Track;
            _activeIndicators[pair.Key].BackColor = _palette.Success;
            foreach (QuotaMeter meter in _meters[pair.Key]) meter.ApplyPalette(_palette);
        }
        foreach (RoundedButton button in Descendants(this).OfType<RoundedButton>())
        {
            bool primary = _startButtons.Values.Contains(button);
            button.BackColor = primary ? _palette.Accent : _palette.Surface;
            button.ForeColor = primary ? _palette.AccentText : _palette.Text;
            button.BorderColor = primary ? Color.Transparent : _palette.Border;
            if (button.Name == "EmergencyStop")
            {
                button.ForeColor = _palette.Danger;
                button.BorderColor = _palette.Danger;
            }
        }
        _autoSwitch.ApplyPalette(_palette);
        _accountList.ApplyPalette(_palette);
        ApplyWindowPalette(_palette);
        _themeButton.Text = _lightTheme ? "Dunkel" : "Hell";
        if (IsHandleCreated) NativeTheme.ApplyTitleBar(Handle, _palette.Background, _palette.Text, !_lightTheme);
        Invalidate(true);
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (Control descendant in Descendants(child)) yield return descendant;
        }
    }

    private void RebuildAccountCards(IEnumerable<int> accounts)
    {
        _accountList.SuspendLayout();
        foreach (Control previous in _accountList.Viewport.Controls.Cast<Control>().ToArray()) previous.Dispose();
        _accountList.Viewport.Controls.Clear();
        _accountList.ScrollTo(0);
        _meters.Clear();
        _avatars.Clear();
        _statusLabels.Clear();
        _nameLabels.Clear();
        _startButtons.Clear();
        _loginButtons.Clear();
        _logoutButtons.Clear();
        _activeIndicators.Clear();
        _accountCards.Clear();
        int index = 0;
        foreach (int account in accounts.Order())
        {
            AccountCardPanel card = CreateAccountCard(account);
            _accountList.AddCard(card, index++ * (int)(146 * DeviceDpi / 96f));
        }
        _accountList.SetContentHeight(index * (int)(146 * DeviceDpi / 96f));
        ResizeAccountCards();
        _accountList.ResumeLayout();
        if (_themeButton is not null) ApplyTheme();
    }

    private void ResizeAccountCards()
    {
        int width = Math.Max(600, _accountList.Viewport.ClientSize.Width - (int)(4 * DeviceDpi / 96f));
        foreach (AccountCardPanel card in _accountCards.Values)
        {
            card.Width = width;
        }
    }

    private AccountCardPanel CreateAccountCard(int account)
    {
        int D(int value) => (int)(value * DeviceDpi / 96f);
        var panel = new AccountCardPanel { Size = new Size(1100, D(132)), BackColor = _palette.Surface };
        _accountCards[account] = panel;
        var indicator = new Panel { BackColor = _palette.Success, Size = new Size(D(3), D(70)),
            Location = new Point(D(2), D(30)), Visible = false };
        _activeIndicators[account] = indicator;
        panel.Controls.Add(indicator);
        var avatar = new Label { Text = account.ToString(), TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(D(20), D(39)), Size = new Size(D(50), D(50)),
            Font = new Font("Segoe UI", 18, FontStyle.Bold), BackColor = _palette.Track };
        _avatars[account] = avatar;
        panel.Controls.Add(avatar);
        var name = new Label { Text = _accountStore.DisplayName(account), AutoEllipsis = true,
            Font = new Font("Segoe UI", 12, FontStyle.Bold), Location = new Point(D(88), D(36)),
            Size = new Size(D(185), D(28)) };
        _nameLabels[account] = name;
        panel.Controls.Add(name);
        var status = new Label { AutoEllipsis = true, Location = new Point(D(88), D(68)),
            Size = new Size(D(185), D(46)), ForeColor = _palette.Muted };
        _statusLabels[account] = status;
        panel.Controls.Add(status);
        var primary = new QuotaMeter("5 Stunden", false) { Name = $"PrimaryQuota{account}" };
        var weekly = new QuotaMeter("Woche", true) { Name = $"WeeklyQuota{account}" };
        _meters[account] = [primary, weekly];
        panel.Controls.Add(primary);
        panel.Controls.Add(weekly);
        var start = CreateButton("Wechseln", Point.Empty, new Size(D(130), D(42)), true);
        start.AccessibleName = $"{_accountStore.DisplayName(account)} in Codex öffnen oder dorthin wechseln";
        start.Click += async (_, _) =>
        {
            if (_accountStore.IsLoggedIn(account)) await StartAccountAsync(account);
            else await LoginAsync(account);
        };
        _startButtons[account] = start;
        panel.Controls.Add(start);
        var more = CreateButton("···", Point.Empty, new Size(D(36), D(42)), false);
        more.AccessibleName = $"Weitere Aktionen für {_accountStore.DisplayName(account)}";
        var menu = new ContextMenuStrip();
        var login = new ToolStripMenuItem("Neu anmelden");
        login.Click += async (_, _) => await LoginAsync(account);
        var logout = new ToolStripMenuItem("Abmelden");
        logout.Click += async (_, _) => await LogoutAsync(account);
        menu.Items.Add(login);
        menu.Items.Add(logout);
        _loginButtons[account] = login;
        _logoutButtons[account] = logout;
        more.Click += (_, _) => menu.Show(more, new Point(0, more.Height));
        panel.Disposed += (_, _) => menu.Dispose();
        panel.Controls.Add(more);
        void LayoutRow()
        {
            int identity = Math.Max(D(220), (int)(panel.Width * .26));
            name.Width = status.Width = Math.Max(D(110), identity - D(100));
            more.Location = new Point(panel.Width - D(52), D(44));
            start.Location = new Point(more.Left - start.Width - D(10), D(44));
            int quotaWidth = Math.Max(D(120), (start.Left - identity - D(36)) / 2);
            primary.Bounds = new Rectangle(identity, D(20), quotaWidth, D(96));
            weekly.Bounds = new Rectangle(identity + quotaWidth + D(16), D(20), quotaWidth, D(96));
        }
        panel.Resize += (_, _) => LayoutRow();
        LayoutRow();
        return panel;
    }

    private RoundedButton CreateButton(string text, Point location, Size size, bool accent) => new()
    {
        Text = text, Location = location, Size = size,
        BackColor = accent ? _palette.Accent : _palette.Surface,
        ForeColor = accent ? _palette.AccentText : _palette.Text,
        BorderColor = accent ? Color.Transparent : _palette.Border,
        Font = new Font("Segoe UI", 10, FontStyle.Regular), CornerRadius = 10
    };

    private async Task LoginAsync(int account)
    {
        BeginOperation($"{_accountStore.DisplayName(account)} wird angemeldet …");
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
            EndOperation();
        }
    }

    private async Task StartAccountAsync(int account)
    {
        BeginOperation($"Wechsel zu {_accountStore.DisplayName(account)} läuft …");
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
            EndOperation();
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

        BeginOperation($"{_accountStore.DisplayName(account)} wird abgemeldet …");
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
            EndOperation();
        }
    }

    private async Task RefreshLimitsAsync(RoundedButton? sourceButton = null)
    {
        string previousText = sourceButton?.Text ?? "Limits prüfen";
        if (sourceButton is not null)
        {
            sourceButton.Enabled = false;
            sourceButton.Text = "Prüfe …";
        }
        try
        {
            await _monitor.RefreshAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Limits prüfen", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            if (sourceButton is not null)
            {
                sourceButton.Text = previousText;
                sourceButton.Enabled = true;
            }
            RefreshView();
        }
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

    private void BeginOperation(string description)
    {
        _operationInProgress = true;
        _operationDescription = description;
        RefreshView();
    }

    private void EndOperation()
    {
        _operationInProgress = false;
        _operationDescription = null;
        RefreshView();
    }

    private void OnTaskRouterActivityChanged(object? sender, EventArgs eventArgs) => SafeRefresh();

    private void HideInsteadOfClose(object? sender, FormClosingEventArgs eventArgs)
    {
        if (eventArgs.CloseReason == CloseReason.UserClosing)
        {
            eventArgs.Cancel = true;
            Hide();
        }
    }
}


/// <summary>A keyboard-accessible checkbox drawn as an on/off switch.</summary>
internal sealed class AccountToggle : CheckBox
{
    private AccountPalette _palette = AccountPalette.Dark;

    public AccountToggle()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
        TabStop = true;
        AccessibleRole = AccessibleRole.CheckButton;
    }

    public void ApplyPalette(AccountPalette palette)
    {
        _palette = palette;
        Invalidate();
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        int gap = (int)(58 * DeviceDpi / 96f);
        Size text = TextRenderer.MeasureText(Text, Font);
        return new Size(text.Width + gap, Math.Max(text.Height + 4, (int)(28 * DeviceDpi / 96f)));
    }

    protected override void OnCheckedChanged(EventArgs e)
    {
        base.OnCheckedChanged(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        int D(int value) => (int)(value * DeviceDpi / 96f);
        int height = D(22), width = D(40);
        var track = new Rectangle(D(2), (Height - height) / 2, width, height);
        using var path = new System.Drawing.Drawing2D.GraphicsPath();
        path.AddArc(track.Left, track.Top, height, height, 90, 180);
        path.AddArc(track.Right - height, track.Top, height, height, 270, 180);
        path.CloseFigure();
        using var fill = new SolidBrush(Checked && Enabled ? _palette.Success : _palette.Track);
        e.Graphics.FillPath(fill, path);
        using var border = new Pen(Checked && Enabled ? _palette.Success : _palette.Muted);
        e.Graphics.DrawPath(border, path);
        int diameter = height - D(6);
        int left = Checked ? track.Right - diameter - D(3) : track.Left + D(3);
        using var knob = new SolidBrush(Enabled ? (Checked ? Color.White : _palette.Text) : _palette.Muted);
        e.Graphics.FillEllipse(knob, left, track.Top + D(3), diameter, diameter);
        var textBounds = new Rectangle(D(54), 0, Math.Max(0, Width - D(54)), Height);
        TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, Enabled ? _palette.Text : _palette.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        if (Focused && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -1, -1), _palette.Text, BackColor);
    }
}
