using Clauddy.Logging;
using FluentAssertions;
using Xunit;

namespace Clauddy.Tests;

public class FileLoggerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"clauddy-log-{Guid.NewGuid():N}");
    public FileLoggerTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void Log_writes_line_with_timestamp_and_level()
    {
        var path = Path.Combine(_dir, "log.txt");
        new FileLogger(path, maxBytes: 1024 * 1024, keep: 3).Info("hello world");
        var line = File.ReadAllText(path);
        line.Should().Contain("INFO").And.Contain("hello world");
    }

    [Fact]
    public void Logger_rotates_when_size_exceeds_max_bytes()
    {
        var path = Path.Combine(_dir, "log.txt");
        var log = new FileLogger(path, maxBytes: 200, keep: 3);
        for (int i = 0; i < 50; i++) log.Info(new string('x', 50));
        Directory.GetFiles(_dir, "log*.txt").Length.Should().BeGreaterOrEqualTo(2);
    }

    [Fact]
    public void Logger_keeps_at_most_N_rotated_files()
    {
        var path = Path.Combine(_dir, "log.txt");
        var log = new FileLogger(path, maxBytes: 100, keep: 2);
        for (int i = 0; i < 200; i++) log.Info(new string('x', 50));
        Directory.GetFiles(_dir, "log*.txt").Length.Should().BeLessOrEqualTo(3);
    }
}
