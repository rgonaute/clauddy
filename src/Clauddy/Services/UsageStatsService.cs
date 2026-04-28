using System.IO;
using System.Text.Json;

namespace Clauddy.Services;

public record UsageStats(
    long Last5HoursTokens,
    long Last7DaysTokens,
    double Last5HoursCacheRate);

/// <summary>
/// Computes /usage-style metrics by walking all *.jsonl transcripts under a root.
/// Pure side-effect-free read of the local filesystem; safe to call on a timer.
/// </summary>
public class UsageStatsService
{
    private readonly string _root;

    public UsageStatsService(string transcriptsRoot) => _root = transcriptsRoot;

    public static UsageStatsService Default() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude", "projects"));

    public UsageStats Compute(DateTimeOffset now)
    {
        if (!Directory.Exists(_root)) return new UsageStats(0, 0, 0);

        long input5h = 0, output5h = 0, cacheRead5h = 0, cacheCreate5h = 0;
        long total7d = 0;
        var fiveHoursAgo = now - TimeSpan.FromHours(5);
        var sevenDaysAgo = now - TimeSpan.FromDays(7);

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
                if (ts >= fiveHoursAgo)
                {
                    input5h += i; output5h += o; cacheRead5h += cr; cacheCreate5h += cc;
                }
            }
        }

        long inputSide = input5h + cacheRead5h + cacheCreate5h;
        double cacheRate = inputSide == 0 ? 0 : (double)cacheRead5h / inputSide;
        long total5h = inputSide + output5h;
        return new UsageStats(total5h, total7d, cacheRate);
    }

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
