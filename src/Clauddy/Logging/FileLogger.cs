using System.IO;

namespace Clauddy.Logging;

public class FileLogger
{
    private readonly string _path;
    private readonly long _maxBytes;
    private readonly int _keep;
    private readonly object _lock = new();

    public FileLogger(string path, long maxBytes, int keep)
    {
        _path = path; _maxBytes = maxBytes; _keep = keep;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
    }

    public static FileLogger Default()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Clauddy", "log.txt");
        return new FileLogger(path, 1024 * 1024, 3);
    }

    public void Info(string msg) => Write("INFO", msg);
    public void Warn(string msg) => Write("WARN", msg);
    public void Error(string msg) => Write("ERROR", msg);

    private void Write(string level, string msg)
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(_path) && new FileInfo(_path).Length > _maxBytes) Rotate();
                File.AppendAllText(_path,
                    $"{DateTimeOffset.Now:yyyy-MM-ddTHH:mm:ss.fffzzz} {level} {msg}{Environment.NewLine}");
            }
            catch { /* never let logging crash the app */ }
        }
    }

    private void Rotate()
    {
        var dir = Path.GetDirectoryName(_path)!;
        var stem = Path.GetFileNameWithoutExtension(_path);
        for (int i = _keep; i >= 1; i--)
        {
            var src = Path.Combine(dir, $"{stem}.{i - 1}.txt");
            var dst = Path.Combine(dir, $"{stem}.{i}.txt");
            if (i == 1) src = _path;
            if (File.Exists(src))
            {
                if (File.Exists(dst)) File.Delete(dst);
                File.Move(src, dst);
            }
        }
        var orphan = Path.Combine(dir, $"{stem}.{_keep + 1}.txt");
        if (File.Exists(orphan)) File.Delete(orphan);
    }
}
