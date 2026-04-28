using Clauddy.Services;
using FluentAssertions;
using Xunit;

namespace Clauddy.Tests;

public class WslDistroDetectorTests
{
    [Fact]
    public void Parses_distro_names_one_per_line()
    {
        var d = new WslDistroDetector(_ => "Ubuntu\nDebian\n");
        d.List().Should().BeEquivalentTo(new[] { "Ubuntu", "Debian" });
    }

    [Fact]
    public void Trims_blank_lines_and_whitespace()
    {
        var d = new WslDistroDetector(_ => "  Ubuntu  \n\n  \nDebian\n");
        d.List().Should().BeEquivalentTo(new[] { "Ubuntu", "Debian" });
    }

    [Fact]
    public void Empty_output_returns_empty_list()
    {
        var d = new WslDistroDetector(_ => "");
        d.List().Should().BeEmpty();
    }

    [Fact]
    public void Throwing_runner_returns_empty_list()
    {
        var d = new WslDistroDetector(_ => throw new InvalidOperationException("wsl not installed"));
        d.List().Should().BeEmpty();
    }
}
