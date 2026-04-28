namespace Clauddy.Services;

public class LifecycleManager
{
    private readonly SessionStore _store;
    private readonly Func<DateTimeOffset> _clock;
    private readonly TimeSpan _maxAge;

    public LifecycleManager(SessionStore store, Func<DateTimeOffset> clock, TimeSpan maxAge)
    {
        _store = store; _clock = clock; _maxAge = maxAge;
    }

    public void Tick() => _store.RemoveStale(_clock(), _maxAge);
}
