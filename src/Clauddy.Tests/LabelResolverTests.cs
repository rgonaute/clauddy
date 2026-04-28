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
        r.Resolve("/c/repo/sub", "").Should().Be("repo@main");
    }

    [Fact]
    public void Git_repo_yields_repo_at_branch()
    {
        var r = new LabelResolver(new FakeGit { RepoRoot = "/c/projects/clauddy", Branch = "feat/x" });
        r.Resolve("/c/projects/clauddy/src", null).Should().Be("clauddy@feat/x");
    }

    [Fact]
    public void Non_git_falls_back_to_basename()
    {
        var r = new LabelResolver(new FakeGit());
        r.Resolve("/c/scratch/notes", null).Should().Be("notes");
    }

    [Fact]
    public void Windows_paths_are_handled()
    {
        var r = new LabelResolver(new FakeGit());
        r.Resolve(@"C:\Users\Ron\projects\foo", null).Should().Be("foo");
    }
}
