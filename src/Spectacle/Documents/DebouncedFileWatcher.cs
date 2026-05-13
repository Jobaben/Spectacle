using System;
using System.IO;
using System.Threading;

namespace Spectacle.Documents;

internal sealed class DebouncedFileWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly System.Threading.Timer _timer;
    private readonly Action _onChanged;
    private const int DebounceMs = 150;

    public DebouncedFileWatcher(string fullPath, Action onChanged)
    {
        _onChanged = onChanged;
        var dir = Path.GetDirectoryName(fullPath)!;
        var name = Path.GetFileName(fullPath);
        _watcher = new FileSystemWatcher(dir, name)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        _watcher.Changed += OnRaw;
        _watcher.Created += OnRaw;
        _watcher.Renamed += OnRaw;
        _timer = new System.Threading.Timer(_ => _onChanged(), null, Timeout.Infinite, Timeout.Infinite);
    }

    private void OnRaw(object sender, FileSystemEventArgs e) =>
        _timer.Change(DebounceMs, Timeout.Infinite);

    public void Dispose()
    {
        _watcher.Dispose();
        _timer.Dispose();
    }
}
