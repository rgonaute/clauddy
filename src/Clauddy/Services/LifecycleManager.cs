using System.Runtime.InteropServices;

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
    /// kernel32 OpenProcess. Sessions whose Pid is 0 are skipped (no probe).
    /// </param>
    public LifecycleManager(SessionStore store, Func<DateTimeOffset> clock, TimeSpan maxAge,
        Func<int, bool>? isAlive = null)
    {
        _store = store; _clock = clock; _maxAge = maxAge; _isAlive = isAlive;
    }

    // Returns: true if alive OR opaque (can't confirm); false only when we know it exited.
    // Opaque PIDs are normal — MSYS/Git Bash sends $PPID=1, WSL sends a Linux PID, neither
    // is a Windows process. SessionEnd hook + 30-min stale GC clean those up instead.
    public static Func<int, bool> DefaultIsAlive => pid =>
    {
        if (pid <= 0) return true;
        var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
        if (h == IntPtr.Zero) return true;
        try
        {
            return GetExitCodeProcess(h, out var code) && code == STILL_ACTIVE;
        }
        finally { CloseHandle(h); }
    };

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint STILL_ACTIVE = 259;
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(IntPtr hProcess, out uint lpExitCode);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

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
