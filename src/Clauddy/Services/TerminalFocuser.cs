using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;

namespace Clauddy.Services;

/// <summary>
/// Best-effort: given a hook script's PID, walks up the parent process chain
/// (via WMI) until it finds a process with a top-level window, then brings that
/// window to the foreground. Falls back to no-op if no window is found.
///
/// Use case: clicking a session pill should focus the terminal where Claude Code
/// is running. The chain typically looks like:
///     bash (the hook, PID we receive) -> claude.exe -> wt.exe / cmd.exe / mintty.exe (terminal — has the window)
/// </summary>
public class TerminalFocuser
{
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    private const int SW_RESTORE = 9;

    private const int MaxAncestors = 8;

    public bool TryFocus(int pid)
    {
        if (pid <= 0) return false;
        var current = pid;
        for (int hop = 0; hop < MaxAncestors && current > 0; hop++)
        {
            try
            {
                var p = Process.GetProcessById(current);
                if (p.MainWindowHandle != IntPtr.Zero)
                {
                    ShowWindow(p.MainWindowHandle, SW_RESTORE);
                    return SetForegroundWindow(p.MainWindowHandle);
                }
            }
            catch { /* process may have exited; try the parent */ }

            var parent = GetParentPid(current);
            if (parent == current || parent == 0) break;
            current = parent;
        }
        return false;
    }

    private static int GetParentPid(int pid)
    {
        try
        {
            using var search = new ManagementObjectSearcher(
                $"SELECT ParentProcessId FROM Win32_Process WHERE ProcessId={pid}");
            foreach (var mo in search.Get())
            {
                using (mo)
                {
                    var v = mo["ParentProcessId"];
                    if (v != null) return Convert.ToInt32(v);
                }
            }
        }
        catch { }
        return 0;
    }
}
