using CodexAccountTray;

namespace CodexAccountTray.Tests;

public sealed class TurnActivityTrackerTests
{
    [Fact]
    public async Task WaitForIdleAsync_WaitsUntilLastTurnCompleted()
    {
        var tracker = new TurnActivityTracker();
        tracker.Observe("{\"method\":\"turn/started\",\"params\":{\"turn\":{\"id\":\"t1\"}}}");
        tracker.Observe("{\"method\":\"turn/started\",\"params\":{\"turn\":{\"id\":\"t2\"}}}");

        Task waiting = tracker.WaitForIdleAsync(CancellationToken.None);
        tracker.Observe("{\"method\":\"turn/completed\",\"params\":{\"turn\":{\"id\":\"t1\"}}}");
        Assert.False(waiting.IsCompleted);

        tracker.Observe("{\"method\":\"turn/completed\",\"params\":{\"turn\":{\"id\":\"t2\"}}}");
        await waiting;
    }

    [Fact]
    public void Observe_IgnoresMalformedAndUnrelatedMessages()
    {
        var tracker = new TurnActivityTracker();

        tracker.Observe("not json");
        tracker.Observe("{\"method\":\"account/updated\"}");

        Assert.False(tracker.HasActiveTurns);
    }
}
