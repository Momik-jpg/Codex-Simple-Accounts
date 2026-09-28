using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace CodexAccountTray;

public sealed class RoundedButton : Button
{
    private Color _normalColor;
    private int _cornerRadius = 10;
    private bool _pressed;
    private bool _hovered;

    public RoundedButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        Cursor = Cursors.Hand;
        TabStop = true;
        AccessibleRole = AccessibleRole.PushButton;
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
            : _pressed
                ? Blend(_normalColor, Color.Black, 0.18f)
                : _hovered || Focused
                    ? Blend(_normalColor, Color.White, 0.10f)
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

    protected override void OnMouseEnter(EventArgs eventArgs)
    {
        base.OnMouseEnter(eventArgs);
        _hovered = true;
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
        _hovered = false;
        _pressed = false;
        Invalidate();
    }

    protected override void OnKeyDown(KeyEventArgs eventArgs)
    {
        base.OnKeyDown(eventArgs);
        if (eventArgs.KeyCode is Keys.Space or Keys.Enter)
        {
            _pressed = true;
            Invalidate();
        }
    }

    protected override void OnKeyUp(KeyEventArgs eventArgs)
    {
        base.OnKeyUp(eventArgs);
        if (eventArgs.KeyCode is Keys.Space or Keys.Enter)
        {
            _pressed = false;
            Invalidate();
        }
    }

    protected override void OnGotFocus(EventArgs eventArgs)
    {
        base.OnGotFocus(eventArgs);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs eventArgs)
    {
        base.OnLostFocus(eventArgs);
        _pressed = false;
        Invalidate();
    }

    protected override void OnEnabledChanged(EventArgs eventArgs)
    {
        base.OnEnabledChanged(eventArgs);
        Cursor = Enabled ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    private static Color Blend(Color from, Color to, float amount)
    {
        int red = (int)Math.Round(from.R + (to.R - from.R) * amount);
        int green = (int)Math.Round(from.G + (to.G - from.G) * amount);
        int blue = (int)Math.Round(from.B + (to.B - from.B) * amount);
        return Color.FromArgb(from.A, red, green, blue);
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
