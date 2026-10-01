using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace CodexAccountTray;

public sealed class RoundedButton : Button
{
    private Color _normalColor;
    private int _cornerRadius = 10;
    private bool _pressed;
    private bool _hovered;
    private float _hoverProgress;
    private float _hoverTarget;
    private readonly System.Windows.Forms.Timer _hoverTimer = new() { Interval = 16 };

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
        _hoverTimer.Tick += (_, _) => AdvanceHoverAnimation();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _hoverTimer.Dispose();
        base.Dispose(disposing);
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

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color BorderColor { get; set; } = Color.Transparent;

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
            ? Blend(_normalColor, Parent?.BackColor ?? BackColor, 0.65f)
            : _pressed
                ? Blend(_normalColor, Color.Black, 0.18f)
                : Blend(_normalColor, Color.White, _hoverProgress * 0.10f);
        using var brush = new SolidBrush(fill);
        eventArgs.Graphics.FillPath(brush, path);
        if (BorderColor.A > 0)
        {
            using var borderPen = new Pen(BorderColor);
            eventArgs.Graphics.DrawPath(borderPen, path);
        }
        Rectangle textBounds = _pressed
            ? new Rectangle(bounds.X, bounds.Y + 1, bounds.Width, bounds.Height)
            : bounds;
        TextRenderer.DrawText(
            eventArgs.Graphics,
            Text,
            Font,
            textBounds,
            Enabled ? ForeColor : Blend(ForeColor, Parent?.BackColor ?? BackColor, 0.55f),
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
        UpdateHoverTarget();
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
        UpdateHoverTarget();
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
        UpdateHoverTarget();
    }

    protected override void OnLostFocus(EventArgs eventArgs)
    {
        base.OnLostFocus(eventArgs);
        _pressed = false;
        UpdateHoverTarget();
    }

    protected override void OnEnabledChanged(EventArgs eventArgs)
    {
        base.OnEnabledChanged(eventArgs);
        Cursor = Enabled ? Cursors.Hand : Cursors.Default;
        UpdateHoverTarget();
        Invalidate();
    }

    private void UpdateHoverTarget()
    {
        _hoverTarget = Enabled && (_hovered || Focused) ? 1f : 0f;
        if (!MotionPreferences.AnimationsEnabled)
        {
            _hoverTimer.Stop();
            _hoverProgress = _hoverTarget;
            Invalidate();
            return;
        }
        if (Math.Abs(_hoverProgress - _hoverTarget) > 0.001f)
            _hoverTimer.Start();
        Invalidate();
    }

    private void AdvanceHoverAnimation()
    {
        if (!MotionPreferences.AnimationsEnabled)
        {
            _hoverProgress = _hoverTarget;
        }
        else
        {
            float direction = Math.Sign(_hoverTarget - _hoverProgress);
            _hoverProgress = Math.Clamp(_hoverProgress + direction * 0.1f, 0f, 1f);
            if (Math.Abs(_hoverProgress - _hoverTarget) < 0.101f)
                _hoverProgress = _hoverTarget;
        }
        if (_hoverProgress == _hoverTarget) _hoverTimer.Stop();
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

internal static class MotionPreferences
{
    private const uint GetClientAreaAnimation = 0x1042;

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool SystemParametersInfoW(uint action, uint parameter, out int value, uint flags);

    public static bool AnimationsEnabled =>
        OperatingSystem.IsWindows() &&
        SystemParametersInfoW(GetClientAreaAnimation, 0, out int enabled, 0) && enabled != 0;
}
