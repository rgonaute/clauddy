using Clauddy.Services;
using FluentAssertions;
using Xunit;

namespace Clauddy.Tests;

public class EndpointFileTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "clauddy-tests-" + Guid.NewGuid().ToString("N"));

    public EndpointFileTests() => Directory.CreateDirectory(_tempDir);
    public void Dispose() { try { Directory.Delete(_tempDir, true); } catch { } }

    [Fact]
    public void Write_creates_directory_and_file()
    {
        var f = new EndpointFile(Path.Combine(_tempDir, ".clauddy", "endpoint"));
        f.Write("http://127.0.0.1:51797");
        File.Exists(Path.Combine(_tempDir, ".clauddy", "endpoint")).Should().BeTrue();
        File.ReadAllText(Path.Combine(_tempDir, ".clauddy", "endpoint")).Should().Be("http://127.0.0.1:51797");
    }

    [Fact]
    public void Write_overwrites_existing_file()
    {
        var path = Path.Combine(_tempDir, "endpoint");
        File.WriteAllText(path, "stale");
        new EndpointFile(path).Write("fresh");
        File.ReadAllText(path).Should().Be("fresh");
    }

    [Fact]
    public void Delete_removes_file_if_present()
    {
        var path = Path.Combine(_tempDir, "endpoint");
        File.WriteAllText(path, "x");
        new EndpointFile(path).Delete();
        File.Exists(path).Should().BeFalse();
    }

    [Fact]
    public void Delete_silent_if_missing()
    {
        Action act = () => new EndpointFile(Path.Combine(_tempDir, "nope")).Delete();
        act.Should().NotThrow();
    }
}
