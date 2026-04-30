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
        s.CurrentBucketTokens.Should().Be(0);
        s.Last7DaysTokens.Should().Be(0);
        s.CurrentBucketCacheRate.Should().Be(0);
    }

    [Fact]
    public void Sums_tokens_in_active_bucket()
    {
        // Two messages 1h apart, both within the past 5h — single active bucket.
        var now = DateTimeOffset.Parse("2026-04-29T12:00:00Z");
        WriteTranscript("a.jsonl",
            Line("2026-04-29T11:00:00Z", input: 1000, output: 500),
            Line("2026-04-29T10:00:00Z", input: 100, output: 50));
        var s = new UsageStatsService(_dir).Compute(now);
        s.CurrentBucketTokens.Should().Be(1650);
    }

    [Fact]
    public void Bucket_resets_after_long_idle_gap()
    {
        // 6h gap between consecutive messages → only the more recent one is in the
        // current bucket. The older one belonged to a previous (now-expired) bucket.
        var now = DateTimeOffset.Parse("2026-04-29T12:00:00Z");
        WriteTranscript("a.jsonl",
            Line("2026-04-29T11:00:00Z", input: 100, output: 200),     // current bucket
            Line("2026-04-29T05:00:00Z", input: 9999, output: 9999));  // previous bucket
        var s = new UsageStatsService(_dir).Compute(now);
        s.CurrentBucketTokens.Should().Be(300);
    }

    [Fact]
    public void Bucket_includes_messages_older_than_5h_when_activity_is_continuous()
    {
        // No gaps >5h between consecutive messages, total span ≤5h: single bucket.
        var now = DateTimeOffset.Parse("2026-04-29T12:00:00Z");
        WriteTranscript("a.jsonl",
            Line("2026-04-29T07:30:00Z", input: 100, output: 100),  // 4.5h ago → bucket start
            Line("2026-04-29T09:00:00Z", input: 100, output: 100),  // 3h ago
            Line("2026-04-29T11:30:00Z", input: 100, output: 100)); // 30m ago
        var s = new UsageStatsService(_dir).Compute(now);
        s.CurrentBucketTokens.Should().Be(600);
    }

    [Fact]
    public void Bucket_resets_when_continuous_activity_spans_more_than_5h()
    {
        // Anthropic resets at bucket_start + 5h regardless of activity. So a continuous
        // 6h run should produce two buckets — only the later one counts.
        var now = DateTimeOffset.Parse("2026-04-29T12:00:00Z");
        WriteTranscript("a.jsonl",
            Line("2026-04-29T06:00:00Z", input: 100, output: 100),  // bucket A start
            Line("2026-04-29T08:00:00Z", input: 100, output: 100),  // bucket A
            Line("2026-04-29T11:30:00Z", input: 200, output: 300)); // span >5h → bucket B start
        var s = new UsageStatsService(_dir).Compute(now);
        s.CurrentBucketTokens.Should().Be(500);
    }

    [Fact]
    public void Bucket_is_empty_when_now_is_past_bucket_expiry()
    {
        // Last message is 6h ago; the bucket that contained it expired at bucket_start+5h.
        // Anthropic's /usage would show 0% in this state; match it.
        var now = DateTimeOffset.Parse("2026-04-29T12:00:00Z");
        WriteTranscript("a.jsonl",
            Line("2026-04-29T06:00:00Z", input: 9999, output: 9999));
        var s = new UsageStatsService(_dir).Compute(now);
        s.CurrentBucketTokens.Should().Be(0);
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
    public void Includes_cache_tokens_in_bucket_total()
    {
        var now = DateTimeOffset.Parse("2026-04-29T12:00:00Z");
        WriteTranscript("a.jsonl",
            Line("2026-04-29T11:00:00Z", input: 100, output: 200, cacheRead: 5000, cacheCreate: 50));
        var s = new UsageStatsService(_dir).Compute(now);
        s.CurrentBucketTokens.Should().Be(100 + 200 + 5000 + 50);
    }

    [Fact]
    public void Computes_cache_hit_rate_for_current_bucket()
    {
        var now = DateTimeOffset.Parse("2026-04-29T12:00:00Z");
        WriteTranscript("a.jsonl",
            Line("2026-04-29T11:00:00Z", input: 100, output: 999, cacheRead: 800, cacheCreate: 100));
        // input side = 100 + 100 + 800 = 1000 ; hit = 800/1000 = 0.8
        var s = new UsageStatsService(_dir).Compute(now);
        s.CurrentBucketCacheRate.Should().BeApproximately(0.8, 0.001);
    }

    [Fact]
    public void Cache_rate_is_zero_when_no_input()
    {
        var s = new UsageStatsService(_dir).Compute(DateTimeOffset.UtcNow);
        s.CurrentBucketCacheRate.Should().Be(0);
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
        s.CurrentBucketTokens.Should().Be(600);
    }

    [Fact]
    public void Skips_user_role_lines()
    {
        var now = DateTimeOffset.Parse("2026-04-29T12:00:00Z");
        WriteTranscript("a.jsonl",
            """{"type":"user","timestamp":"2026-04-29T11:00:00Z","message":{"role":"user","content":"hi"}}""",
            Line("2026-04-29T11:30:00Z", 100, 200));
        var s = new UsageStatsService(_dir).Compute(now);
        s.CurrentBucketTokens.Should().Be(300);
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
        s.CurrentBucketTokens.Should().Be(200);
    }
}
