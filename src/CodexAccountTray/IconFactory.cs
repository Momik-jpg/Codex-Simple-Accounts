namespace CodexAccountTray;

public static class IconFactory
{
    public static Icon CreateTrayIcon()
    {
        Stream stream = typeof(IconFactory).Assembly.GetManifestResourceStream("CodexAccountTray.AppIcon.ico")
            ?? throw new InvalidOperationException("Die eingebettete App-Symbolressource fehlt.");
        using (stream)
        using (var icon = new Icon(stream))
        {
            return (Icon)icon.Clone();
        }
    }
}
