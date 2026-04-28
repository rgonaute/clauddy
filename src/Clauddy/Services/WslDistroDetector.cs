using System.Diagnostics;
using System.Text;

namespace Clauddy.Services;

public class WslDistroDetector
{
    private readonly Func<string, string> _run;

    public WslDistroDetector(Func<string, string> run) => _run = run;

    public static WslDistroDetector Default() => new(args =>
    {
        var psi = new ProcessStartInfo("wsl.exe", args)
        {
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.Unicode  // wsl outputs UTF-16-LE
        };
        using var p = Process.Start(psi)!;
        var s = p.StandardOutput.ReadToEnd();
        p.WaitForExit(3000);
        return s;
    });

    public IReadOnlyList<string> List()
    {
        try
        {
            var raw = _run("-l -q");
            return raw.Split('\n', '\r')
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrEmpty(l))
                .ToList();
        }
        catch { return Array.Empty<string>(); }
    }
}
