using System.Runtime.InteropServices;

namespace CodexAccountTray;

/// <summary>Theme-owned window chrome; resizing and dragging still use Windows messages.</summary>
public class ThemedAccountWindow : Form
{
    private readonly Panel _caption = new() { Name = "AccountWindowCaption", Dock = DockStyle.Top, Height = 38 };
    private readonly Label _captionText = new() { AutoSize = true, Location = new Point(15, 10) };
    private readonly RoundedButton _minimize;
    private readonly RoundedButton _maximize;
    private readonly RoundedButton _close;
    private AccountPalette _windowPalette = AccountPalette.Dark;

    public ThemedAccountWindow()
    {
        FormBorderStyle = FormBorderStyle.None;
        Padding = new Padding(1);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        _minimize = CaptionButton("−", "WindowMinimize", "Fenster minimieren");
        _maximize = CaptionButton("□", "WindowMaximize", "Fenster maximieren oder wiederherstellen");
        _close = CaptionButton("×", "WindowClose", "Fenster ausblenden");
        _minimize.Click += (_, _) => WindowState = FormWindowState.Minimized;
        _maximize.Click += (_, _) => ToggleMaximize();
        _close.Click += (_, _) => Close();
        _caption.Controls.AddRange([_captionText, _minimize, _maximize, _close]);
        Controls.Add(_caption);
        _caption.Resize += (_, _) =>
        {
            _close.Location = new Point(_caption.Width - _close.Width - 5, 4);
            _maximize.Location = new Point(_close.Left - _maximize.Width - 4, 4);
            _minimize.Location = new Point(_maximize.Left - _minimize.Width - 4, 4);
        };
        _caption.MouseDown += DragCaption;
        _captionText.MouseDown += DragCaption;
        _caption.MouseDoubleClick += (_, e) => { if (e.Button == MouseButtons.Left) ToggleMaximize(); };
        _captionText.MouseDoubleClick += (_, e) => { if (e.Button == MouseButtons.Left) ToggleMaximize(); };
    }

    private static RoundedButton CaptionButton(string text, string name, string accessibleName) => new()
    {
        Text = text, Name = name, AccessibleName = accessibleName, Size = new Size(38, 30),
        Font = new Font("Segoe UI", 12), CornerRadius = 6
    };

    protected void ApplyWindowPalette(AccountPalette palette)
    {
        _windowPalette = palette;
        _caption.BackColor = _captionText.BackColor = palette.Background;
        _captionText.ForeColor = palette.Muted;
        foreach (var button in new[] { _minimize, _maximize, _close })
        {
            button.BackColor = palette.Background;
            button.ForeColor = button == _close ? palette.Danger : palette.Text;
            button.BorderColor = Color.Transparent;
        }
        Invalidate();
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        if (_captionText is not null) _captionText.Text = Text;
    }

    private void ToggleMaximize()
    {
        MaximizedBounds = Screen.FromControl(this).WorkingArea;
        WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
        _maximize.Text = WindowState == FormWindowState.Maximized ? "❐" : "□";
    }

    private void DragCaption(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || e.Clicks > 1) return;
        ReleaseCapture();
        SendMessage(Handle, 0xA1, 2, 0); // WM_NCLBUTTONDOWN, HTCAPTION
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Width > 1 && Height > 1)
        {
            using var pen = new Pen(_windowPalette.Border);
            e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
    }

    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);
        if (message.Msg != 0x84 || WindowState != FormWindowState.Normal) return; // WM_NCHITTEST
        long packed = message.LParam.ToInt64();
        Point point = PointToClient(new Point(unchecked((short)packed), unchecked((short)(packed >> 16))));
        int border = Math.Max(6, (int)(6 * DeviceDpi / 96f));
        bool left = point.X < border, right = point.X >= ClientSize.Width - border;
        bool top = point.Y < border, bottom = point.Y >= ClientSize.Height - border;
        int hit = top ? left ? 13 : right ? 14 : 12
            : bottom ? left ? 16 : right ? 17 : 15
            : left ? 10 : right ? 11 : 1;
        if (hit != 1) message.Result = hit;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);
}
