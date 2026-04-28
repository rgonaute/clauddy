using System.IO;

namespace Clauddy.Services;

public class FirstRunDetector
{
    private readonly string _markerPath;
    public FirstRunDetector(string markerPath) => _markerPath = markerPath;

    public static FirstRunDetector Default() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".clauddy", "installed-hooks.json"));

    public bool IsFirstRun() => !File.Exists(_markerPath);
}
