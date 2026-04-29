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
    private readonly Func<string, bool> _hasClaudeDir;

    public LabelResolver(IGitProbe git, Func<string, bool>? hasClaudeDir = null)
    {
        _git = git;
        _hasClaudeDir = hasClaudeDir ?? (p => Directory.Exists(Path.Combine(p, ".claude")));
    }

    public string Resolve(string cwd, string? overrideLabel)
    {
        if (!string.IsNullOrWhiteSpace(overrideLabel)) return overrideLabel!;
        // Prefer the nearest ancestor with a .claude/ marker — that's where the user
        // explicitly set up Claude Code. Beats a deep git toplevel (e.g., fraud/docs
        // is its own repo but the user thinks of the workspace as "fraud").
        if (TryFindClaudeAncestor(cwd, out var marked))
            return Path.GetFileName(marked.TrimEnd('/', '\\'));
        if (_git.TryGetRepoInfo(cwd, out var root, out _))
            return Path.GetFileName(root.TrimEnd('/', '\\'));
        return Path.GetFileName(cwd.TrimEnd('/', '\\'));
    }

    private bool TryFindClaudeAncestor(string cwd, out string match)
    {
        match = "";
        var dir = cwd?.TrimEnd('/', '\\');
        while (!string.IsNullOrEmpty(dir))
        {
            if (_hasClaudeDir(dir)) { match = dir; return true; }
            var parent = Path.GetDirectoryName(dir);
            if (string.IsNullOrEmpty(parent) || parent == dir) return false;
            dir = parent;
        }
        return false;
    }
}
