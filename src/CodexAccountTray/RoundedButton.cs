using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace CodexAccountTray;

public sealed class RoundedButton : Button
{
    private Color _normalColor;
    private int _cornerRadius = 10;
    private bool _pressed;

    public RoundedButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        Cursor = Cursors.Hand;
        TabStop = true;
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
    }

    [DefaultValue(10)]
    public int CornerRadius
    {
        get => _cornerRadius;
        set
        {
            _cornerRadius = Math.Max(1, value);
            Invalidate();
        }
    }

    protected override void OnBackColorChanged(EventArgs eventArgs)
    {
        base.OnBackColorChanged(eventArgs);
        _normalColor = BackColor;
        FlatAppearance.MouseOverBackColor = BackColor;
        FlatAppearance.MouseDownBackColor = BackColor;
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        eventArgs.Graphics.Clear(Parent?.BackColor ?? BackColor);
        Rectangle bounds = new(0, 0, Width - 1, Height - 1);
        using GraphicsPath path = CreateRoundedPath(bounds, CornerRadius);
        Color fill = !Enabled
            ? Color.FromArgb(45, 48, 51)
            : _normalColor;
        using var brush = new SolidBrush(fill);
        eventArgs.Graphics.FillPath(brush, path);
        Rectangle textBounds = _pressed
            ? new Rectangle(bounds.X, bounds.Y + 1, bounds.Width, bounds.Height)
            : bounds;
        TextRenderer.DrawText(
            eventArgs.Graphics,
            Text,
            Font,
            textBounds,
            Enabled ? ForeColor : Color.FromArgb(125, 130, 134),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

        if (Focused && ShowFocusCues && Enabled)
        {
            Rectangle focusBounds = Rectangle.Inflate(bounds, -3, -3);
            using GraphicsPath focusPath = CreateRoundedPath(focusBounds, Math.Max(2, CornerRadius - 3));
            using var focusPen = new Pen(Color.FromArgb(150, 190, 235));
            eventArgs.Graphics.DrawPath(focusPen, focusPath);
        }
    }

    protected override void OnMouseDown(MouseEventArgs eventArgs)
    {
        base.OnMouseDown(eventArgs);
        _pressed = eventArgs.Button == MouseButtons.Left;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs eventArgs)
    {
        base.OnMouseUp(eventArgs);
        _pressed = false;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs eventArgs)
    {
        base.OnMouseLeave(eventArgs);
        _pressed = false;
        Invalidate();
    }

    private static GraphicsPath CreateRoundedPath(Rectangle rectangle, int radius)
    {
        int diameter = Math.Min(radius * 2, Math.Min(rectangle.Width, rectangle.Height));
        var path = new GraphicsPath();
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
