using CodexAccountTray;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace CodexAccountTray.Tests;

public sealed class TaskRouterFormStateTests
{
    [Fact]
    public void PlanTabsRenderOnWindowsWithoutLaunchingCodex()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var form = new TaskRouterForm(
                    new TaskRouterLauncher(new CodexCommand("unused-test-command", [])),
                    () => Path.GetTempPath(), new TaskRouterActivity(), () => null);
                form.CreateControl();
                var tabs = Descendants(form).OfType<TabControl>().Single();
                Assert.Equal("Arbeitsplan", tabs.TabPages[0].Text);
                Assert.Equal("Auswahl, Bewertung & Laufstatus", tabs.TabPages[1].Text);
                var plan = Descendants(tabs.TabPages[0]).OfType<TextBox>().Single();
                Assert.True(plan.ReadOnly);
                plan.Text = TaskRouterLauncher.FormatWorkPlan("""{"status":"ready","goal":"Änderung sicher prüfen","plan_steps":[{"action":"Projekt untersuchen","verification":"Relevante Dateien bestätigen"},{"action":"Regression prüfen","verification":"Alle Tests bestehen"}]}""");
                tabs.SelectedIndex = 0;
                form.PerformLayout();
                Assert.True(tabs.Width > 500);
                Assert.True(tabs.Height > 100);
                using var bitmap = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(bitmap, form.ClientRectangle);
                string? artifacts = Environment.GetEnvironmentVariable("ROUTER_UI_ARTIFACTS");
                if (!string.IsNullOrWhiteSpace(artifacts))
                {
                    Directory.CreateDirectory(artifacts);
                    bitmap.Save(Path.Combine(artifacts, "router-plan.png"), ImageFormat.Png);
                }
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Native UI rendering timed out.");
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static IEnumerable<Control> Descendants(Control control)
    {
        foreach (Control child in control.Controls)
        {
            yield return child;
            foreach (Control descendant in Descendants(child)) yield return descendant;
        }
    }

    [Fact]
    public void MissingProjectKeepsStartActionsDisabled()
    {
        TaskRouterControlState state = TaskRouterFormState.Calculate(
            projectReady: false, taskReady: true, policyReady: true, imagesReady: true,
            busy: false, cancellationRequested: false);

        Assert.False(state.CanStart);
        Assert.True(state.InputsEnabled);
        Assert.Contains("Projektordner", state.StatusText);
    }

    [Fact]
    public void CompleteInputEnablesBothStartPaths()
    {
        TaskRouterControlState state = TaskRouterFormState.Calculate(
            projectReady: true, taskReady: true, policyReady: true, imagesReady: true,
            busy: false, cancellationRequested: false);

        Assert.True(state.CanStart);
        Assert.False(state.CancelEnabled);
        Assert.True(state.CloseEnabled);
    }

    [Fact]
    public void RunningTaskLocksInputsAndEnablesCancel()
    {
        TaskRouterControlState state = TaskRouterFormState.Calculate(
            projectReady: true, taskReady: true, policyReady: true, imagesReady: true,
            busy: true, cancellationRequested: false);

        Assert.False(state.CanStart);
        Assert.False(state.InputsEnabled);
        Assert.True(state.CancelEnabled);
        Assert.False(state.CloseEnabled);
    }

    [Fact]
    public void RepeatedCancellationIsPrevented()
    {
        TaskRouterControlState state = TaskRouterFormState.Calculate(
            projectReady: true, taskReady: true, policyReady: true, imagesReady: true,
            busy: true, cancellationRequested: true);

        Assert.False(state.CancelEnabled);
        Assert.Contains("Abbruch", state.CancelText);
    }

    [Theory]
    [InlineData(false, true, "Regeldatei")]
    [InlineData(true, false, "Referenzbild")]
    public void InvalidOptionalInputKeepsStartActionsDisabled(
        bool policyReady, bool imagesReady, string expectedStatus)
    {
        TaskRouterControlState state = TaskRouterFormState.Calculate(
            projectReady: true, taskReady: true, policyReady: policyReady, imagesReady: imagesReady,
            busy: false, cancellationRequested: false);

        Assert.False(state.CanStart);
        Assert.Contains(expectedStatus, state.StatusText);
    }
}
