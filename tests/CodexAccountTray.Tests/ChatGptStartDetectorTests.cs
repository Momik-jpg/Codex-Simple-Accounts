using CodexAccountTray;

namespace CodexAccountTray.Tests;

public sealed class ChatGptStartDetectorTests
{
    [Fact]
    public void Observe_ReportsOnlyNewChatGptStarts()
    {
        bool running = false;
        var detector = new ChatGptStartDetector(() => running);

        Assert.False(detector.Observe());
        running = true;
        Assert.True(detector.Observe());
        Assert.False(detector.Observe());
        running = false;
        Assert.False(detector.Observe());
        running = true;
        Assert.True(detector.Observe());
    }
}
