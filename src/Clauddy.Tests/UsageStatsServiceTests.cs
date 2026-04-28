using Clauddy.Services;
using FluentAssertions;
using Xunit;

namespace Clauddy.Tests;

public class UsageStatsServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"clauddy-usage-{Guid.NewGuid():N}");

    public UsageStatsServiceTests() => Directory.CreateDirectory(_dir);

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private void WriteTranscript(string name, params string[] lines)
    {
        var dir = Path.Combine(_dir, "project1");
        Directory.CreateDirectory(dir);
        File.WriteAllLines(Path.Combine(dir, name), lines);
    }

    private static string Line(string ts, long input, long output, long cacheRead = 0, long cacheCreate = 0)
        => "{\"type\":\"assistant\",\"timestamp\":\"" + ts + "\",\"message\":{\"role\":\"assistant\",\"usage\":{"
           + "\"input_tokens\":" + input + ",\"output_tokens\":" + output
           + ",\"cache_read_input_tokens\":" + cacheRead + ",\"cache_creation_input_tokens\":" + cacheCreate
           + "}}}";

    [Fact]
    public void Returns_zeros_when_directory_missing()
    {
        var svc = new UsageStatsService(Path.Combine(_dir, "nope"));
        var s = svc.Compute(DateTimeOffset.UtcNow);
        s.Last5HoursTokens.Should().Be(0);
        s.Last7DaysTokens.Should().Be(0);
        s.Last5HoursCacheRate.Should().Be(0);
    }

    [Fact]
    public void Sums_tokens_inside_5_hour_window()
    {
        var now = DateTimeOffset.Parse("2026-04-29T12:00:00Z");
        WriteTranscript("a.jsonl",
            Line("2026-04-29T11:00:00Z", input: 1000, output: 500),     // in 5h
            Line("2026-04-29T08:00:00Z", input: 100, output: 50));      // outside 5h (4h ago is in, 4h+1m is out — wait 8:00 is 4h before 12:00, in window; let me adjust)
        var s = new UsageStatsService(_dir).Compute(now);
        s.Last5HoursTokens.Should().Be(1000 + 500 + 100 + 50);  // both within 5h
    }

    [Fact]
    public void Excludes_tokens_older_than_5_hours()
    {
        var now = DateTimeOffset.Parse("2026-04-29T12:00:00Z");
        WriteTranscript("a.jsonl",
            Line("2026-04-29T11:00:00Z", input: 1000, output: 500),    // 1h ago — in
            Line("2026-04-29T06:00:00Z", input: 9999, output: 9999));  // 6h ago — out
        var s = new UsageStatsService(_dir).Compute(now);
        s.Last5HoursTokens.Should().Be(1500);
    }

    [Fact]
    public void Sums_tokens_inside_7_day_window()
    {
        var now = DateTimeOffset.Parse("2026-04-29T12:00:00Z");
        WriteTranscript("a.jsonl",
            Line("2026-04-26T12:00:00Z", input: 1000, output: 0),  // 3 days ago — in
            Line("2026-04-21T12:00:00Z", input: 9999, output: 0)); // 8 days ago — out
        var s = new UsageStatsService(_dir).Compute(now);
        s.Last7DaysTokens.Should().Be(1000);
    }

    [Fact]
    public void Includes_cache_tokens_in_total()
    {
        var now = DateTimeOffset.Parse("2026-04-29T12:00:00Z");
        WriteTranscript("a.jsonl",
            Line("2026-04-29T11:00:00Z", input: 100, output: 200, cacheRead: 5000, cacheCreate: 50));
        var s = new UsageStatsService(_dir).Compute(now);
        s.Last5HoursTokens.Should().Be(100 + 200 + 5000 + 50);
    }

    [Fact]
    public void Computes_cache_hit_rate_for_5h_window()
    {
        var now = DateTimeOffset.Parse("2026-04-29T12:00:00Z");
        // input+cache_create+cache_read = total INPUT side; cache_read / that = hit rate
        WriteTranscript("a.jsonl",
            Line("2026-04-29T11:00:00Z", input: 100, output: 999, cacheRead: 800, cacheCreate: 100));
        // input side = 100 + 100 + 800 = 1000 ; hit = 800/1000 = 0.8
        var s = new UsageStatsService(_dir).Compute(now);
        s.Last5HoursCacheRate.Should().BeApproximately(0.8, 0.001);
    }

    [Fact]
    public void Cache_rate_is_zero_when_no_input()
    {
        var s = new UsageStatsService(_dir).Compute(DateTimeOffset.UtcNow);
        s.Last5HoursCacheRate.Should().Be(0);
    }

    [Fact]
    public void Walks_all_jsonl_files_in_subdirs()
    {
        var now = DateTimeOffset.Parse("2026-04-29T12:00:00Z");
        var p1 = Path.Combine(_dir, "p1"); var p2 = Path.Combine(_dir, "p2");
        Directory.CreateDirectory(p1); Directory.CreateDirectory(p2);
        File.WriteAllLines(Path.Combine(p1, "a.jsonl"), new[] { Line("2026-04-29T11:00:00Z", 100, 100) });
        File.WriteAllLines(Path.Combine(p2, "b.jsonl"), new[] { Line("2026-04-29T11:00:00Z", 200, 200) });
        var s = new UsageStatsService(_dir).Compute(now);
        s.Last5HoursTokens.Should().Be(600);
    }

    [Fact]
    public void Skips_user_role_lines()
    {
        var now = DateTimeOffset.Parse("2026-04-29T12:00:00Z");
        WriteTranscript("a.jsonl",
            """{"type":"user","timestamp":"2026-04-29T11:00:00Z","message":{"role":"user","content":"hi"}}""",
            Line("2026-04-29T11:30:00Z", 100, 200));
        var s = new UsageStatsService(_dir).Compute(now);
        s.Last5HoursTokens.Should().Be(300);
    }

    [Fact]
    public void Skips_malformed_lines_without_throwing()
    {
        var now = DateTimeOffset.Parse("2026-04-29T12:00:00Z");
        WriteTranscript("a.jsonl",
            "not-json",
            "{}",
            "{\"timestamp\":\"bad-date\"}",
            Line("2026-04-29T11:00:00Z", 100, 100));
        var s = new UsageStatsService(_dir).Compute(now);
        s.Last5HoursTokens.Should().Be(200);
    }
}
