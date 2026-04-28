using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Clauddy.Services;

public class WslDistroDetector
{
    private readonly Func<string, string> _run;

    // Distro names: identifier-shaped — letters, digits, dot, underscore, hyphen.
    // Anything else (spaces, punctuation, brackets) means we're looking at help text,
    // not a distro name. Real distros: "Ubuntu", "Ubuntu-22.04", "Debian", "kali-linux".
    private static readonly Regex DistroName = new(@"^[A-Za-z][A-Za-z0-9._-]*$", RegexOptions.Compiled);

    public WslDistroDetector(Func<string, string> run) => _run = run;

    public static WslDistroDetector Default() => new(args =>
    {
        var psi = new ProcessStartInfo("wsl.exe", args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.Unicode  // wsl outputs UTF-16-LE
        };
        using var p = Process.Start(psi)!;
        var s = p.StandardOutput.ReadToEnd();
        p.WaitForExit(3000);
        // If wsl.exe failed (no distros installed → it dumps usage help to stdout
        // and exits non-zero), discard the output entirely.
        return p.ExitCode == 0 ? s : "";
    });

    public IReadOnlyList<string> List()
    {
        try
        {
            var raw = _run("-l -q");
            return raw.Split('\n', '\r')
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrEmpty(l))
                .Where(l => DistroName.IsMatch(l))
                .ToList();
        }
        catch { return Array.Empty<string>(); }
    }
}
