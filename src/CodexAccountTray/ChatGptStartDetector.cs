namespace CodexAccountTray;

public sealed class ChatGptStartDetector
{
    private readonly Func<bool> _isRunning;
    private bool _wasRunning;

    public ChatGptStartDetector(Func<bool> isRunning)
    {
        _isRunning = isRunning;
    }

    public bool Observe()
    {
        bool isRunning = _isRunning();
        bool started = isRunning && !_wasRunning;
        _wasRunning = isRunning;
        return started;
    }
}
