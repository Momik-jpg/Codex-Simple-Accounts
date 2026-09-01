using System.Runtime.InteropServices;

namespace CodexAccountTray;

public sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private const int HotkeyId = 0xCA71;
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private readonly Action _onPressed;
    private bool _registered;

    public HotkeyWindow(Action onPressed)
    {
        _onPressed = onPressed;
        CreateHandle(new CreateParams());
        _registered = RegisterHotKey(Handle, HotkeyId, ModControl | ModAlt, (uint)Keys.R);
    }

    public bool IsRegistered => _registered;

    public void Dispose()
    {
        if (_registered)
        {
            UnregisterHotKey(Handle, HotkeyId);
            _registered = false;
        }
        DestroyHandle();
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmHotkey && message.WParam == HotkeyId)
        {
            _onPressed();
        }
        base.WndProc(ref message);
    }

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(nint window, int id);
}
