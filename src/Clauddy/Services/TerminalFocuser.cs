using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Clauddy.Services;

/// <summary>
/// Best-effort: given a hook script's PID, walks up the parent process chain
/// until it finds a process with a top-level window, then brings that window to
/// the foreground. Falls back to no-op if no window is found.
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

    // Walks the toolhelp snapshot to find ParentProcessId for the given PID.
    // Replaces a WMI/System.Management dependency that pulled in extra DLLs.
    private static int GetParentPid(int pid)
    {
        var snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snap == new IntPtr(-1)) return 0;
        try
        {
            var pe = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>() };
            if (!Process32First(snap, ref pe)) return 0;
            do
            {
                if (pe.th32ProcessID == (uint)pid) return (int)pe.th32ParentProcessID;
            } while (Process32Next(snap, ref pe));
        }
        finally { CloseHandle(snap); }
        return 0;
    }

    private const uint TH32CS_SNAPPROCESS = 0x00000002;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool Process32First(IntPtr hSnap, ref PROCESSENTRY32 pe);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool Process32Next(IntPtr hSnap, ref PROCESSENTRY32 pe);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr h);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PROCESSENTRY32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
    }
}
