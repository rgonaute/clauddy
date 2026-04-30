using System.IO;
using System.Text.Json;

namespace Clauddy.Services;

public record UsageStats(
    long CurrentBucketTokens,
    long Last7DaysTokens,
    double CurrentBucketCacheRate);

/// <summary>
/// Computes /usage-style metrics by walking all *.jsonl transcripts under a root.
/// Pure side-effect-free read of the local filesystem; safe to call on a timer.
///
/// The 5-hour metric is a "session bucket" not a rolling window: a bucket starts
/// after a ≥5h gap in activity (or after the previous bucket has spanned 5h of
/// continuous activity), and expires 5h after its start. This matches Anthropic's
/// /usage reset semantics far better than a strict last-5h sum, which kept old
/// tokens visible long after Anthropic had silently reset them.
/// </summary>
public class UsageStatsService
{
    private readonly string _root;
    private static readonly TimeSpan BucketLength = TimeSpan.FromHours(5);

    public UsageStatsService(string transcriptsRoot) => _root = transcriptsRoot;

    public static UsageStatsService Default() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude", "projects"));

    public UsageStats Compute(DateTimeOffset now)
    {
        if (!Directory.Exists(_root)) return new UsageStats(0, 0, 0);

        long total7d = 0;
        var sevenDaysAgo = now - TimeSpan.FromDays(7);
        var msgs = new List<UsageRow>();

        foreach (var file in Directory.EnumerateFiles(_root, "*.jsonl", SearchOption.AllDirectories))
        {
            // Skip files that haven't been touched in 7 days — irrelevant to either window.
            try { if (File.GetLastWriteTimeUtc(file) < sevenDaysAgo.UtcDateTime) continue; }
            catch { continue; }

            foreach (var line in EnumerateLines(file))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (!TryParseAssistantUsage(line, out var ts, out var i, out var o, out var cr, out var cc)) continue;

                if (ts >= sevenDaysAgo) total7d += i + o + cr + cc;
                msgs.Add(new UsageRow(ts, i, o, cr, cc));
            }
        }

        var bucket = CurrentBucket(msgs, now);
        long inputSide = bucket.Input + bucket.CacheRead + bucket.CacheCreate;
        double cacheRate = inputSide == 0 ? 0 : (double)bucket.CacheRead / inputSide;
        long bucketTotal = inputSide + bucket.Output;
        return new UsageStats(bucketTotal, total7d, cacheRate);
    }

    /// <summary>
    /// Sums tokens for the currently-active 5-hour bucket. A new bucket starts whenever
    /// either condition is met: (a) a ≥5h gap between consecutive messages, or (b) the
    /// span from the bucket's first message exceeds 5h. After identifying the latest
    /// bucket, an empty result is returned if "now" is past that bucket's expiry —
    /// matches Anthropic's behavior of fresh-bucket display once the window has elapsed.
    /// </summary>
    internal static (long Input, long Output, long CacheRead, long CacheCreate) CurrentBucket(
        List<UsageRow> messages, DateTimeOffset now)
    {
        if (messages.Count == 0) return (0, 0, 0, 0);

        // Sort ascending so we can walk forward and detect bucket boundaries.
        messages.Sort((a, b) => a.Ts.CompareTo(b.Ts));

        int bucketStartIdx = 0;
        var bucketStartTs = messages[0].Ts;
        for (int k = 1; k < messages.Count; k++)
        {
            var gap = messages[k].Ts - messages[k - 1].Ts;
            var spanFromStart = messages[k].Ts - bucketStartTs;
            if (gap > BucketLength || spanFromStart > BucketLength)
            {
                bucketStartIdx = k;
                bucketStartTs = messages[k].Ts;
            }
        }

        // Bucket has expired (Anthropic resets exactly bucket_start + 5h).
        if (now - bucketStartTs > BucketLength) return (0, 0, 0, 0);

        long input = 0, output = 0, cacheRead = 0, cacheCreate = 0;
        for (int k = bucketStartIdx; k < messages.Count; k++)
        {
            input += messages[k].Input;
            output += messages[k].Output;
            cacheRead += messages[k].CacheRead;
            cacheCreate += messages[k].CacheCreate;
        }
        return (input, output, cacheRead, cacheCreate);
    }

    internal record UsageRow(DateTimeOffset Ts, long Input, long Output, long CacheRead, long CacheCreate);

    private static IEnumerable<string> EnumerateLines(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        string? line;
        while ((line = reader.ReadLine()) != null) yield return line;
    }

    private static bool TryParseAssistantUsage(string line, out DateTimeOffset ts,
        out long input, out long output, out long cacheRead, out long cacheCreate)
    {
        ts = default; input = 0; output = 0; cacheRead = 0; cacheCreate = 0;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var r = doc.RootElement;
            if (!r.TryGetProperty("timestamp", out var tsEl)) return false;
            if (!DateTimeOffset.TryParse(tsEl.GetString(), out ts)) return false;
            if (!r.TryGetProperty("message", out var msg)) return false;
            if (!msg.TryGetProperty("role", out var role) || role.GetString() != "assistant") return false;
            if (!msg.TryGetProperty("usage", out var usage)) return false;
            input = ReadLong(usage, "input_tokens");
            output = ReadLong(usage, "output_tokens");
            cacheRead = ReadLong(usage, "cache_read_input_tokens");
            cacheCreate = ReadLong(usage, "cache_creation_input_tokens");
            return true;
        }
        catch { return false; }
    }

    private static long ReadLong(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;
}
