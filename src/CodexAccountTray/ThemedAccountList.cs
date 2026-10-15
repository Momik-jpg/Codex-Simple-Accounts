using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace CodexAccountTray;

/// <summary>Clips account rows and scrolls without a native, unthemed scrollbar.</summary>
public sealed class ThemedAccountList : UserControl
{
    private readonly AccountScrollBar _scrollBar;
    private int _contentHeight;
    private int _offset;

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Panel Viewport { get; } = new() { TabStop = false };

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int ScrollOffset => _offset;

    public ThemedAccountList()
    {
        Name = "AccountList";
        AccessibleName = "Kontenliste";
        TabStop = true;
        _scrollBar = new AccountScrollBar(this) { Name = "AccountScrollBar", AccessibleName = "Kontenliste scrollen" };
        Controls.Add(Viewport);
        Controls.Add(_scrollBar);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    public void ApplyPalette(AccountPalette palette)
    {
        BackColor = Viewport.BackColor = palette.Background;
        _scrollBar.ApplyPalette(palette);
    }

    public void AddCard(Control card, int top)
    {
        card.Location = new Point(0, top - _offset);
        Viewport.Controls.Add(card);
        WatchFocus(card, card);
    }

    private void WatchFocus(Control control, Control card)
    {
        control.Enter += (_, _) =>
        {
            if (card.Top < 0) ScrollTo(_offset + card.Top);
            else if (card.Bottom > Viewport.Height) ScrollTo(_offset + card.Bottom - Viewport.Height);
        };
        foreach (Control child in control.Controls) WatchFocus(child, card);
    }

    public void SetContentHeight(int height)
    {
        _contentHeight = Math.Max(0, height);
        PerformLayout();
    }

    public void ScrollTo(int offset)
    {
        int next = Math.Clamp(offset, 0, Math.Max(0, _contentHeight - Viewport.Height));
        int delta = _offset - next;
        _offset = next;
        foreach (Control child in Viewport.Controls) child.Top += delta;
        _scrollBar.SetRange(_contentHeight, Viewport.Height, _offset);
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (_scrollBar is null) return;
        bool overflow = _contentHeight > ClientSize.Height;
        int gutter = overflow ? (int)(18 * DeviceDpi / 96f) : 0;
        Viewport.Bounds = new Rectangle(0, 0, Math.Max(0, ClientSize.Width - gutter), ClientSize.Height);
        _scrollBar.Bounds = new Rectangle(Viewport.Right, 0, gutter, ClientSize.Height);
        _scrollBar.Visible = overflow;
        ScrollTo(_offset);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        ScrollTo(_offset - (int)(48 * DeviceDpi / 96f) * e.Delta / 120);
        if (e is HandledMouseEventArgs handled) handled.Handled = true;
    }

    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) is Keys.Up or Keys.Down or Keys.PageUp or Keys.PageDown or Keys.Home or Keys.End || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (HandleScrollKey(e.KeyCode)) { e.Handled = true; e.SuppressKeyPress = true; }
        base.OnKeyDown(e);
    }

    private bool HandleScrollKey(Keys key)
    {
        int step = (int)(48 * DeviceDpi / 96f);
        int? next = key switch
        {
            Keys.Up => _offset - step, Keys.Down => _offset + step,
            Keys.PageUp => _offset - Viewport.Height, Keys.PageDown => _offset + Viewport.Height,
            Keys.Home => 0, Keys.End => _contentHeight, _ => null
        };
        if (next is null) return false;
        ScrollTo(next.Value);
        return true;
    }

    private sealed class AccountScrollBar(ThemedAccountList owner) : Control
    {
        private AccountPalette _palette = AccountPalette.Dark;
        private int _content, _view, _offset;
        private int? _dragStart;
        private int _dragOffset;

        public void ApplyPalette(AccountPalette palette)
        {
            _palette = palette;
            BackColor = palette.Background;
            Invalidate();
        }

        public void SetRange(int content, int view, int offset)
        {
            _content = content; _view = view; _offset = offset;
            AccessibleDescription = $"{offset} von {Math.Max(0, content - view)}";
            AccessibleRole = AccessibleRole.ScrollBar;
            TabStop = true;
            Invalidate();
        }

        private Rectangle Thumb
        {
            get
            {
                int height = Math.Min(Height, Math.Max((int)(24 * DeviceDpi / 96f), _content <= 0 ? Height : (int)((long)Height * _view / _content)));
                int top = _content <= _view ? 0 : (int)((long)_offset * (Height - height) / (_content - _view));
                int width = Math.Min(Width, (int)(6 * DeviceDpi / 96f));
                return new Rectangle((Width - width) / 2, top, width, height);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            Rectangle thumb = Thumb;
            if (thumb.Width <= 0 || thumb.Height <= 0) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = new GraphicsPath();
            path.AddArc(thumb.Left, thumb.Top, thumb.Width, thumb.Width, 180, 180);
            path.AddArc(thumb.Left, thumb.Bottom - thumb.Width, thumb.Width, thumb.Width, 0, 180);
            path.CloseFigure();
            using var brush = new SolidBrush(Focused || _dragStart is not null ? _palette.Accent : _palette.Muted);
            e.Graphics.FillPath(brush, path);
            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -1, -1), _palette.Text, BackColor);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            Focus();
            if (e.Y < Thumb.Top) owner.ScrollTo(_offset - _view);
            else if (e.Y > Thumb.Bottom) owner.ScrollTo(_offset + _view);
            else { _dragStart = e.Y; _dragOffset = _offset; Capture = true; }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragStart is int start && Height > Thumb.Height)
                owner.ScrollTo(_dragOffset + (int)((long)(e.Y - start) * Math.Max(0, _content - _view) / (Height - Thumb.Height)));
        }

        protected override void OnMouseUp(MouseEventArgs e) { Capture = false; base.OnMouseUp(e); }
        protected override void OnMouseCaptureChanged(EventArgs e) { _dragStart = null; Invalidate(); base.OnMouseCaptureChanged(e); }
        protected override bool IsInputKey(Keys keyData) => owner.IsInputKey(keyData);
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (owner.HandleScrollKey(e.KeyCode)) { e.Handled = true; e.SuppressKeyPress = true; }
            base.OnKeyDown(e);
        }
    }
}
