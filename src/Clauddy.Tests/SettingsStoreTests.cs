using Clauddy.Services;
using FluentAssertions;
using Xunit;

namespace Clauddy.Tests;

public class SettingsStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"clauddy-settings-{Guid.NewGuid():N}.json");
    public void Dispose() { try { File.Delete(_path); } catch { } }

    [Fact]
    public void Load_returns_defaults_when_missing()
    {
        var s = new SettingsStore(_path).Load();
        s.WindowX.Should().BeNull();
        s.WindowY.Should().BeNull();
        s.RunAtLogin.Should().BeFalse();
    }

    [Fact]
    public void Save_then_load_roundtrips()
    {
        var store = new SettingsStore(_path);
        store.Save(new Settings { WindowX = 100, WindowY = 200, RunAtLogin = true });
        var s = store.Load();
        s.WindowX.Should().Be(100);
        s.WindowY.Should().Be(200);
        s.RunAtLogin.Should().BeTrue();
    }

    [Fact]
    public void Load_returns_defaults_on_corrupt_json()
    {
        File.WriteAllText(_path, "{{ not json");
        new SettingsStore(_path).Load().RunAtLogin.Should().BeFalse();
    }
}
