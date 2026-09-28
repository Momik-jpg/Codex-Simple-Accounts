namespace CodexAccountTray;

/// <summary>
/// Prevents router sessions and account mutations from overlapping. The process manager
/// remains responsible for serializing account switches among themselves.
/// </summary>
public sealed class TaskRouterActivity
{
    private const int Idle = 0;
    private const int RouterRun = 1;
    private const int AccountChange = 2;
    private int _state;

    public bool IsActive => Volatile.Read(ref _state) == RouterRun;
    public bool IsAccountChangeActive => Volatile.Read(ref _state) == AccountChange;
    public bool IsBusy => Volatile.Read(ref _state) != Idle;

    public event EventHandler? Changed;

    public IDisposable BeginRouterRun()
    {
        int previous = Interlocked.CompareExchange(ref _state, RouterRun, Idle);
        if (previous != Idle)
        {
            throw new InvalidOperationException(previous == RouterRun
                ? "Eine Auto-Aufgabe läuft bereits."
                : "Ein Kontowechsel läuft noch. Bitte danach die Aufgabe starten.");
        }

        NotifyChanged();
        return new Lease(this, RouterRun);
    }

    public IDisposable BeginAccountChange()
    {
        int previous = Interlocked.CompareExchange(ref _state, AccountChange, Idle);
        if (previous != Idle)
        {
            throw new InvalidOperationException(previous == RouterRun
                ? "Während einer Auto-Aufgabe ist der Kontowechsel gesperrt."
                : "Ein Kontowechsel läuft bereits.");
        }

        NotifyChanged();
        return new Lease(this, AccountChange);
    }

    private void End(int expectedState)
    {
        if (Interlocked.CompareExchange(ref _state, Idle, expectedState) == expectedState)
        {
            NotifyChanged();
        }
    }

    private void NotifyChanged()
    {
        EventHandler? changed = Changed;
        if (changed is null)
        {
            return;
        }

        foreach (Delegate subscriber in changed.GetInvocationList())
        {
            try
            {
                ((EventHandler)subscriber)(this, EventArgs.Empty);
            }
            catch
            {
                // UI refresh failures must never strand the exclusive activity state.
            }
        }
    }

    private sealed class Lease(TaskRouterActivity owner, int state) : IDisposable
    {
        private TaskRouterActivity? _owner = owner;

        public void Dispose()
        {
            Interlocked.Exchange(ref _owner, null)?.End(state);
        }
    }
}
