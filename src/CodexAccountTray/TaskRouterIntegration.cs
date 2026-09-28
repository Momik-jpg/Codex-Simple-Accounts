namespace CodexAccountTray;

/// <summary>Optional entry point; account switching and credential storage remain unchanged.</summary>
public static class TaskRouterIntegration
{
    public static Action<Form> CreateOpenHandler(
        CodexCommand codex,
        ICodexProcessManager processManager,
        AppSettingsStore settingsStore,
        TaskRouterActivity taskRouterActivity)
    {
        TaskRouterForm? openForm = null;
        return owner =>
        {
            if (openForm is { IsDisposed: false })
            {
                if (openForm.WindowState == FormWindowState.Minimized)
                {
                    openForm.WindowState = FormWindowState.Normal;
                }
                openForm.Activate();
                return;
            }

            AppSettings settings = settingsStore.Load();
            using var form = new TaskRouterForm(
                new TaskRouterLauncher(codex),
                () => processManager.ActiveCodexHome,
                taskRouterActivity,
                () => settingsStore.Load().AutoSwitchEnabled
                    ? "Auto-Swap vor einem Router-Lauf ausschalten."
                    : taskRouterActivity.IsActive
                        ? "Eine Auto-Aufgabe läuft bereits."
                        : processManager.IsRunning || processManager.PendingAccount is not null
                            ? "Der Kontowechsel läuft noch. Bitte danach die Aufgabe starten."
                            : null,
                settings.ProjectFolder,
                projectDirectory =>
                {
                    AppSettings current = settingsStore.Load();
                    if (!string.Equals(current.ProjectFolder, projectDirectory,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        settingsStore.Save(current with { ProjectFolder = projectDirectory });
                    }
                });
            openForm = form;
            try
            {
                form.ShowDialog(owner);
            }
            finally
            {
                openForm = null;
            }
        };
    }

    public static void Attach(Form manager, CodexCommand codex,
        ICodexProcessManager processManager, AppSettingsStore settingsStore,
        TaskRouterActivity taskRouterActivity)
    {
        Attach(manager, CreateOpenHandler(codex, processManager, settingsStore, taskRouterActivity));
    }

    public static void Attach(Form manager, Action<Form> openTaskRouter)
    {
        var button = new RoundedButton
        {
            Name = "AutoTaskRouterButton", Text = "Auto-Aufgabe …",
            Location = new Point(30, manager.ClientSize.Height - 124), Size = new Size(210, 42),
            Anchor = AnchorStyles.Left | AnchorStyles.Bottom,
            BackColor = Color.FromArgb(64, 132, 214), ForeColor = Color.White,
            CornerRadius = 11,
            AccessibleName = "Auto-Aufgabe öffnen",
            AccessibleDescription = "Aufgabe automatisch einstufen und mit passendem Modell starten"
        };
        var label = new Label
        {
            Text = "Modell, Denkaufwand und Nebenrollen automatisch wählen.",
            Location = new Point(255, manager.ClientSize.Height - 116),
            Size = new Size(Math.Max(200, manager.ClientSize.Width - 280), 30),
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            ForeColor = Color.FromArgb(151, 162, 174), AutoEllipsis = true
        };
        button.Click += (_, _) => openTaskRouter(manager);
        manager.Controls.Add(button);
        manager.Controls.Add(label);
    }
}
