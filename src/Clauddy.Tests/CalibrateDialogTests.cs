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
        // Without a usage history we can't back-solve a cap.
        CalibrateDialog.BackSolve("25", 0).Should().Be(0);
    }

    [Fact]
    public void BackSolve_clamps_pct_above_100()
    {
        // 150% would invent a cap smaller than current usage — clamp to 100.
        CalibrateDialog.BackSolve("150", 1_000_000).Should().Be(1_000_000);
    }
}
