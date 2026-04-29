using Clauddy.Services;
using FluentAssertions;
using Xunit;

namespace Clauddy.Tests;

public class LabelResolverTests
{
    private class FakeGit : IGitProbe
    {
        public string? RepoRoot;
        public string? Branch;
        public bool TryGetRepoInfo(string cwd, out string repoRoot, out string branch)
        {
            repoRoot = RepoRoot ?? "";
            branch = Branch ?? "";
            return RepoRoot != null && Branch != null;
        }
    }

    [Fact]
    public void Override_takes_precedence()
    {
        var r = new LabelResolver(new FakeGit { RepoRoot = "/c/repo", Branch = "main" });
        r.Resolve("/c/repo/sub", "my-label").Should().Be("my-label");
    }

    [Fact]
    public void Empty_override_falls_through()
    {
        var r = new LabelResolver(new FakeGit { RepoRoot = "/c/repo", Branch = "main" });
        r.Resolve("/c/repo/sub", "").Should().Be("repo");
    }

    [Fact]
    public void Git_repo_yields_repo_name()
    {
        var r = new LabelResolver(new FakeGit { RepoRoot = "/c/projects/clauddy", Branch = "feat/x" }, _ => false);
        r.Resolve("/c/projects/clauddy/src", null).Should().Be("clauddy");
    }

    [Fact]
    public void Claude_marker_ancestor_beats_inner_git_repo()
    {
        // Tree: /g/claude/fraud has .claude/, /g/claude/fraud/docs has .git.
        // Label should be "fraud" (the marked workspace), not "docs" (the inner repo).
        var r = new LabelResolver(
            new FakeGit { RepoRoot = "/g/claude/fraud/docs", Branch = "main" },
            p => p.Replace('\\', '/') == "/g/claude/fraud");
        r.Resolve("/g/claude/fraud/docs", null).Should().Be("fraud");
    }

    [Fact]
    public void Claude_marker_uses_nearest_ancestor()
    {
        // Both /g/claude and /g/claude/clauddy have .claude/ — should pick the
        // nearest (clauddy), not walk all the way up to "claude".
        var r = new LabelResolver(
            new FakeGit { RepoRoot = "/g/claude/clauddy", Branch = "main" },
            p => { var n = p.Replace('\\', '/'); return n == "/g/claude" || n == "/g/claude/clauddy"; });
        r.Resolve("/g/claude/clauddy", null).Should().Be("clauddy");
    }

    [Fact]
    public void Non_git_falls_back_to_basename()
    {
        var r = new LabelResolver(new FakeGit(), _ => false);
        r.Resolve("/c/scratch/notes", null).Should().Be("notes");
    }

    [Fact]
    public void Windows_paths_are_handled()
    {
        var r = new LabelResolver(new FakeGit(), _ => false);
        r.Resolve(@"C:\Users\Ron\projects\foo", null).Should().Be("foo");
    }
}
