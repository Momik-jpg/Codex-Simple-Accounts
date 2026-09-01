using System.Drawing;
using System.Reflection;

namespace CodexAccountTray.Tests;

public sealed class RoundedButtonTests
{
    [Fact]
    public void Hover_DoesNotChangeButtonFillColor()
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
        button.DrawToBitmap(after, button.ClientRectangle);

        Assert.Equal(before.GetPixel(60, 20), after.GetPixel(60, 20));
    }
}
