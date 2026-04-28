using System.IO;

namespace Clauddy.Services;

public class EndpointFile
{
    private readonly string _path;
    public EndpointFile(string path) => _path = path;

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".clauddy", "endpoint");

    public void Write(string url)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, url);
    }

    public void Delete() { try { File.Delete(_path); } catch { } }
}
