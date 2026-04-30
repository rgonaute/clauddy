using Clauddy.Models;
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
        store.Save(new Settings { WindowX = 100, WindowY = 200, RunAtLogin = true,
                                  Quota5hTokens = 440_000, Quota7dTokens = 5_000_000,
                                  Plan = Plan.Team });
        var s = store.Load();
        s.WindowX.Should().Be(100);
        s.WindowY.Should().Be(200);
        s.RunAtLogin.Should().BeTrue();
        s.Quota5hTokens.Should().Be(440_000);
        s.Quota7dTokens.Should().Be(5_000_000);
        s.Plan.Should().Be(Plan.Team);
    }

    [Fact]
    public void Plan_defaults_to_Custom_when_field_missing_from_legacy_file()
    {
        File.WriteAllText(_path, """{"WindowX":100,"WindowY":200,"RunAtLogin":true,"Quota5hTokens":1000}""");
        new SettingsStore(_path).Load().Plan.Should().Be(Plan.Custom);
    }

    [Fact]
    public void Load_returns_defaults_on_corrupt_json()
    {
        File.WriteAllText(_path, "{{ not json");
        new SettingsStore(_path).Load().RunAtLogin.Should().BeFalse();
    }
}
