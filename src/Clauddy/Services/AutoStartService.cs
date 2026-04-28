using Microsoft.Win32;

namespace Clauddy.Services;

public class AutoStartService
{
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly string _subKey;
    private readonly string _name;
    private readonly string _exePath;

    public AutoStartService(string subKey, string name, string exePath)
    {
        _subKey = subKey; _name = name; _exePath = exePath;
    }

    public static AutoStartService Default(string exePath) =>
        new(RunKey, "Clauddy", exePath);

    public void Enable()
    {
        using var k = Registry.CurrentUser.CreateSubKey(_subKey);
        k!.SetValue(_name, $"\"{_exePath}\"");
    }

    public void Disable()
    {
        using var k = Registry.CurrentUser.OpenSubKey(_subKey, writable: true);
        k?.DeleteValue(_name, throwOnMissingValue: false);
    }

    public bool IsEnabled()
    {
        using var k = Registry.CurrentUser.OpenSubKey(_subKey);
        return k?.GetValue(_name) != null;
    }
}
