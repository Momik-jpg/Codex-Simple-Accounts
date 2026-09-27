using CodexAccountTray;

namespace CodexAccountTray.Tests;

public sealed class TaskRouterActivityTests
{
    [Fact]
    public void RouterLease_MarksActivityUntilDisposed()
    {
        var activity = new TaskRouterActivity();
        int changes = 0;
        activity.Changed += (_, _) => changes++;

        IDisposable lease = activity.BeginRouterRun();

        Assert.True(activity.IsActive);
        lease.Dispose();
        lease.Dispose();
        Assert.False(activity.IsActive);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void RouterLease_BlocksAccountChanges()
    {
        var activity = new TaskRouterActivity();
        using IDisposable lease = activity.BeginRouterRun();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => activity.BeginAccountChange());

        Assert.Contains("Kontowechsel gesperrt", exception.Message);
    }

    [Fact]
    public void AccountChange_BlocksRouterStartUntilDisposed()
    {
        var activity = new TaskRouterActivity();
        using (activity.BeginAccountChange())
        {
            Assert.Throws<InvalidOperationException>(() => activity.BeginRouterRun());
        }

        using IDisposable router = activity.BeginRouterRun();
        Assert.True(activity.IsActive);
    }
}
