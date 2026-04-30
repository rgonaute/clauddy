using Clauddy.Models;
using Clauddy.Views;
using FluentAssertions;
using Xunit;

namespace Clauddy.Tests;

public class CalibrateDialogTests
{
    [Theory]
    [InlineData("25", 1_000_000, 4_000_000)]   // 1M tokens at 25% → 4M cap
    [InlineData("50", 1_000_000, 2_000_000)]
    [InlineData("100", 1_000_000, 1_000_000)]
    public void BackSolve_divides_tokens_by_pct(string pct, long tokens, long expected)
    {
        CalibrateDialog.BackSolve(pct, tokens).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData(null)]
    [InlineData("abc")]
    [InlineData("-5")]
    public void BackSolve_returns_zero_for_invalid_or_blank_pct(string? pct)
    {
        CalibrateDialog.BackSolve(pct, 1_000_000).Should().Be(0);
    }

    [Fact]
    public void BackSolve_returns_zero_when_no_tokens_logged_yet()
    {
        CalibrateDialog.BackSolve("25", 0).Should().Be(0);
    }

    [Fact]
    public void BackSolve_clamps_pct_above_100()
    {
        CalibrateDialog.BackSolve("150", 1_000_000).Should().Be(1_000_000);
    }

    [Fact]
    public void ResolveCap_prefers_pct_over_plan_default()
    {
        // /usage entry overrides the plan default — user wanted to fine-tune.
        var cap = CalibrateDialog.ResolveCap("50", currentTokens: 1_000_000, planDefault: 999_999);
        cap.Should().Be(2_000_000);
    }

    [Fact]
    public void ResolveCap_falls_back_to_plan_default_when_pct_blank()
    {
        var cap = CalibrateDialog.ResolveCap("", currentTokens: 1_000_000, planDefault: 5_000_000);
        cap.Should().Be(5_000_000);
    }

    [Fact]
    public void ResolveCap_returns_zero_when_neither_input_is_set()
    {
        // Custom plan + empty pct = no calibration; widget falls back to raw tokens.
        var cap = CalibrateDialog.ResolveCap("", currentTokens: 1_000_000, planDefault: 0);
        cap.Should().Be(0);
    }

    [Theory]
    [InlineData(Plan.Free,    5_000_000L,    80_000_000L)]
    [InlineData(Plan.Pro,    21_000_000L,   380_000_000L)]
    [InlineData(Plan.Max5x, 106_000_000L, 1_900_000_000L)]
    [InlineData(Plan.Max20x, 424_000_000L, 7_600_000_000L)]
    [InlineData(Plan.Team,  106_000_000L, 1_900_000_000L)]
    public void PlanDefaults_returns_known_caps(Plan plan, long expected5h, long expected7d)
    {
        var caps = PlanDefaults.For(plan);
        caps.Tokens5h.Should().Be(expected5h);
        caps.Tokens7d.Should().Be(expected7d);
    }

    [Fact]
    public void PlanDefaults_Custom_returns_zeros()
    {
        var caps = PlanDefaults.For(Plan.Custom);
        caps.Tokens5h.Should().Be(0);
        caps.Tokens7d.Should().Be(0);
    }
}
