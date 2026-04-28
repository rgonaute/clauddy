using System.Text.Json;
using Clauddy.Services;
using FluentAssertions;
using Xunit;

namespace Clauddy.Tests;

public class HookInstallerTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), $"clauddy-home-{Guid.NewGuid():N}");
    private readonly string _scriptSource = Path.Combine(Path.GetTempPath(), $"clauddy-scripts-{Guid.NewGuid():N}");

    public HookInstallerTests()
    {
        Directory.CreateDirectory(_scriptSource);
        File.WriteAllText(Path.Combine(_scriptSource, "clauddy-hook.sh"), "#!/usr/bin/env bash\necho hi\n");
        File.WriteAllText(Path.Combine(_scriptSource, "clauddy-hook.ps1"), "Write-Host hi\n");
    }

    public void Dispose()
    {
        try { Directory.Delete(_home, true); } catch { }
        try { Directory.Delete(_scriptSource, true); } catch { }
    }

    [Fact]
    public void Install_copies_hook_scripts_into_clauddy_hooks_dir()
    {
        new HookInstaller(_home, _scriptSource).InstallWindows();
        File.Exists(Path.Combine(_home, ".clauddy", "hooks", "clauddy-hook.sh")).Should().BeTrue();
        File.Exists(Path.Combine(_home, ".clauddy", "hooks", "clauddy-hook.ps1")).Should().BeTrue();
    }

    [Fact]
    public void Install_creates_settings_json_when_absent()
    {
        new HookInstaller(_home, _scriptSource).InstallWindows();
        var path = Path.Combine(_home, ".claude", "settings.json");
        File.Exists(path).Should().BeTrue();
        var json = JsonDocument.Parse(File.ReadAllText(path)).RootElement;
        json.TryGetProperty("hooks", out var hooks).Should().BeTrue();
        hooks.TryGetProperty("SessionStart", out _).Should().BeTrue();
        hooks.TryGetProperty("UserPromptSubmit", out _).Should().BeTrue();
        hooks.TryGetProperty("Stop", out _).Should().BeTrue();
        hooks.TryGetProperty("SubagentStop", out _).Should().BeTrue();
        hooks.TryGetProperty("Notification", out _).Should().BeTrue();
        hooks.TryGetProperty("SessionEnd", out _).Should().BeTrue();
    }

    [Fact]
    public void Install_merges_into_existing_settings_json_preserving_other_keys()
    {
        var path = Path.Combine(_home, ".claude", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{"theme":"dark","permissions":{"allow":["bash"]}}""");
        new HookInstaller(_home, _scriptSource).InstallWindows();
        var json = JsonDocument.Parse(File.ReadAllText(path)).RootElement;
        json.GetProperty("theme").GetString().Should().Be("dark");
        json.GetProperty("permissions").GetProperty("allow")[0].GetString().Should().Be("bash");
        json.TryGetProperty("hooks", out _).Should().BeTrue();
    }

    [Fact]
    public void Install_writes_ledger_with_added_hook_paths()
    {
        new HookInstaller(_home, _scriptSource).InstallWindows();
        var ledgerPath = Path.Combine(_home, ".clauddy", "installed-hooks.json");
        File.Exists(ledgerPath).Should().BeTrue();
        var ledger = JsonDocument.Parse(File.ReadAllText(ledgerPath)).RootElement;
        ledger.TryGetProperty("windows", out var w).Should().BeTrue();
        w.GetProperty("settings_json").GetString().Should().Contain(".claude");
    }

    [Fact]
    public void Uninstall_restores_original_settings_json()
    {
        var path = Path.Combine(_home, ".claude", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{"theme":"dark"}""");

        var inst = new HookInstaller(_home, _scriptSource);
        inst.InstallWindows();
        inst.UninstallWindows();

        var json = JsonDocument.Parse(File.ReadAllText(path)).RootElement;
        json.TryGetProperty("hooks", out _).Should().BeFalse();
        json.GetProperty("theme").GetString().Should().Be("dark");
    }
}
