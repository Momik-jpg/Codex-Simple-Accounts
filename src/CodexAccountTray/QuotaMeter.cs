using System.Drawing.Drawing2D;

namespace CodexAccountTray;

public sealed record AccountPalette(Color Background, Color Surface, Color Text, Color Muted,
    Color Border, Color Track, Color Accent, Color AccentText, Color Success, Color Danger)
{
    public static AccountPalette Dark { get; } = new(
        Color.FromArgb(8, 9, 11), Color.FromArgb(18, 20, 24), Color.FromArgb(240, 242, 245),
        Color.FromArgb(158, 166, 179), Color.FromArgb(43, 48, 57), Color.FromArgb(38, 43, 51),
        Color.FromArgb(193, 217, 250), Color.FromArgb(17, 31, 49), Color.FromArgb(115, 215, 174), Color.FromArgb(237, 146, 150));
    public static AccountPalette Light { get; } = new(
        Color.White, Color.FromArgb(247, 248, 250), Color.FromArgb(26, 31, 39),
        Color.FromArgb(92, 102, 117), Color.FromArgb(220, 225, 232), Color.FromArgb(228, 233, 239),
        Color.FromArgb(40, 91, 195), Color.White, Color.FromArgb(25, 125, 90), Color.FromArgb(174, 48, 60));
}

/// <summary>Displays remaining quota. An unavailable window never implies a full or empty account.</summary>
public sealed class QuotaMeter : Control
{
    private readonly string _title;
    private readonly bool _weekly;
    private RateLimitWindow? _window;
    private bool _stale;
    private AccountPalette _palette = AccountPalette.Dark;

    public QuotaMeter(string title, bool weekly)
    {
        _title = title;
        _weekly = weekly;
        AccessibleRole = AccessibleRole.ProgressBar;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    public void ApplyPalette(AccountPalette palette)
    {
        _palette = palette;
        BackColor = palette.Surface;
        ForeColor = palette.Text;
        Invalidate();
    }

    public void SetLimit(RateLimitWindow? window, bool stale)
    {
        _window = window;
        _stale = stale;
        AccessibleName = $"{_title}: {(window is null ? "Keine Daten" : $"{window.RemainingPercent} Prozent frei")}{(stale ? ", veraltet" : "")}";
        Invalidate();
    }

    public static int CalculateFillWidth(int width, int? remainingPercent) =>
        remainingPercent is null ? 0 : (int)Math.Round(Math.Max(0, width) * Math.Clamp(remainingPercent.Value, 0, 100) / 100d);

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.Clear(BackColor);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        int D(int value) => (int)(value * DeviceDpi / 96f);
        int width = Math.Max(0, Width - D(8));
        using var titleFont = new Font(Font, FontStyle.Bold);
        TextRenderer.DrawText(e.Graphics, _title, titleFont, new Rectangle(0, 0, width, D(25)), _palette.Text,
            TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        var bar = new Rectangle(0, D(31), width, D(7));
        using var track = new SolidBrush(_palette.Track);
        e.Graphics.FillRectangle(track, bar);
        int fillWidth = CalculateFillWidth(width, _window?.RemainingPercent);
        if (fillWidth > 0)
        {
            using var fill = new SolidBrush(_stale ? _palette.Muted : _weekly ? _palette.Success : _palette.Accent);
            e.Graphics.FillRectangle(fill, new Rectangle(bar.Location, new Size(fillWidth, bar.Height)));
        }
        string value = _window is null ? "Keine Daten" : $"{_window.RemainingPercent} % frei";
        if (_stale && _window is not null) value += " · veraltet";
        TextRenderer.DrawText(e.Graphics, value, Font, new Rectangle(0, D(45), width, D(24)), _palette.Text,
            TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        string reset = "Reset unbekannt";
        if (_window?.ResetsAt is long timestamp)
        {
            try { reset = $"Neu {DateTimeOffset.FromUnixTimeSeconds(timestamp).ToLocalTime():dd.MM. HH:mm}"; }
            catch (ArgumentOutOfRangeException) { }
        }
        if (_window is not null)
            TextRenderer.DrawText(e.Graphics, reset, Font, new Rectangle(0, D(71), width, D(22)), _palette.Muted,
                TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }
}
