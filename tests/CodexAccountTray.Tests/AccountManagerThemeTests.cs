using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;
using Xunit;

namespace CodexAccountTray.Tests;

public sealed class AccountManagerThemeTests
{
    [Theory]
    [InlineData(200, 68, 136)]
    [InlineData(200, 0, 0)]
    [InlineData(200, 100, 200)]
    [InlineData(200, -20, 0)]
    [InlineData(200, 120, 200)]
    [InlineData(200, null, 0)]
    public void RemainingQuotaHasCorrectFill(int width, int? remaining, int expected) =>
        Assert.Equal(expected, QuotaMeter.CalculateFillWidth(width, remaining));

    [Fact]
    public void ThemesRenderRealControlsAndPreserveSettingsAndAccountLocks()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            string root = Path.Combine(Path.GetTempPath(), $"CodexTheme-{Guid.NewGuid():N}");
            try
            {
                var paths = new AppPaths(root);
                var store = new AccountStore(paths);
                for (int account = 1; account <= 3; account++)
                    store.Save(account, JsonSerializer.SerializeToUtf8Bytes(new { tokens = new { account_id = $"fixture-{account}" } }));
                var settings = new AppSettingsStore(paths);
                settings.Save(new AppSettings(root, true) { RouterProfile = "balanced", RouterEffort = "medium" });
                string activeHome = paths.LoginHome(1);
                Directory.CreateDirectory(activeHome);
                File.WriteAllBytes(Path.Combine(activeHome, "auth.json"), store.Load(1));
                var process = new FixtureProcess(activeHome);
                using var monitor = new LimitMonitor(paths, store, new FixtureProtocol(), process, TimeSpan.FromMinutes(9), () => false);
                monitor.RefreshAsync(CancellationToken.None).GetAwaiter().GetResult();
                using var form = new AccountManagerForm(store,
                    new AccountLoginService(paths, store, new CodexCommand("unused-fixture", [])),
                    process, monitor, settings, SystemIcons.Application);
                form.Show();
                Application.DoEvents();
                foreach (string theme in new[] { "dark", "light" })
                {
                    Assert.Equal(theme, settings.Load().Theme);
                    Assert.Equal(theme == "dark" ? AccountPalette.Dark.Background : Color.White, form.BackColor);
                    var cards = Descendants(form).OfType<AccountCardPanel>().ToArray();
                    Assert.Equal(3, cards.Length);
                    foreach (AccountCardPanel card in cards)
                    {
                        Control[] children = card.Controls.Cast<Control>().ToArray();
                        Assert.All(children, child => Assert.True(card.ClientRectangle.Contains(child.Bounds), $"Clipped {child.GetType().Name}: {child.Bounds}"));
                        var meters = children.OfType<QuotaMeter>().ToArray();
                        Assert.Equal(2, meters.Length);
                        Assert.False(meters[0].Bounds.IntersectsWith(meters[1].Bounds));
                        Assert.All(meters, meter => Assert.False(meter.Bounds.IntersectsWith(children.OfType<RoundedButton>().First().Bounds)));
                    }
                    Assert.Contains("68 Prozent frei", Descendants(form).OfType<QuotaMeter>().Single(m => m.Name == "PrimaryQuota1").AccessibleName);
                    Assert.Contains("Keine Daten", Descendants(form).OfType<QuotaMeter>().Single(m => m.Name == "WeeklyQuota3").AccessibleName);
                    Assert.Equal(FormBorderStyle.None, form.FormBorderStyle);
                    var caption = form.Controls.Cast<Control>().Single(c => c.Name == "AccountWindowCaption");
                    var content = form.Controls.Cast<Control>().Single(c => c.Name == "AccountContent");
                    Assert.True(caption.Visible);
                    Assert.Equal(caption.Bottom, content.Top);
                    Assert.False(caption.Bounds.IntersectsWith(content.Bounds));
                    Assert.All(caption.Controls.OfType<RoundedButton>(), button =>
                    {
                        Assert.True(button.Visible);
                        Assert.True(caption.ClientRectangle.Contains(button.Bounds));
                    });
                    var list = Descendants(form).OfType<ThemedAccountList>().Single();
                    form.Size = form.MinimumSize;
                    Application.DoEvents();
                    list.ScrollTo(int.MaxValue);
                    Assert.True(list.ScrollOffset > 0);
                    Assert.True(cards.Max(c => c.Bottom) <= list.Viewport.Height);
                    list.ScrollTo(0);
                    Assert.Equal(0, cards.Min(c => c.Top));
                    Point wheelPoint = list.Viewport.PointToScreen(new Point(10, 10));
                    nint wheelCoordinates = unchecked((nint)((wheelPoint.X & 0xffff) | (wheelPoint.Y << 16)));
                    SendMessage(list.Viewport.Handle, 0x20A, (nint)(-120 << 16), wheelCoordinates);
                    Assert.True(list.ScrollOffset > 0);
                    list.ScrollTo(0);
                    SaveImage(form, $"accounts-{theme}.png");
                    form.Size = form.MinimumSize;
                    Application.DoEvents();
                    Assert.All(cards, card => Assert.All(card.Controls.Cast<Control>(), child => Assert.True(card.ClientRectangle.Contains(child.Bounds))));
                    form.ClientSize = new Size(1200, 800);
                    Descendants(form).OfType<RoundedButton>().Single(b => b.Name == "ThemeToggle").PerformClick();
                    Application.DoEvents();
                }
                Assert.True(settings.Load().AutoSwitchEnabled);
                Assert.Equal("balanced", settings.Load().RouterProfile);
                Point edge = form.PointToScreen(new Point(form.ClientSize.Width - 2, form.ClientSize.Height - 2));
                nint coordinates = unchecked((nint)((edge.X & 0xffff) | (edge.Y << 16)));
                Assert.Equal((nint)17, SendMessage(form.Handle, 0x84, 0, coordinates));
                var maximize = Descendants(form).OfType<RoundedButton>().Single(b => b.Name == "WindowMaximize");
                maximize.PerformClick();
                Assert.Equal(FormWindowState.Maximized, form.WindowState);
                maximize.PerformClick();
                Assert.Equal(FormWindowState.Normal, form.WindowState);
                Descendants(form).OfType<RoundedButton>().Single(b => b.Name == "WindowMinimize").PerformClick();
                Assert.Equal(FormWindowState.Minimized, form.WindowState);
                form.WindowState = FormWindowState.Normal;
                process.Pending = 2;
                form.RefreshView();
                Assert.All(Descendants(form).OfType<AccountCardPanel>().SelectMany(c => c.Controls.OfType<RoundedButton>()).Where(b => b.Text != "···"), b => Assert.False(b.Enabled));
                process.Pending = null;
                process.Running = true;
                form.RefreshView();
                Assert.All(Descendants(form).OfType<AccountCardPanel>().SelectMany(c => c.Controls.OfType<RoundedButton>()).Where(b => b.Text != "···"), b => Assert.False(b.Enabled));
                Descendants(form).OfType<RoundedButton>().Single(b => b.Name == "WindowClose").PerformClick();
                Assert.False(form.Visible);
                Assert.False(form.IsDisposed);
            }
            catch (Exception exception) { failure = exception; }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        });
        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Native account UI timed out.");
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (Control descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void SaveImage(Form form, string name)
    {
        string? artifacts = Environment.GetEnvironmentVariable("ROUTER_UI_ARTIFACTS");
        if (string.IsNullOrWhiteSpace(artifacts)) return;
        Directory.CreateDirectory(artifacts);
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        Assert.Equal(form.BackColor.ToArgb(), bitmap.GetPixel(2, 2).ToArgb());
        var list = Descendants(form).OfType<ThemedAccountList>().Single();
        Point rail = form.PointToClient(list.PointToScreen(new Point(list.Width - 1, list.Height / 2)));
        Assert.Equal(form.BackColor.ToArgb(), bitmap.GetPixel(rail.X, rail.Y).ToArgb());
        bitmap.Save(Path.Combine(artifacts, name), ImageFormat.Png);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);

    private sealed class FixtureProtocol : ICodexProtocolClient
    {
        public Task<AccountLimits> ReadLimitsAsync(string codexHome, CancellationToken cancellationToken)
        {
            int account = int.Parse(Path.GetFileName(codexHome));
            return Task.FromResult(new AccountLimits(new RateLimitWindow(account == 1 ? 32 : 15, 1790877600, 300),
                account == 3 ? null : new RateLimitWindow(55, 1790964000, 10080), DateTimeOffset.Now));
        }
    }

    private sealed class FixtureProcess(string activeHome) : ICodexProcessManager
    {
        public bool Running;
        public int? Pending;
        public bool IsRunning => Running;
        public int? ActiveAccount => 1;
        public int? PendingAccount => Pending;
        public int? LastAccount => 1;
        public string ActiveCodexHome => activeHome;
        public event EventHandler? ProcessExited { add { } remove { } }
        public Task StartAsync(int accountNumber, bool resumeLast, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
