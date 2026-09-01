using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace CodexAccountTray;

public sealed class AccountCardPanel : Panel
{
    private int _cornerRadius = 14;
    private bool _isActive;
    private Color _borderColor = Color.FromArgb(39, 50, 61);
    private Color _activeBorderColor = Color.FromArgb(64, 132, 214);

    public AccountCardPanel()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
    }

    [DefaultValue(14)]
    public int CornerRadius
    {
        get => _cornerRadius;
        set
        {
            _cornerRadius = Math.Max(1, value);
            UpdateRegion();
            Invalidate();
        }
    }

    [DefaultValue(false)]
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value)
            {
                return;
            }
            _isActive = value;
            Invalidate();
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color BorderColor
    {
        get => _borderColor;
        set
        {
            _borderColor = value;
            Invalidate();
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color ActiveBorderColor
    {
        get => _activeBorderColor;
        set
        {
            _activeBorderColor = value;
            Invalidate();
        }
    }

    protected override void OnSizeChanged(EventArgs eventArgs)
    {
        base.OnSizeChanged(eventArgs);
        UpdateRegion();
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        if (Width < 2 || Height < 2)
        {
            return;
        }

        eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        int inset = IsActive ? 1 : 0;
        Rectangle bounds = Rectangle.Inflate(ClientRectangle, -inset - 1, -inset - 1);
        using GraphicsPath path = CreateRoundedPath(bounds, CornerRadius - inset);
        using var pen = new Pen(IsActive ? ActiveBorderColor : BorderColor, IsActive ? 2f : 1f);
        eventArgs.Graphics.DrawPath(pen, path);
    }

    private void UpdateRegion()
    {
        if (Width <= 0 || Height <= 0)
        {
            return;
        }
        using GraphicsPath path = CreateRoundedPath(new Rectangle(0, 0, Width, Height), CornerRadius);
        Region? oldRegion = Region;
        Region = new Region(path);
        oldRegion?.Dispose();
    }

    private static GraphicsPath CreateRoundedPath(Rectangle rectangle, int radius)
    {
        int safeRadius = Math.Max(1, radius);
        int diameter = Math.Min(safeRadius * 2, Math.Min(rectangle.Width, rectangle.Height));
        var path = new GraphicsPath();
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
