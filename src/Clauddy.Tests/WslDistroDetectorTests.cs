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

    [Fact]
    public void Filters_out_wsl_help_text_when_no_distros_installed()
    {
        // Reproduces the bug seen on a Windows machine with WSL installed but
        // no distros: `wsl.exe -l -q` prints its usage help to stdout instead of
        // an empty list. We must filter it.
        var help = string.Join('\n',
            "Copyright (c) Microsoft Corporation. All rights reserved.",
            "Usage: wsl.exe [Argument]",
            "Arguments:",
            "    --install <Options>",
            "        Install Windows Subsystem for Linux features.",
            "Options:",
            "    --distribution, -d [Argument]",
            "");
        var d = new WslDistroDetector(_ => help);
        d.List().Should().BeEmpty();
    }

    [Fact]
    public void Accepts_realistic_distro_names_with_versions_and_dots()
    {
        var d = new WslDistroDetector(_ => "Ubuntu\nUbuntu-22.04\nkali-linux\nDebian\n");
        d.List().Should().BeEquivalentTo(new[] { "Ubuntu", "Ubuntu-22.04", "kali-linux", "Debian" });
    }
}
