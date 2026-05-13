using System.IO;

namespace Spectacle.Documents;

public sealed class FileDocument : Document
{
    private readonly string _path;
    private readonly DebouncedFileWatcher _watcher;
    private string _text;

    private FileDocument(string path, string text)
    {
        _path = path;
        _text = text;
        _watcher = new DebouncedFileWatcher(path, ReloadAndNotify);
    }

    public static FileDocument Open(string path)
    {
        var full = Path.GetFullPath(path);
        return new FileDocument(full, ReadAllTextSafely(full));
    }

    public override string Text => _text;
    public override string BaseDirectory => Path.GetDirectoryName(_path)!;

    private void ReloadAndNotify()
    {
        try { _text = ReadAllTextSafely(_path); }
        catch (IOException) { return; } // editor still writing — let next debounce catch it
        OnChanged();
    }

    private static string ReadAllTextSafely(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var sr = new StreamReader(fs);
        return sr.ReadToEnd();
    }

    public override void Dispose()
    {
        _watcher.Dispose();
        base.Dispose();
    }
}
