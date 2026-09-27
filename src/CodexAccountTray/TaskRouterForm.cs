using System.Diagnostics;

namespace CodexAccountTray;

/// <summary>Explicit task intake; never reads or changes a running desktop conversation.</summary>
public sealed class TaskRouterForm : Form
{
    private readonly TaskRouterLauncher _launcher;
    private readonly Func<string> _codexHome;
    private readonly Func<string?> _startBlocker;
    private readonly TextBox _project = new() { Dock = DockStyle.Fill };
    private readonly TextBox _policy = new() { Dock = DockStyle.Fill, PlaceholderText = "Optional: eigene routing_policy.json" };
    private readonly TextBox _task = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, MaxLength = 60000, AcceptsReturn = true };
    private readonly TextBox _report = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true };
    private readonly Label _imageLabel = new() { Text = "Keine Referenzbilder", AutoSize = false, AutoEllipsis = true, Width = 520, Height = 30 };
    private readonly NumericUpDown _agents = new() { Minimum = 0, Maximum = 2, Value = 2, Width = 55 };
    private readonly CheckBox _write = new() { Text = "Beauftragte lokale Änderungen erlauben", AutoSize = true };
    private readonly Button _plan = new() { Text = "Nur einstufen", AutoSize = true };
    private readonly Button _run = new() { Text = "Automatisch starten", AutoSize = true };
    private readonly Button _cancel = new() { Text = "Abbrechen", AutoSize = true, Enabled = false };
    private readonly Button _logs = new() { Text = "Laufordner öffnen", AutoSize = true, Enabled = false };
    private readonly List<string> _images = [];
    private CancellationTokenSource? _cancellation;
    private string? _lastRun;

    public TaskRouterForm(TaskRouterLauncher launcher, Func<string> codexHome, Func<string?> startBlocker)
    {
        _launcher = launcher;
        _codexHome = codexHome;
        _startBlocker = startBlocker;
        Text = "Auto-Aufgabe · Codex Konten";
        ClientSize = new Size(920, 790);
        MinimumSize = new Size(820, 740);
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 10);
        BackColor = Color.FromArgb(17, 20, 24);
        ForeColor = Color.White;
        BuildLayout();
        _plan.Click += async (_, _) => await LaunchAsync(false);
        _run.Click += async (_, _) => await LaunchAsync(true);
        _cancel.Click += (_, _) => _cancellation?.Cancel();
        _logs.Click += (_, _) => OpenLogs();
        FormClosing += (_, e) =>
        {
            if (_cancellation is null) return;
            e.Cancel = true;
            _cancellation.Cancel();
            _report.Text = "Abbruch angefordert. Der gestartete Prozess wird beendet; danach das Fenster schliessen.";
        };
    }

    private void BuildLayout()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 9, Padding = new Padding(18) };
        foreach (RowStyle style in new[] { new RowStyle(SizeType.Absolute, 90), new RowStyle(SizeType.Absolute, 42),
                     new RowStyle(SizeType.Absolute, 42), new RowStyle(SizeType.Percent, 55),
                     new RowStyle(SizeType.Absolute, 42), new RowStyle(SizeType.Absolute, 42),
                     new RowStyle(SizeType.Absolute, 28), new RowStyle(SizeType.Percent, 45), new RowStyle(SizeType.Absolute, 44) })
            layout.RowStyles.Add(style);
        layout.Controls.Add(new Label { Dock = DockStyle.Fill, Text =
            "Auftrag eingeben → KI-Einstufung → feste Regeln → neue Codex-Sitzung.\n" +
            "Nutzt die aktuelle CLI-Anmeldung des Kontowechslers. Während des Laufs kein Konto wechseln.\n" +
            "Die Einstufung verbraucht Kontingent. Laufende Chats bleiben unverändert. Python 3.10+ erforderlich." }, 0, 0);
        layout.Controls.Add(PathRow("Projekt", _project, () =>
        {
            using var dialog = new FolderBrowserDialog { Description = "Projektordner auswählen", UseDescriptionForTitle = true };
            if (dialog.ShowDialog(this) == DialogResult.OK) _project.Text = dialog.SelectedPath;
        }), 0, 1);
        layout.Controls.Add(PathRow("Regeln", _policy, () =>
        {
            using var dialog = new OpenFileDialog { Filter = "Routing-Regeln (*.json)|*.json" };
            if (dialog.ShowDialog(this) == DialogResult.OK) _policy.Text = dialog.FileName;
        }), 0, 2);
        _task.PlaceholderText = "Ziel, relevanter Kontext und überprüfbare Abschlusskriterien …";
        layout.Controls.Add(_task, 0, 3);
        var images = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        var chooseImages = new Button { Text = "Bilder auswählen", AutoSize = true };
        chooseImages.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog { Multiselect = true, Filter = "Bilder|*.png;*.jpg;*.jpeg;*.webp" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            if (dialog.FileNames.Length > 6) { MessageBox.Show(this, "Höchstens sechs Bilder auswählen."); return; }
            _images.Clear(); _images.AddRange(dialog.FileNames);
            _imageLabel.Text = string.Join(", ", _images.Select(Path.GetFileName));
        };
        var clearImages = new Button { Text = "Leeren", AutoSize = true };
        clearImages.Click += (_, _) => { _images.Clear(); _imageLabel.Text = "Keine Referenzbilder"; };
        images.Controls.AddRange([chooseImages, clearImages, _imageLabel]);
        layout.Controls.Add(images, 0, 4);
        var options = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        options.Controls.AddRange([new Label { Text = "Max. Subagenten:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, _agents, _write]);
        layout.Controls.Add(options, 0, 5);
        layout.Controls.Add(new Label { Text = "Entscheidung / Status (Start stuft erneut ein; eine Vorschau ist keine Freigabe)", Dock = DockStyle.Fill }, 0, 6);
        layout.Controls.Add(_report, 0, 7);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        buttons.Controls.AddRange([_plan, _run, _cancel, _logs]);
        layout.Controls.Add(buttons, 0, 8);
        foreach (TextBox box in new[] { _project, _policy, _task, _report })
        { box.BackColor = Color.FromArgb(27, 32, 38); box.ForeColor = Color.White; box.BorderStyle = BorderStyle.FixedSingle; }
        foreach (Button button in new[] { _plan, _run, _cancel, _logs, chooseImages, clearImages })
        { button.BackColor = Color.FromArgb(43, 50, 58); button.ForeColor = Color.White; button.FlatStyle = FlatStyle.Flat; }
        Controls.Add(layout);
    }

    private static Control PathRow(string label, TextBox field, Action browse)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 65));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        var button = new Button { Text = "Auswählen", Dock = DockStyle.Fill, BackColor = Color.FromArgb(43, 50, 58), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        button.Click += (_, _) => browse();
        row.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, 0, 0);
        row.Controls.Add(field, 1, 0); row.Controls.Add(button, 2, 0);
        return row;
    }

    private async Task LaunchAsync(bool execute)
    {
        if (_cancellation is not null) return;
        string? blocker = _startBlocker();
        if (blocker is not null) { MessageBox.Show(this, blocker, "Aufgabenstart", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        _cancellation = new CancellationTokenSource();
        _plan.Enabled = _run.Enabled = false; _cancel.Enabled = true;
        _report.Text = execute ? "Einstufung und Ausführung im neuen Terminal. Freigaben dort beantworten."
                               : "Modellkatalog und kurze Einstufung werden abgefragt. Der Projektauftrag wird noch nicht ausgeführt.";
        try
        {
            var request = new TaskRouterRequest(_project.Text.Trim(), _task.Text, _images.ToArray(),
                (int)_agents.Value, _write.Checked, string.IsNullOrWhiteSpace(_policy.Text) ? null : _policy.Text.Trim());
            TaskRouterResult result = await _launcher.RunAsync(request, _codexHome(), execute, _cancellation.Token);
            _lastRun = result.RunDirectory; _logs.Enabled = true;
            _report.Text = result.Report;
        }
        catch (OperationCanceledException) { _report.Text = "Abgebrochen. Bereits ausgeführte Projektänderungen wurden nicht rückgängig gemacht."; }
        catch (Exception exception) { _report.Text = "Fehler: " + exception.Message; }
        finally
        {
            _cancellation.Dispose(); _cancellation = null;
            _plan.Enabled = _run.Enabled = true; _cancel.Enabled = false;
        }
    }

    private void OpenLogs()
    {
        if (_lastRun is null) return;
        try { Process.Start(new ProcessStartInfo { FileName = _lastRun, UseShellExecute = true })?.Dispose(); }
        catch (Exception exception) { MessageBox.Show(this, exception.Message, "Laufordner"); }
    }
}
