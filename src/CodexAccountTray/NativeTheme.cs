using System.Runtime.InteropServices;

namespace CodexAccountTray;

internal static class NativeTheme
{
    private const int ImmersiveDarkMode = 20;
    private const int ImmersiveDarkModeBefore20H1 = 19;
    private const int BorderColor = 34;
    private const int CaptionColor = 35;
    private const int TextColor = 36;

    public static void ApplyDarkTitleBar(nint handle, Color background)
    {
        int enabled = 1;
        if (DwmSetWindowAttribute(handle, ImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
        {
            DwmSetWindowAttribute(handle, ImmersiveDarkModeBefore20H1, ref enabled, sizeof(int));
        }

        int backgroundColor = ToColorRef(background);
        int foregroundColor = ToColorRef(Color.White);
        DwmSetWindowAttribute(handle, BorderColor, ref backgroundColor, sizeof(int));
        DwmSetWindowAttribute(handle, CaptionColor, ref backgroundColor, sizeof(int));
        DwmSetWindowAttribute(handle, TextColor, ref foregroundColor, sizeof(int));
    }

    private static int ToColorRef(Color color) =>
        color.R | (color.G << 8) | (color.B << 16);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        nint window,
        int attribute,
        ref int value,
        int valueSize);
}
