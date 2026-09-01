namespace CodexAccountTray;

public sealed record AccountCardPositions(
    int LimitX,
    int LimitWidth,
    int LoginX,
    int LogoutX,
    int StartX);

public static class AccountCardLayout
{
    public static AccountCardPositions Calculate(int cardWidth)
    {
        const int rightMargin = 16;
        int start = cardWidth - rightMargin - 96;
        int logout = start - 10 - 98;
        int login = logout - 10 - 130;
        const int limitX = 210;
        return new AccountCardPositions(
            limitX,
            Math.Max(100, login - 20 - limitX),
            login,
            logout,
            start);
    }
}
