using System.Diagnostics;
using System.Security.Cryptography;

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
            status = "Bereit: Analyse & Plan ansehen oder direkt mit Codex starten.";
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
    private readonly Action<TaskRouterPreferences>? _rememberPreferences;
    private readonly TextBox _project = new() { Dock = DockStyle.Fill };
    private readonly TextBox _policy = new() { Dock = DockStyle.Fill, PlaceholderText = "Optional: eigene routing_policy.json" };
    private readonly TextBox _task = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, MaxLength = 60000, AcceptsReturn = true };
    private readonly TextBox _report = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true };
    private readonly TextBox _workPlan = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true, AccessibleName = "Vorläufiger Arbeitsplan" };
    private readonly TabControl _decisionTabs = new() { Dock = DockStyle.Fill };
    private readonly Label _imageLabel = new() { AutoSize = false, AutoEllipsis = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label _taskCount = new() { AutoSize = true, ForeColor = Muted };
    private readonly Label _writeHint = new() { AutoSize = false, Dock = DockStyle.Fill, ForeColor = Muted };
    private readonly Label _status = new() { AutoSize = false, AutoEllipsis = true, Dock = DockStyle.Fill, ForeColor = Muted, TextAlign = ContentAlignment.MiddleLeft };
    private readonly NumericUpDown _agents = new() { Minimum = 0, Maximum = 2, Value = 2, Width = 55 };
    private readonly ComboBox _profile = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190 };
    private readonly ComboBox _model = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210 };
    private readonly ComboBox _effort = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 185 };
    private readonly RoundedButton _refreshModels = new() { Text = "Modelle laden", Size = new Size(125, 34) };
    private readonly CheckBox _write = new() { Text = "Lokale Änderungen erlauben", AutoSize = true };
    private readonly RoundedButton _projectBrowse = new() { Text = "Auswählen …", Dock = DockStyle.Fill };
    private readonly RoundedButton _policyBrowse = new() { Text = "Auswählen …", Dock = DockStyle.Fill };
    private readonly RoundedButton _chooseImages = new() { Text = "Bilder hinzufügen", Size = new Size(150, 36) };
    private readonly RoundedButton _clearImages = new() { Text = "Leeren", Size = new Size(82, 36), Enabled = false };
    private readonly RoundedButton _resetOptions = new() { Text = "Auto zurücksetzen", Size = new Size(145, 34) };
    private readonly RoundedButton _plan = new() { Text = "Analyse & Plan", Size = new Size(174, 44) };
    private readonly RoundedButton _run = new() { Text = "Mit Codex starten", Size = new Size(205, 44) };
    private readonly RoundedButton _cancel = new() { Text = "Abbrechen", Size = new Size(125, 44), Enabled = false };
    private readonly RoundedButton _logs = new() { Text = "Laufordner", Size = new Size(125, 44), Enabled = false };
    private readonly RoundedButton _close = new() { Text = "Schliessen", Size = new Size(110, 44) };
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
    private string? _cachedAssessment;
    private string? _cachedAssessmentKey;
    private string? _cachedPlan;
    private string? _cachedDecisionKey;
    private IReadOnlyList<TaskRouterModel> _models = [];
    private readonly List<string> _effortCodes = ["auto"];
    private readonly CancellationTokenSource _modelCancellation = new();
    private bool _catalogReady;
    private bool _loadingModels;
    private string? _catalogProblem;
    private readonly string _initialEffort;
    private readonly string _initialModel;

    public TaskRouterForm(
        TaskRouterLauncher launcher,
        Func<string> codexHome,
        TaskRouterActivity activity,
        Func<string?> startBlocker,
        string? initialProjectDirectory = null,
        Action<string>? rememberProject = null,
        TaskRouterPreferences? initialPreferences = null,
        Action<TaskRouterPreferences>? rememberPreferences = null)
    {
        _launcher = launcher;
        _codexHome = codexHome;
        _activity = activity;
        _startBlocker = startBlocker;
        _rememberProject = rememberProject;
        _rememberPreferences = rememberPreferences;
        TaskRouterPreferences preferences = (initialPreferences ?? new TaskRouterPreferences()).Normalize();
        _initialEffort = preferences.Effort;
        _initialModel = preferences.Model;
        _profile.Items.AddRange(["Automatisch (empfohlen)", "Schnell", "Ausgewogen", "Gründlich"]);
        _model.Items.Add("Automatisch aus Live-Katalog");
        _model.SelectedIndex = 0;
        _effort.Items.Add("Automatisch (empfohlen)");
        _profile.SelectedIndex = Array.IndexOf(TaskRouterPreferences.Profiles, preferences.Profile);
        _effort.SelectedIndex = 0;
        _agents.Value = preferences.MaxSubagents;
        if (!string.IsNullOrWhiteSpace(initialProjectDirectory) && Directory.Exists(initialProjectDirectory))
        {
            _project.Text = Path.GetFullPath(initialProjectDirectory);
        }

        Text = "Auto-Aufgabe · Codex Konten";
        ClientSize = new Size(980, 870);
        MinimumSize = new Size(900, 790);
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 10);
        BackColor = Background;
        ForeColor = Color.White;
        KeyPreview = true;
        CancelButton = _close;

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
            _modelCancellation.Cancel();
            _modelCancellation.Dispose();
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
                     new RowStyle(SizeType.Absolute, 28),
                     new RowStyle(SizeType.Percent, 42),
                     new RowStyle(SizeType.Absolute, 46),
                     new RowStyle(SizeType.Absolute, 150),
                     new RowStyle(SizeType.Absolute, 44),
                     new RowStyle(SizeType.Absolute, 44),
                     new RowStyle(SizeType.Absolute, 28),
                     new RowStyle(SizeType.Percent, 58),
                     new RowStyle(SizeType.Absolute, 56)
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
            Text = "Auftrag bewerten, KI-Arbeitsplan prüfen und erst danach mit einem live angebotenen Modell starten.",
            ForeColor = Muted,
            AutoSize = false,
            AutoEllipsis = true,
            Location = new Point(2, 40),
            Size = new Size(860, 25),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        });
        layout.Controls.Add(header, 0, 0);

        layout.Controls.Add(PathRow("Projekt", _project, _projectBrowse), 0, 1);
        var taskCaption = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        taskCaption.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        taskCaption.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        taskCaption.Controls.Add(new Label { Text = "Auftrag", AutoSize = true, Font = new Font("Segoe UI Semibold", 10) }, 0, 0);
        taskCaption.Controls.Add(_taskCount, 1, 0);
        layout.Controls.Add(taskCaption, 0, 2);
        _task.PlaceholderText = "Ziel, relevanter Kontext und überprüfbare Abschlusskriterien …";
        layout.Controls.Add(_task, 0, 3);

        var images = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
        images.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        images.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        images.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        images.Controls.Add(_chooseImages, 0, 0);
        images.Controls.Add(_clearImages, 1, 0);
        images.Controls.Add(_imageLabel, 2, 0);
        layout.Controls.Add(images, 0, 4);

        var optionPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1 };
        optionPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        optionPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        optionPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        optionPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var routingOptions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        routingOptions.Controls.AddRange([
            OptionLabel("Qualitätsprofil"), _profile,
            OptionLabel("Modell"), _model, _refreshModels
        ]);
        var effortOptions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        effortOptions.Controls.AddRange([OptionLabel("Denkaufwand"), _effort, _resetOptions]);
        var executionOptions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        executionOptions.Controls.AddRange([
            OptionLabel("Max. Nebenrollen"), _agents,
            new Label { Text = "0–2, nur für unabhängige Teilaufgaben", AutoSize = true, ForeColor = Muted, Padding = new Padding(4, 7, 16, 0) },
            _write
        ]);
        optionPanel.Controls.Add(routingOptions, 0, 0);
        optionPanel.Controls.Add(effortOptions, 0, 1);
        optionPanel.Controls.Add(executionOptions, 0, 2);
        optionPanel.Controls.Add(_writeHint, 0, 3);
        layout.Controls.Add(optionPanel, 0, 5);

        layout.Controls.Add(PathRow("Eigene Regeln", _policy, _policyBrowse), 0, 6);

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
        _report.Text = "Noch keine Analyse. «Analyse & Plan» erstellt einen vorläufigen Arbeitsplan, führt den Projektauftrag aber nicht aus.";
        _workPlan.Text = "Noch kein Arbeitsplan. «Analyse & Plan» erstellt die vorgeschlagenen Schritte und ihre Prüfkriterien.";
        var planPage = new TabPage("Arbeitsplan") { BackColor = Surface };
        var decisionPage = new TabPage("Auswahl, Bewertung & Laufstatus") { BackColor = Surface };
        planPage.Controls.Add(_workPlan);
        decisionPage.Controls.Add(_report);
        _decisionTabs.TabPages.AddRange([planPage, decisionPage]);
        _decisionTabs.SelectedIndex = 1;
        layout.Controls.Add(_decisionTabs, 0, 9);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 4, 0, 0)
        };
        buttons.Controls.AddRange([_close, _logs, _cancel, _run, _plan]);
        layout.Controls.Add(buttons, 0, 10);

        foreach (TextBox box in new[] { _project, _policy, _task, _report, _workPlan })
        {
            box.BackColor = Surface;
            box.ForeColor = Color.White;
            box.BorderStyle = BorderStyle.FixedSingle;
        }
        foreach (RoundedButton button in new[]
                 {
                     _projectBrowse, _policyBrowse, _chooseImages, _clearImages, _refreshModels,
                     _resetOptions, _plan, _run, _cancel, _logs, _close
                 })
        {
            button.BackColor = ButtonSurface;
            button.ForeColor = Color.White;
            button.CornerRadius = 10;
        }
        _run.BackColor = Accent;
        _run.Font = new Font("Segoe UI Semibold", 10);
        _plan.Font = new Font("Segoe UI Semibold", 10);
        foreach (ComboBox combo in new[] { _profile, _model, _effort })
        {
            combo.BackColor = Surface;
            combo.ForeColor = Color.White;
            combo.FlatStyle = FlatStyle.Flat;
        }

        _inputs.AddRange([_project, _policy, _task, _projectBrowse, _policyBrowse, _chooseImages,
            _profile, _model, _effort, _refreshModels, _resetOptions, _agents, _write]);
        _toolTip.SetToolTip(_plan, "Auftrag bewerten und vorläufigen KI-Arbeitsplan anzeigen, ohne ihn auszuführen · Strg+Enter");
        _toolTip.SetToolTip(_run, "Neue Codex-Sitzung starten · Strg+Umschalt+Enter");
        _toolTip.SetToolTip(_cancel, "Nur den von diesem Dialog gestarteten Prozessbaum beenden · Esc");
        _toolTip.SetToolTip(_write, "Gilt nur für beauftragte lokale Änderungen; keine Veröffentlichung oder Zusammenführung.");
        _toolTip.SetToolTip(_agents, "Obergrenze, keine Pflicht zur Delegation.");
        _toolTip.SetToolTip(_profile, "Auto ist empfohlen. Manuelle Profile beeinflussen die Modellauswahl; Sicherheitsgrenzen bleiben erhalten.");
        _toolTip.SetToolTip(_model, "Nur Modelle aus dem aktuellen Live-Katalog. Die lokale Regeldatei bestimmt erlaubte Qualitätsstufen.");
        _toolTip.SetToolTip(_refreshModels, "Live-Katalog neu abfragen; vor jedem Start wird er nochmals geprüft.");
        _toolTip.SetToolTip(_effort, "Nur Denkstufen aus dem aktuellen Live-Katalog werden verwendet. Maximum/Ultra müssen ausdrücklich gewählt werden.");
        _toolTip.SetToolTip(_resetOptions, "Automatische Auswahl, zwei mögliche Nebenrollen und Schreibzugriff AUS.");
        _run.AccessibleName = "Aufgabe mit Codex starten";
        _plan.AccessibleName = "Aufgabe bewerten und vorläufigen KI-Arbeitsplan anzeigen";
        Controls.Add(layout);
    }

    private static Label OptionLabel(string text) => new()
    {
        Text = text + ":",
        AutoSize = true,
        Padding = new Padding(0, 7, 4, 0),
        Margin = new Padding(0, 0, 2, 0)
    };

    private void WireEvents()
    {
        _projectBrowse.Click += (_, _) => ChooseProject();
        _policyBrowse.Click += (_, _) => ChoosePolicy();
        _chooseImages.Click += (_, _) => ChooseImages();
        _clearImages.Click += (_, _) =>
        {
            _images.Clear();
            InvalidateCachedAssessment();
            UpdateImageSummary();
            UpdateUi();
        };
        _project.TextChanged += (_, _) => UpdateUi();
        _policy.TextChanged += (_, _) => UpdateUi();
        _task.TextChanged += (_, _) =>
        {
            InvalidateCachedAssessment();
            UpdateUi();
        };
        _profile.SelectedIndexChanged += (_, _) => UpdateUi();
        _model.SelectedIndexChanged += (_, _) =>
        {
            if (_catalogReady) _catalogProblem = null;
            UpdateEffortOptions();
            UpdateUi();
        };
        _effort.SelectedIndexChanged += (_, _) =>
        {
            if (_catalogReady) _catalogProblem = null;
            UpdateUi();
        };
        _refreshModels.Click += async (_, _) => await RefreshModelsAsync();
        _agents.ValueChanged += (_, _) => UpdateUi();
        _write.CheckedChanged += (_, _) => UpdateUi();
        _resetOptions.Click += (_, _) =>
        {
            _profile.SelectedIndex = 0;
            _model.SelectedIndex = 0;
            _effort.SelectedIndex = 0;
            _agents.Value = 2;
            _write.Checked = false;
            if (_catalogReady) _catalogProblem = null;
            UpdateUi();
        };
        _plan.Click += async (_, _) => await LaunchAsync(false);
        _run.Click += async (_, _) => await LaunchAsync(true);
        _cancel.Click += (_, _) => RequestCancellation();
        _logs.Click += (_, _) => OpenLogs();
        _close.Click += (_, _) => Close();
        _elapsedTimer.Tick += (_, _) => UpdateUi();
        KeyDown += OnShortcutKeyDown;
        FormClosing += OnFormClosing;
        Shown += (_, _) =>
        {
            (_project.TextLength == 0 ? _project : _task).Focus();
            _ = RefreshModelsAsync(_initialEffort, _initialModel);
        };
    }

    private async Task RefreshModelsAsync(string? preferredEffort = null, string? preferredModel = null)
    {
        if (_loadingModels || _cancellation is not null)
            return;
        _loadingModels = true;
        _catalogReady = false;
        _catalogProblem = null;
        UpdateUi();
        try
        {
            string selectedModel = preferredModel ?? SelectedModel();
            string selectedEffort = preferredEffort ?? SelectedEffort();
            IReadOnlyList<TaskRouterModel> models = await _launcher.LoadModelsAsync(
                _codexHome(), _modelCancellation.Token);
            if (IsDisposed) return;
            _models = models;
            _model.Items.Clear();
            _model.Items.Add("Automatisch aus Live-Katalog");
            foreach (TaskRouterModel model in models)
                _model.Items.Add(model.Id);
            int selectedIndex = selectedModel == "auto" ? 0 :
                models.ToList().FindIndex(model => model.Id == selectedModel) + 1;
            _model.SelectedIndex = Math.Max(0, selectedIndex);
            UpdateEffortOptions(selectedEffort);
            _catalogReady = true;
            if (selectedModel != "auto" && selectedIndex == 0)
                _catalogProblem = $"{selectedModel} ist nicht mehr verfügbar. Bitte Modell erneut wählen oder Auto zurücksetzen.";
            if (selectedEffort != "auto" && SelectedEffort() != selectedEffort)
                _catalogProblem = $"Denkstufe {selectedEffort} wird nicht mehr angeboten. Bitte Auswahl bestätigen oder Auto zurücksetzen.";
        }
        catch (OperationCanceledException) when (_modelCancellation.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            if (IsDisposed) return;
            _catalogProblem = "Modellkatalog nicht verfügbar: " + exception.Message + " Erneut laden.";
        }
        finally
        {
            if (!IsDisposed)
            {
                _loadingModels = false;
                if (_catalogProblem is not null)
                    _report.Text = _catalogProblem + "\r\n\r\nKatalog erneut laden und Auswahl prüfen. Der Start bleibt bis dahin gesperrt.";
                UpdateUi();
            }
        }
    }

    private void UpdateEffortOptions(string? preferred = null)
    {
        string requested = preferred ?? SelectedEffort();
        IEnumerable<string> available = SelectedModel() == "auto"
            ? _models.SelectMany(model => model.Efforts).Where(effort => effort is not ("max" or "ultra"))
            : _models.FirstOrDefault(model => model.Id == SelectedModel())?.Efforts ?? [];
        HashSet<string> supported = available.ToHashSet(StringComparer.Ordinal);
        _effortCodes.Clear();
        _effort.Items.Clear();
        _effortCodes.Add("auto");
        _effort.Items.Add("Automatisch (empfohlen)");
        foreach (string effort in TaskRouterPreferences.Efforts.Skip(1))
        {
            if (!supported.Contains(effort)) continue;
            _effortCodes.Add(effort);
            _effort.Items.Add(EffortLabel(effort));
        }
        int index = _effortCodes.IndexOf(requested);
        _effort.SelectedIndex = index >= 0 ? index : 0;
        if (index < 0 && requested != "auto" && _catalogReady)
            _catalogProblem = $"{requested} passt nicht zum Modell. Bitte Denkstufe erneut wählen oder Auto zurücksetzen.";
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
        InvalidateCachedAssessment();
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
            string.IsNullOrWhiteSpace(_policy.Text) ? null : _policy.Text.Trim(),
            SelectedProfile(),
            SelectedEffort(),
            Model: SelectedModel());
        if (execute && CanReuseAssessment() && !CanReuseDecision())
        {
            _report.Text = "Projekt, Regeln oder Auswahl haben sich seit der Vorschau geändert. Bitte zuerst «Auswahl prüfen» wählen; die vorhandene Einstufung wird dabei ohne neuen Klassifikationsaufruf verwendet.";
            _status.Text = "Auswahl erneut prüfen.";
            _status.ForeColor = Warning;
            return;
        }
        if (CanReuseAssessment() && (execute || !CanReuseDecision()))
        {
            request = request with { AssessmentFile = _cachedAssessment };
        }
        if (execute && CanReuseDecision())
            request = request with { ExpectedPlan = _cachedPlan };
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
            var settingsWarnings = new List<string>();
            try
            {
                _rememberProject?.Invoke(request.ProjectDirectory);
            }
            catch (Exception exception)
            {
                settingsWarnings.Add("Projektordner konnte nicht gespeichert werden: " + exception.Message);
            }
            try
            {
                _rememberPreferences?.Invoke(CurrentPreferences());
            }
            catch (Exception exception)
            {
                settingsWarnings.Add("Router-Einstellungen konnten nicht gespeichert werden: " + exception.Message);
            }
            await RunRequestAsync(request, execute,
                settingsWarnings.Count == 0 ? null : string.Join("; ", settingsWarnings));
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
        _decisionTabs.SelectedIndex = 1;
        _report.Text = execute
            ? TaskRouterLauncher.FormatRequest(request, execute) +
              (request.AssessmentFile is null
                  ? "\r\n\r\nDie Aufgabe wird neu eingestuft und in einer separaten Codex-Konsole gestartet. Freigaben dort beantworten."
                  : "\r\n\r\nDie vorhandene Einschätzung wird wiederverwendet, Live-Modelle und Regeln werden neu geprüft und danach startet die separate Codex-Konsole.")
            : TaskRouterLauncher.FormatRequest(request, execute) +
              "\r\n\r\nModellkatalog und kurze Einstufung werden abgefragt. Der Projektauftrag wird nicht ausgeführt.";
        try
        {
            TaskRouterResult result = await _launcher.RunAsync(request, _codexHome(), execute, _cancellation.Token);
            _lastRun = result.RunDirectory;
            _logs.Enabled = true;
            if (!execute && result.ExitCode == 0)
            {
                string assessment = Path.Combine(result.RunDirectory, "decision", "assessment.json");
                string plan = Path.Combine(result.RunDirectory, "decision", "plan.json");
                if (File.Exists(assessment) && File.Exists(plan))
                {
                    _cachedAssessment = assessment;
                    _cachedAssessmentKey = CurrentAssessmentKey();
                    _cachedPlan = plan;
                    _cachedDecisionKey = CurrentDecisionKey();
                }
            }
            string resultPlan = Path.Combine(result.RunDirectory, "decision", "plan.json");
            if (File.Exists(resultPlan))
            {
                _workPlan.Text = TaskRouterLauncher.FormatWorkPlan(await File.ReadAllTextAsync(resultPlan));
                if (!execute && result.ExitCode == 0) _decisionTabs.SelectedIndex = 0;
            }
            _report.Text = result.Report + "\r\n\r\n" + TaskRouterLauncher.FormatRequest(request, execute);
            if (!string.IsNullOrEmpty(settingsWarning))
            {
                _report.AppendText("\r\n\r\nHinweis: " + settingsWarning);
            }
            _status.Text = result.ExitCode == 0 ? (execute ? "Codex-Prozess beendet; Ergebnis im Codex-Bericht prüfen." : "Einstufung und Plan bereit; Auftrag noch nicht ausgeführt.") :
                result.ExitCode == 130 ? "Abgebrochen." : $"Beendet mit Exit-Code {result.ExitCode}.";
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
        bool imageModelSupported = _images.Count == 0 || SelectedModel() == "auto" ||
            _models.Any(model => model.Id == SelectedModel() && model.SupportsImages);
        bool catalogUsable = _catalogReady && !_loadingModels && _catalogProblem is null && imageModelSupported;
        _plan.Enabled = state.CanStart && catalogUsable;
        _run.Enabled = state.CanStart && catalogUsable &&
                       (!CanReuseAssessment() || CanReuseDecision());
        bool reuseAssessment = CanReuseAssessment();
        bool reuseDecision = CanReuseDecision();
        _plan.Text = reuseDecision ? "Plan erneuern" : reuseAssessment ? "Auswahl prüfen" : "Analyse & Plan";
        _run.Text = reuseDecision ? "Entscheidung starten" : "Mit Codex starten";
        _toolTip.SetToolTip(_run, reuseAssessment
            ? "Vorhandene Einschätzung verwenden; Live-Modelle und Regeln werden erneut geprüft · Strg+Umschalt+Enter"
            : "Neu einstufen und eine Codex-Sitzung starten · Strg+Umschalt+Enter");
        _cancel.Enabled = state.CancelEnabled;
        _cancel.Text = state.CancelText;
        _close.Enabled = state.CloseEnabled;
        _progress.Visible = busy;
        _taskCount.Text = $"{_task.TextLength:N0} / {_task.MaxLength:N0}";
        UpdateWriteHint();

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
            _status.Text = _loadingModels ? "Live-Modelle werden geladen …" :
                _catalogProblem ?? (!imageModelSupported ? "Dieses Modell bietet keine bestätigte Bildeingabe. Anderes Modell wählen." :
                !_catalogReady ? "Live-Modelle noch nicht geladen." :
                reuseAssessment && !reuseDecision ? "Auswahl geändert · erst Auswahl prüfen." : state.StatusText);
            _status.ForeColor = _catalogProblem is not null || !imageModelSupported ? Warning :
                state.CanStart && catalogUsable ? Success : Muted;
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
        string selection = SelectedProfile() == "auto" && SelectedEffort() == "auto"
            && SelectedModel() == "auto"
            ? "Auto optimiert Modell und Denkaufwand über Live-Katalog und Regeln."
            : $"Vorgabe: {ProfileLabel(SelectedProfile())}, Modell {SelectedModel()}, Denkaufwand {EffortLabel(SelectedEffort())}.";
        _writeHint.Text = _write.Checked
            ? selection + " Schreibzugriff EIN · nur beauftragte lokale Änderungen; keine Veröffentlichung oder Zusammenführung."
            : selection + " Schreibzugriff AUS.";
        _writeHint.ForeColor = _write.Checked ? Warning : Muted;
    }

    private string SelectedProfile() =>
        TaskRouterPreferences.Profiles[Math.Clamp(_profile.SelectedIndex, 0, TaskRouterPreferences.Profiles.Length - 1)];

    private string SelectedModel() => _model.SelectedIndex <= 0 ? "auto" :
        _model.SelectedItem?.ToString() ?? "auto";

    private string SelectedEffort() =>
        _effortCodes[Math.Clamp(_effort.SelectedIndex, 0, _effortCodes.Count - 1)];

    private TaskRouterPreferences CurrentPreferences() =>
        new(SelectedProfile(), SelectedEffort(), (int)_agents.Value, SelectedModel());

    private string CurrentAssessmentKey() =>
        _task.Text + "\u001f" + ActiveAuthKey() + "\u001f" + string.Join("\u001f", _images.Select(image =>
            Path.GetFullPath(image) + ":" + SafeModifiedAt(image)));

    private string ActiveAuthKey()
    {
        string home = _codexHome();
        string authFile = Path.Combine(home, "auth.json");
        try
        {
            using FileStream stream = File.OpenRead(authFile);
            return home + ":" + Convert.ToHexString(SHA256.HashData(stream));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return home + ":Anmeldung nicht lesbar";
        }
    }

    private string CurrentDecisionKey() => string.Join("\u001f", CurrentAssessmentKey(),
        _project.Text.Trim(), _policy.Text.Trim(),
        SafeModifiedAt(_policy.Text.Trim()),
        SelectedProfile(), SelectedModel(), SelectedEffort(), _agents.Value, _write.Checked);

    private static long SafeModifiedAt(string path)
    {
        try { return File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks : 0; }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private bool CanReuseAssessment() =>
        _cachedAssessment is not null &&
        File.Exists(_cachedAssessment) &&
        string.Equals(_cachedAssessmentKey, CurrentAssessmentKey(), StringComparison.Ordinal);

    private bool CanReuseDecision() => CanReuseAssessment() && _cachedPlan is not null &&
        File.Exists(_cachedPlan) &&
        string.Equals(_cachedDecisionKey, CurrentDecisionKey(), StringComparison.Ordinal);

    private void InvalidateCachedAssessment()
    {
        _workPlan.Text = "Auftrag geändert. Arbeitsplan erneut erstellen.";
        _cachedAssessment = null;
        _cachedAssessmentKey = null;
        _cachedPlan = null;
        _cachedDecisionKey = null;
    }

    private static string ProfileLabel(string profile) => profile switch
    {
        "fast" => "Schnell",
        "balanced" => "Ausgewogen",
        "deep" => "Gründlich",
        _ => "Automatisch"
    };

    private static string EffortLabel(string effort) => effort switch
    {
        "minimal" => "Minimal",
        "low" => "Niedrig",
        "medium" => "Mittel",
        "high" => "Hoch",
        "xhigh" => "Sehr hoch",
        "max" => "Maximum",
        "ultra" => "Ultra",
        _ => "Automatisch"
    };

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
