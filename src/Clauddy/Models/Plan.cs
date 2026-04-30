namespace Clauddy.Models;

/// <summary>
/// Anthropic subscription tier. Drives default cap suggestions in the Calibrate dialog
/// when the user doesn't want to hand-enter /usage percentages. Anthropic doesn't publish
/// exact token caps for these tiers, so the defaults are best-effort approximations
/// derived from a Team-plan calibration on 2026-04-30 (36%/77% against ~38M/1.47B local
/// tokens) and scaled by the marketed message-tier ratios. Users wanting precision
/// should still recalibrate via /usage.
/// </summary>
public enum Plan { Custom, Free, Pro, Max5x, Max20x, Team }

public static class PlanDefaults
{
    public record Caps(long Tokens5h, long Tokens7d);

    public static Caps For(Plan plan) => plan switch
    {
        Plan.Free   => new(    5_000_000,        80_000_000),
        Plan.Pro    => new(   21_000_000,       380_000_000),
        Plan.Max5x  => new(  106_000_000,     1_900_000_000),
        Plan.Max20x => new(  424_000_000,     7_600_000_000),
        Plan.Team   => new(  106_000_000,     1_900_000_000),
        _           => new(0, 0)  // Custom — no plan-derived defaults; rely on /usage entry
    };

    public static string DisplayName(Plan plan) => plan switch
    {
        Plan.Custom => "Custom (calibrate via /usage only)",
        Plan.Free   => "Free",
        Plan.Pro    => "Pro",
        Plan.Max5x  => "Max 5×",
        Plan.Max20x => "Max 20×",
        Plan.Team   => "Team",
        _           => plan.ToString()
    };
}
