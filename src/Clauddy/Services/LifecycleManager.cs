using System.Diagnostics;

namespace Clauddy.Services;

public class LifecycleManager
{
    private readonly SessionStore _store;
    private readonly Func<DateTimeOffset> _clock;
    private readonly TimeSpan _maxAge;
    private readonly Func<int, bool>? _isAlive;

    /// <param name="isAlive">
    /// Optional process-liveness probe. Given a PID, returns true iff the process is
    /// still running. Pass null in tests; production wires Default() which uses
    /// System.Diagnostics.Process. Sessions whose Pid is 0 are skipped (no probe).
    /// </param>
    public LifecycleManager(SessionStore store, Func<DateTimeOffset> clock, TimeSpan maxAge,
        Func<int, bool>? isAlive = null)
    {
        _store = store; _clock = clock; _maxAge = maxAge; _isAlive = isAlive;
    }

    public static Func<int, bool> DefaultIsAlive => pid =>
    {
        if (pid <= 0) return true;  // unknown — treat as alive
        try
        {
            var p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        // PID isn't a Windows-visible process — e.g. MSYS/Git Bash sends $PPID=1, WSL
        // sends a Linux PID. Treat as opaque/alive: we can't confirm death, so let
        // SessionEnd + 30-min stale GC handle cleanup instead of nuking on first tick.
        catch (ArgumentException) { return true; }
        catch { return false; }
    };

    public void Tick()
    {
        // First: any session whose owning process has died, remove immediately.
        // Covers Ctrl+C, closed terminal, kill -9 — cases where SessionEnd never fires.
        if (_isAlive != null)
        {
            foreach (var s in _store.Sessions.Where(s => s.Pid > 0).ToList())
            {
                if (!_isAlive(s.Pid)) _store.Remove(s.SessionId);
            }
        }
        // Belt-and-suspenders: stale sessions (no hook activity for maxAge) also go.
        _store.RemoveStale(_clock(), _maxAge);
    }
}
