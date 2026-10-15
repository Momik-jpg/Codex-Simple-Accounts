using System.Drawing;
using System.Reflection;

namespace CodexAccountTray.Tests;

public sealed class RoundedButtonTests
{
    [Fact]
    public void Hover_GivesAVisibleFillFeedback()
    {
        using var button = new RoundedButton
        {
            Size = new Size(120, 40),
            BackColor = Color.FromArgb(48, 52, 56),
            Text = string.Empty
        };
        using var before = new Bitmap(button.Width, button.Height);
        using var after = new Bitmap(button.Width, button.Height);

        button.DrawToBitmap(before, button.ClientRectangle);
        typeof(RoundedButton)
            .GetMethod("OnMouseEnter", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(button, [EventArgs.Empty]);
        var advance = typeof(RoundedButton)
            .GetMethod("AdvanceHoverAnimation", BindingFlags.Instance | BindingFlags.NonPublic)!;
        for (int i = 0; i < 12; i++) advance.Invoke(button, null);
        button.DrawToBitmap(after, button.ClientRectangle);

        Assert.NotEqual(before.GetPixel(60, 20), after.GetPixel(60, 20));
        Assert.Equal(new Size(120, 40), button.Size);

        typeof(RoundedButton)
            .GetMethod("OnMouseLeave", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(button, [EventArgs.Empty]);
        for (int i = 0; i < 12; i++) advance.Invoke(button, null);
        using var restored = new Bitmap(button.Width, button.Height);
        button.DrawToBitmap(restored, button.ClientRectangle);
        Assert.Equal(before.GetPixel(60, 20), restored.GetPixel(60, 20));
    }
}
