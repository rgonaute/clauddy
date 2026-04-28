using Clauddy.Services;
using FluentAssertions;
using Microsoft.Win32;
using Xunit;

namespace Clauddy.Tests;

public class AutoStartServiceTests : IDisposable
{
    private const string TestKey = @"Software\Clauddy.Tests\Run";

    public void Dispose()
    {
        try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Clauddy.Tests"); } catch { }
    }

    [Fact]
    public void Enable_writes_value_under_run_key()
    {
        var svc = new AutoStartService(TestKey, "Clauddy", @"C:\bin\Clauddy.exe");
        svc.Enable();
        using var k = Registry.CurrentUser.OpenSubKey(TestKey);
        k!.GetValue("Clauddy").Should().Be(@"""C:\bin\Clauddy.exe""");
    }

    [Fact]
    public void Disable_removes_value_if_present()
    {
        var svc = new AutoStartService(TestKey, "Clauddy", @"C:\bin\Clauddy.exe");
        svc.Enable();
        svc.Disable();
        using var k = Registry.CurrentUser.OpenSubKey(TestKey);
        k?.GetValue("Clauddy").Should().BeNull();
    }

    [Fact]
    public void IsEnabled_reflects_state()
    {
        var svc = new AutoStartService(TestKey, "Clauddy", @"C:\bin\Clauddy.exe");
        svc.IsEnabled().Should().BeFalse();
        svc.Enable();
        svc.IsEnabled().Should().BeTrue();
    }
}
