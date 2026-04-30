using System.Runtime.InteropServices;
using Clauddy.Models;

namespace Clauddy.Services;

public class LifecycleManager
{
    private readonly SessionStore _store;
    private readonly Func<DateTimeOffset> _clock;
    private readonly TimeSpan _sleepAfter;
    private readonly Func<int, bool>? _isAlive;

    /// <param name="sleepAfter">
    /// How long a Chilling session must be silent before it transitions to Sleeping.
    /// Sessions are never removed by age — only SessionEnd hooks or confirmed process
    /// death remove tiles, so accumulated sleeping pills act as a session inventory.
    /// </param>
    /// <param name="isAlive">
    /// Optional process-liveness probe. Given a PID, returns true iff the process is
    /// still running. Pass null in tests; production wires Default() which uses
    /// kernel32 OpenProcess. Sessions whose Pid is 0 are skipped (no probe).
    /// </param>
    public LifecycleManager(SessionStore store, Func<DateTimeOffset> clock, TimeSpan sleepAfter,
        Func<int, bool>? isAlive = null)
    {
        _store = store; _clock = clock; _sleepAfter = sleepAfter; _isAlive = isAlive;
    }

    // Returns: true if alive OR opaque (can't confirm); false only when we positively
    // confirmed the process exited. OpenProcess failure (any error code) is "opaque" —
    // INVALID_PARAMETER is returned both for "PID never existed" (e.g. Git Bash sends
    // $PPID=1 from the MSYS pseudo-process table) AND for "PID existed and is gone now",
    // and we can't tell which. Mapping it to "dead" mass-removes legitimate Git Bash
    // sessions ~30s after their last hook. Stale GC + SessionEnd handle real cleanup.
    public static Func<int, bool> DefaultIsAlive => pid =>
    {
        if (pid <= 0) return true;
        var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
        if (h == IntPtr.Zero) return true; // opaque — assume alive, defer to stale GC
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
        // Removal: only by confirmed process death — the lone fast cleanup path. Stale
        // sessions are no longer auto-removed; the user wants the pill bar to act as a
        // running session inventory, even if a session is silent for hours.
        if (_isAlive != null)
        {
            foreach (var s in _store.Sessions.Where(s => s.Pid > 0).ToList())
            {
                if (!_isAlive(s.Pid)) _store.Remove(s.SessionId);
            }
        }
        // Sleep transition: Chilling sessions older than _sleepAfter become Sleeping.
        // The next hook for that session (SessionStart, UserPromptSubmit, Stop, …)
        // overwrites it back to whatever state the hook reports.
        var sleepBefore = _clock() - _sleepAfter;
        foreach (var s in _store.Sessions.Where(s => s.State == SessionState.Chilling && s.LastSeen < sleepBefore).ToList())
        {
            _store.Upsert(s with { State = SessionState.Sleeping });
        }
    }
}
