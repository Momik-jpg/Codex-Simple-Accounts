using System.Diagnostics;

namespace CodexAccountTray;

public sealed record TaskRouterControlState(
    bool CanStart,
    bool InputsEnabled,
    bool CancelEnabled,
    bool CloseEnabled,
    string CancelText,
    string StatusText);

/// <summary>Pure UI-state calculation so busy and validation states stay consistent.</summary>
public static class TaskRouterFormState
{
    public static TaskRouterControlState Calculate(
        bool projectReady,
        bool taskReady,
        bool policyReady,
        bool imagesReady,
        bool busy,
        bool cancellationRequested)
    {
        if (busy)
        {
            return new TaskRouterControlState(
                false,
                false,
                !cancellationRequested,
                false,
                cancellationRequested ? "Abbruch läuft …" : "Abbrechen",
                cancellationRequested ? "Abbruch angefordert …" : "Auto-Aufgabe läuft …");
        }

        string status;
        if (!projectReady)
        {
            status = "Bitte einen vorhandenen Projektordner auswählen.";
        }
        else if (!taskReady)
        {
            status = "Bitte den Auftrag beschreiben.";
        }
        else if (!policyReady)
        {
            status = "Die optionale Regeldatei fehlt oder ist kein absoluter Pfad.";
        }
        else if (!imagesReady)
        {
            status = "Mindestens ein Referenzbild fehlt oder hat ein ungeeignetes Format.";
        }
        else
        {
            status = "Bereit: erst einstufen oder direkt automatisch starten.";
        }
        return new TaskRouterControlState(
            projectReady && taskReady && policyReady && imagesReady,
            true,
            false,
            true,
            "Abbrechen",
            status);
    }
}

/// <summary>Explicit task intake; never reads or changes a running desktop conversation.</summary>
public sealed class TaskRouterForm : Form
{
    private static readonly Color Background = Color.FromArgb(17, 20, 24);
    private static readonly Color Surface = Color.FromArgb(27, 32, 38);
    private static readonly Color ButtonSurface = Color.FromArgb(43, 50, 58);
    private static readonly Color Muted = Color.FromArgb(151, 162, 174);
    private static readonly Color Accent = Color.FromArgb(64, 132, 214);
    private static readonly Color Success = Color.FromArgb(108, 201, 145);
    private static readonly Color Warning = Color.FromArgb(235, 184, 92);

    private readonly TaskRouterLauncher _launcher;
    private readonly Func<string> _codexHome;
    private readonly TaskRouterActivity _activity;
    private readonly Func<string?> _startBlocker;
    private readonly Action<string>? _rememberProject;
    private readonly TextBox _project = new() { Dock = DockStyle.Fill };
    private readonly TextBox _policy = new() { Dock = DockStyle.Fill, PlaceholderText = "Optional: eigene routing_policy.json" };
    private readonly TextBox _task = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, MaxLength = 60000, AcceptsReturn = true };
    private readonly TextBox _report = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true };
    private readonly Label _imageLabel = new() { AutoSize = false, AutoEllipsis = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label _taskCount = new() { AutoSize = true, ForeColor = Muted };
    private readonly Label _writeHint = new() { AutoSize = false, Dock = DockStyle.Fill, ForeColor = Muted };
    private readonly Label _status = new() { AutoSize = false, Dock = DockStyle.Fill, ForeColor = Muted, TextAlign = ContentAlignment.MiddleLeft };
    private readonly NumericUpDown _agents = new() { Minimum = 0, Maximum = 2, Value = 2, Width = 55 };
    private readonly CheckBox _write = new() { Text = "Lokale Änderungen erlauben", AutoSize = true };
    private readonly Button _projectBrowse = new() { Text = "Auswählen …", Dock = DockStyle.Fill };
    private readonly Button _policyBrowse = new() { Text = "Auswählen …", Dock = DockStyle.Fill };
    private readonly Button _chooseImages = new() { Text = "Bilder auswählen …", AutoSize = true };
    private readonly Button _clearImages = new() { Text = "Leeren", AutoSize = true, Enabled = false };
    private readonly Button _plan = new() { Text = "Nur einstufen", AutoSize = true };
    private readonly Button _run = new() { Text = "Automatisch starten", AutoSize = true };
    private readonly Button _cancel = new() { Text = "Abbrechen", AutoSize = true, Enabled = false };
    private readonly Button _logs = new() { Text = "Laufordner öffnen", AutoSize = true, Enabled = false };
    private readonly Button _close = new() { Text = "Schliessen", AutoSize = true };
    private readonly ProgressBar _progress = new() { Dock = DockStyle.Top, Height = 5, Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 24, Visible = false };
    private readonly ToolTip _toolTip = new();
    private readonly System.Windows.Forms.Timer _elapsedTimer = new() { Interval = 500 };
    private readonly List<Control> _inputs = [];
    private readonly List<string> _images = [];
    private CancellationTokenSource? _cancellation;
    private string? _lastRun;
    private DateTime _startedAtUtc;
    private bool _executeMode;
    private bool _cancelRequested;

    public TaskRouterForm(
        TaskRouterLauncher launcher,
        Func<string> codexHome,
        TaskRouterActivity activity,
        Func<string?> startBlocker,
        string? initialProjectDirectory = null,
        Action<string>? rememberProject = null)
    {
        _launcher = launcher;
        _codexHome = codexHome;
        _activity = activity;
        _startBlocker = startBlocker;
        _rememberProject = rememberProject;
        if (!string.IsNullOrWhiteSpace(initialProjectDirectory) && Directory.Exists(initialProjectDirectory))
        {
            _project.Text = Path.GetFullPath(initialProjectDirectory);
        }

        Text = "Auto-Aufgabe · Codex Konten";
        ClientSize = new Size(940, 820);
        MinimumSize = new Size(840, 760);
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 10);
        BackColor = Background;
        ForeColor = Color.White;
        KeyPreview = true;

        BuildLayout();
        WireEvents();
        UpdateImageSummary();
        UpdateWriteHint();
        UpdateUi();
    }

    protected override void OnHandleCreated(EventArgs eventArgs)
    {
        base.OnHandleCreated(eventArgs);
        NativeTheme.ApplyDarkTitleBar(Handle, Background);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _elapsedTimer.Dispose();
            _toolTip.Dispose();
            _cancellation?.Dispose();
        }
        base.Dispose(disposing);
    }

    private void BuildLayout()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 11,
            Padding = new Padding(20)
        };
        foreach (RowStyle style in new[]
                 {
                     new RowStyle(SizeType.Absolute, 72),
                     new RowStyle(SizeType.Absolute, 44),
                     new RowStyle(SizeType.Absolute, 44),
                     new RowStyle(SizeType.Absolute, 28),
                     new RowStyle(SizeType.Percent, 44),
                     new RowStyle(SizeType.Absolute, 46),
                     new RowStyle(SizeType.Absolute, 66),
                     new RowStyle(SizeType.Absolute, 44),
                     new RowStyle(SizeType.Absolute, 28),
                     new RowStyle(SizeType.Percent, 56),
                     new RowStyle(SizeType.Absolute, 48)
                 })
        {
            layout.RowStyles.Add(style);
        }

        var header = new Panel { Dock = DockStyle.Fill };
        header.Controls.Add(new Label
        {
            Text = "Neue Auto-Aufgabe",
            Font = new Font("Segoe UI Semibold", 18),
            AutoSize = true,
            Location = new Point(0, 0)
        });
        header.Controls.Add(new Label
        {
            Text = "Modell und Denkaufwand werden eingestuft; feste Regeln treffen die endgültige Auswahl. Laufende Chats bleiben unverändert.",
            ForeColor = Muted,
            AutoSize = false,
            AutoEllipsis = true,
            Location = new Point(2, 40),
            Size = new Size(860, 25),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        });
        layout.Controls.Add(header, 0, 0);

        layout.Controls.Add(PathRow("Projekt", _project, _projectBrowse), 0, 1);
        layout.Controls.Add(PathRow("Eigene Regeln", _policy, _policyBrowse), 0, 2);

        var taskCaption = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        taskCaption.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        taskCaption.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        taskCaption.Controls.Add(new Label { Text = "Auftrag", AutoSize = true, Font = new Font("Segoe UI Semibold", 10) }, 0, 0);
        taskCaption.Controls.Add(_taskCount, 1, 0);
        layout.Controls.Add(taskCaption, 0, 3);
        _task.PlaceholderText = "Ziel, relevanter Kontext und überprüfbare Abschlusskriterien …";
        layout.Controls.Add(_task, 0, 4);

        var images = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
        images.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        images.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        images.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        images.Controls.Add(_chooseImages, 0, 0);
        images.Controls.Add(_clearImages, 1, 0);
        images.Controls.Add(_imageLabel, 2, 0);
        layout.Controls.Add(images, 0, 5);

        var optionPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        optionPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        optionPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var options = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        options.Controls.AddRange([
            new Label { Text = "Max. Nebenrollen:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) },
            _agents,
            new Label { Text = "0–2, nur bei passenden unabhängigen Teilaufgaben", AutoSize = true, ForeColor = Muted, Padding = new Padding(4, 6, 16, 0) },
            _write
        ]);
        optionPanel.Controls.Add(options, 0, 0);
        optionPanel.Controls.Add(_writeHint, 0, 1);
        layout.Controls.Add(optionPanel, 0, 6);

        var progressPanel = new Panel { Dock = DockStyle.Fill };
        progressPanel.Controls.Add(_status);
        progressPanel.Controls.Add(_progress);
        _status.Padding = new Padding(0, 8, 0, 0);
        layout.Controls.Add(progressPanel, 0, 7);

        layout.Controls.Add(new Label
        {
            Text = "Entscheidung und Laufstatus",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 10)
        }, 0, 8);
        _report.Text = "Noch keine Einstufung. «Nur einstufen» führt den Projektauftrag nicht aus.";
        layout.Controls.Add(_report, 0, 9);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 4, 0, 0)
        };
        buttons.Controls.AddRange([_close, _logs, _cancel, _run, _plan]);
        layout.Controls.Add(buttons, 0, 10);

        foreach (TextBox box in new[] { _project, _policy, _task, _report })
        {
            box.BackColor = Surface;
            box.ForeColor = Color.White;
            box.BorderStyle = BorderStyle.FixedSingle;
        }
        foreach (Button button in new[]
                 {
                     _projectBrowse, _policyBrowse, _chooseImages, _clearImages,
                     _plan, _run, _cancel, _logs, _close
                 })
        {
            button.BackColor = ButtonSurface;
            button.ForeColor = Color.White;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = Color.FromArgb(66, 73, 81);
        }
        _run.BackColor = Accent;
        _plan.Font = new Font(Font, FontStyle.Bold);
        _run.Font = new Font(Font, FontStyle.Bold);

        _inputs.AddRange([_project, _policy, _task, _projectBrowse, _policyBrowse, _chooseImages, _agents, _write]);
        _toolTip.SetToolTip(_plan, "Nur Entscheidung anzeigen · Strg+Enter");
        _toolTip.SetToolTip(_run, "Neu einstufen und Codex-Sitzung starten · Strg+Umschalt+Enter");
        _toolTip.SetToolTip(_cancel, "Nur den von diesem Dialog gestarteten Prozessbaum beenden · Esc");
        _toolTip.SetToolTip(_write, "Gilt nur für beauftragte lokale Änderungen; keine Veröffentlichung oder Zusammenführung.");
        _toolTip.SetToolTip(_agents, "Obergrenze, keine Pflicht zur Delegation.");
        Controls.Add(layout);
    }

    private void WireEvents()
    {
        _projectBrowse.Click += (_, _) => ChooseProject();
        _policyBrowse.Click += (_, _) => ChoosePolicy();
        _chooseImages.Click += (_, _) => ChooseImages();
        _clearImages.Click += (_, _) =>
        {
            _images.Clear();
            UpdateImageSummary();
            UpdateUi();
        };
        _project.TextChanged += (_, _) => UpdateUi();
        _policy.TextChanged += (_, _) => UpdateUi();
        _task.TextChanged += (_, _) => UpdateUi();
        _write.CheckedChanged += (_, _) => UpdateWriteHint();
        _plan.Click += async (_, _) => await LaunchAsync(false);
        _run.Click += async (_, _) => await LaunchAsync(true);
        _cancel.Click += (_, _) => RequestCancellation();
        _logs.Click += (_, _) => OpenLogs();
        _close.Click += (_, _) => Close();
        _elapsedTimer.Tick += (_, _) => UpdateUi();
        KeyDown += OnShortcutKeyDown;
        FormClosing += OnFormClosing;
        Shown += (_, _) => (_project.TextLength == 0 ? _project : _task).Focus();
    }

    private static Control PathRow(string label, TextBox field, Button button)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));
        row.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(0, 7, 0, 0) }, 0, 0);
        row.Controls.Add(field, 1, 0);
        row.Controls.Add(button, 2, 0);
        return row;
    }

    private void ChooseProject()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Projektordner auswählen",
            UseDescriptionForTitle = true,
            InitialDirectory = Directory.Exists(_project.Text) ? _project.Text : string.Empty
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _project.Text = dialog.SelectedPath;
            _task.Focus();
        }
    }

    private void ChoosePolicy()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Routing-Regeln (*.json)|*.json",
            CheckFileExists = true,
            Title = "Optionale Routing-Regeln auswählen"
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _policy.Text = dialog.FileName;
        }
    }

    private void ChooseImages()
    {
        using var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Filter = "Bilder|*.png;*.jpg;*.jpeg;*.webp",
            CheckFileExists = true,
            Title = "Bis zu sechs Referenzbilder auswählen"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }
        if (dialog.FileNames.Length > 6)
        {
            MessageBox.Show(this, "Bitte höchstens sechs Bilder auswählen.", "Referenzbilder",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        _images.Clear();
        _images.AddRange(dialog.FileNames);
        UpdateImageSummary();
        UpdateUi();
    }

    private async Task LaunchAsync(bool execute)
    {
        if (_cancellation is not null)
        {
            return;
        }
        var request = new TaskRouterRequest(
            _project.Text.Trim(),
            _task.Text,
            _images.ToArray(),
            (int)_agents.Value,
            _write.Checked,
            string.IsNullOrWhiteSpace(_policy.Text) ? null : _policy.Text.Trim());
        try
        {
            TaskRouterLauncher.Validate(request);
        }
        catch (Exception exception) when (exception is ArgumentException or PathTooLongException)
        {
            _report.Text = "Eingabe prüfen:\r\n" + exception.Message;
            _status.Text = "Noch nicht startbereit.";
            _status.ForeColor = Warning;
            FocusFirstInvalidInput();
            return;
        }

        string? blocker = _startBlocker();
        if (blocker is not null)
        {
            _report.Text = "Start nicht möglich:\r\n" + blocker;
            _status.Text = "Start blockiert.";
            _status.ForeColor = Warning;
            return;
        }

        IDisposable activity;
        try
        {
            activity = _activity.BeginRouterRun();
        }
        catch (InvalidOperationException exception)
        {
            _report.Text = "Start nicht möglich:\r\n" + exception.Message;
            _status.Text = "Start blockiert.";
            _status.ForeColor = Warning;
            return;
        }

        using (activity)
        {
            string? settingsWarning = null;
            try
            {
                _rememberProject?.Invoke(request.ProjectDirectory);
            }
            catch (Exception exception)
            {
                settingsWarning = "Projektordner konnte nicht gespeichert werden: " + exception.Message;
            }
            await RunRequestAsync(request, execute, settingsWarning);
        }
    }

    private async Task RunRequestAsync(TaskRouterRequest request, bool execute, string? settingsWarning)
    {
        _cancellation = new CancellationTokenSource();
        _executeMode = execute;
        _cancelRequested = false;
        _startedAtUtc = DateTime.UtcNow;
        _elapsedTimer.Start();
        UpdateUi();
        _report.Text = execute
            ? TaskRouterLauncher.FormatRequest(request, execute) +
              "\r\n\r\nDie Aufgabe wird neu eingestuft und in einer separaten Codex-Konsole gestartet. Freigaben dort beantworten."
            : TaskRouterLauncher.FormatRequest(request, execute) +
              "\r\n\r\nModellkatalog und kurze Einstufung werden abgefragt. Der Projektauftrag wird nicht ausgeführt.";
        try
        {
            TaskRouterResult result = await _launcher.RunAsync(request, _codexHome(), execute, _cancellation.Token);
            _lastRun = result.RunDirectory;
            _logs.Enabled = true;
            _report.Text = result.Report + "\r\n\r\n" + TaskRouterLauncher.FormatRequest(request, execute);
            if (!string.IsNullOrEmpty(settingsWarning))
            {
                _report.AppendText("\r\n\r\nHinweis: " + settingsWarning);
            }
            _status.Text = result.ExitCode == 0 ? "Abgeschlossen." : $"Beendet mit Exit-Code {result.ExitCode}.";
            _status.ForeColor = result.ExitCode == 0 ? Success : Warning;
        }
        catch (OperationCanceledException)
        {
            _report.Text = "Abgebrochen. Bereits ausgeführte Projektänderungen wurden nicht rückgängig gemacht.";
            _status.Text = "Abgebrochen.";
            _status.ForeColor = Warning;
        }
        catch (Exception exception)
        {
            _report.Text = "Fehler:\r\n" + exception.Message;
            _status.Text = "Fehlgeschlagen.";
            _status.ForeColor = Warning;
        }
        finally
        {
            _elapsedTimer.Stop();
            _cancellation.Dispose();
            _cancellation = null;
            _executeMode = false;
            _cancelRequested = false;
            UpdateUi(preserveStatus: true);
        }
    }

    private void RequestCancellation()
    {
        if (_cancellation is null || _cancelRequested)
        {
            return;
        }
        _cancelRequested = true;
        _cancellation.Cancel();
        UpdateUi();
    }

    private void UpdateUi(bool preserveStatus = false)
    {
        bool projectReady = false;
        try
        {
            string project = _project.Text.Trim();
            projectReady = Path.IsPathFullyQualified(project) && Directory.Exists(project);
        }
        catch (Exception) when (_project.Text.Length > 0)
        {
            projectReady = false;
        }
        bool taskReady = !string.IsNullOrWhiteSpace(_task.Text) && _task.TextLength <= _task.MaxLength;
        bool policyReady = false;
        bool imagesReady = false;
        try
        {
            string policy = _policy.Text.Trim();
            policyReady = policy.Length == 0 || Path.IsPathFullyQualified(policy) && File.Exists(policy);
            imagesReady = _images.All(image =>
                Path.IsPathFullyQualified(image) &&
                File.Exists(image) &&
                TaskRouterLauncher.IsSupportedImage(image));
        }
        catch (Exception) when (_policy.Text.Length > 0 || _images.Count > 0)
        {
            policyReady = false;
            imagesReady = false;
        }
        bool busy = _cancellation is not null;
        TaskRouterControlState state = TaskRouterFormState.Calculate(
            projectReady, taskReady, policyReady, imagesReady, busy, _cancelRequested);

        foreach (Control input in _inputs)
        {
            input.Enabled = state.InputsEnabled;
        }
        _clearImages.Enabled = state.InputsEnabled && _images.Count > 0;
        _plan.Enabled = state.CanStart;
        _run.Enabled = state.CanStart;
        _cancel.Enabled = state.CancelEnabled;
        _cancel.Text = state.CancelText;
        _close.Enabled = state.CloseEnabled;
        _progress.Visible = busy;
        _taskCount.Text = $"{_task.TextLength:N0} / {_task.MaxLength:N0}";

        if (busy)
        {
            TimeSpan elapsed = DateTime.UtcNow - _startedAtUtc;
            string operation = _executeMode ? "Auto-Aufgabe läuft" : "Einstufung läuft";
            _status.Text = _cancelRequested
                ? $"Abbruch angefordert · {elapsed:mm\\:ss}"
                : $"{operation} · {elapsed:mm\\:ss}";
            _status.ForeColor = _cancelRequested ? Warning : Success;
        }
        else if (!preserveStatus)
        {
            _status.Text = state.StatusText;
            _status.ForeColor = state.CanStart ? Success : Muted;
        }
    }

    private void UpdateImageSummary()
    {
        _imageLabel.Text = _images.Count == 0
            ? "Keine Referenzbilder"
            : $"{_images.Count} ausgewählt · " + string.Join(", ", _images.Select(Path.GetFileName));
        _imageLabel.ForeColor = _images.Count == 0 ? Muted : Color.White;
    }

    private void UpdateWriteHint()
    {
        _writeHint.Text = _write.Checked
            ? "Schreibzugriff EIN · nur beauftragte lokale Änderungen; der Router darf trotzdem read-only wählen. Keine Veröffentlichung, Löschung oder Zusammenführung."
            : "Schreibzugriff AUS · Einstufung und gestartete Aufgabe arbeiten ohne lokale Änderungsfreigabe.";
        _writeHint.ForeColor = _write.Checked ? Warning : Muted;
    }

    private void FocusFirstInvalidInput()
    {
        if (!Directory.Exists(_project.Text.Trim()))
        {
            _project.Focus();
        }
        else if (string.IsNullOrWhiteSpace(_task.Text))
        {
            _task.Focus();
        }
        else if (!string.IsNullOrWhiteSpace(_policy.Text) && !File.Exists(_policy.Text.Trim()))
        {
            _policy.Focus();
        }
    }

    private void OpenLogs()
    {
        if (_lastRun is null || !Directory.Exists(_lastRun))
        {
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo { FileName = _lastRun, UseShellExecute = true })?.Dispose();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Laufordner", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void OnShortcutKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.KeyCode == Keys.Escape)
        {
            if (_cancellation is null)
            {
                Close();
            }
            else
            {
                RequestCancellation();
            }
            eventArgs.SuppressKeyPress = true;
            return;
        }

        if (eventArgs.Control && eventArgs.Shift && eventArgs.KeyCode == Keys.Enter && _run.Enabled)
        {
            _ = LaunchAsync(true);
            eventArgs.SuppressKeyPress = true;
        }
        else if (eventArgs.Control && eventArgs.KeyCode == Keys.Enter && _plan.Enabled)
        {
            _ = LaunchAsync(false);
            eventArgs.SuppressKeyPress = true;
        }
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        if (_cancellation is null)
        {
            return;
        }
        eventArgs.Cancel = true;
        RequestCancellation();
    }
}
