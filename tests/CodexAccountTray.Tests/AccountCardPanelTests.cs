using System.Drawing;

namespace CodexAccountTray.Tests;

public sealed class AccountCardPanelTests
{
    [Fact]
    public void Defaults_MatchVariantAStyle()
    {
        using var card = new AccountCardPanel();

        Assert.Equal(14, card.CornerRadius);
        Assert.False(card.IsActive);
        Assert.Equal(Color.FromArgb(39, 50, 61), card.BorderColor);
        Assert.Equal(Color.FromArgb(64, 132, 214), card.ActiveBorderColor);
    }
}
