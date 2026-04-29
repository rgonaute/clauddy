using System.Diagnostics;
using System.IO;

namespace Clauddy.Services;

public interface IGitProbe
{
    bool TryGetRepoInfo(string cwd, out string repoRoot, out string branch);
}

public class GitProbe : IGitProbe
{
    public bool TryGetRepoInfo(string cwd, out string repoRoot, out string branch)
    {
        repoRoot = ""; branch = "";
        try
        {
            repoRoot = Run("rev-parse --show-toplevel", cwd);
            branch = Run("rev-parse --abbrev-ref HEAD", cwd);
            return !string.IsNullOrWhiteSpace(repoRoot) && !string.IsNullOrWhiteSpace(branch);
        }
        catch { return false; }
    }

    private static string Run(string args, string cwd)
    {
        var psi = new ProcessStartInfo("git", args)
        {
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            UseShellExecute = false
        };
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd().Trim();
        p.WaitForExit(2000);
        return p.ExitCode == 0 ? output : "";
    }
}

public class LabelResolver
{
    private readonly IGitProbe _git;
    public LabelResolver(IGitProbe git) => _git = git;

    public string Resolve(string cwd, string? overrideLabel)
    {
        if (!string.IsNullOrWhiteSpace(overrideLabel)) return overrideLabel!;
        if (_git.TryGetRepoInfo(cwd, out var root, out _))
            return Path.GetFileName(root.TrimEnd('/', '\\'));
        return Path.GetFileName(cwd.TrimEnd('/', '\\'));
    }
}
