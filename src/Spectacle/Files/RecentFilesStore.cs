using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Spectacle.Files;

/// <summary>
/// A small most-recently-used list of opened documents, persisted as a JSON
/// string array. Entries are stored as absolute paths, newest first, de-duplicated
/// case-insensitively (Windows file system) and capped at <see cref="Capacity"/>.
/// All disk access is best-effort: a corrupt or unreadable store reads back as an
/// empty list rather than throwing, so a bad file can never block opening a document.
/// </summary>
public sealed class RecentFilesStore
{
    public const int DefaultCapacity = 10;

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly string _storePath;

    public RecentFilesStore(string storePath, int capacity = DefaultCapacity)
    {
        _storePath = storePath;
        Capacity = capacity < 1 ? 1 : capacity;
    }

    public int Capacity { get; }

    /// <summary>The per-user store under %APPDATA%\Spectacle\recent.json.</summary>
    public static RecentFilesStore Default() => new(DefaultStorePath());

    public static string DefaultStorePath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Spectacle",
        "recent.json");

    /// <summary>The recent list, newest first. Never throws — returns empty on any read error.</summary>
    public IReadOnlyList<string> Load()
    {
        try
        {
            if (!File.Exists(_storePath)) return Array.Empty<string>();
            var json = File.ReadAllText(_storePath);
            var items = JsonSerializer.Deserialize<string[]>(json);
            if (items is null) return Array.Empty<string>();
            return items.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>Like <see cref="Load"/> but drops entries whose file no longer exists.</summary>
    public IReadOnlyList<string> LoadExisting() => Load().Where(File.Exists).ToList();

    /// <summary>
    /// Records <paramref name="filePath"/> as the most recent entry: normalizes it to an
    /// absolute path, removes any prior occurrence (case-insensitive), moves it to the
    /// front and trims to <see cref="Capacity"/>. Best-effort; write errors are swallowed.
    /// </summary>
    public void Add(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return;

        string full;
        try { full = Path.GetFullPath(filePath); }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException) { return; }

        var next = new List<string> { full };
        next.AddRange(Load().Where(p => !string.Equals(p, full, StringComparison.OrdinalIgnoreCase)));
        if (next.Count > Capacity) next.RemoveRange(Capacity, next.Count - Capacity);

        Save(next);
    }

    public void Clear() => Save(Array.Empty<string>());

    private void Save(IReadOnlyList<string> items)
    {
        try
        {
            var dir = Path.GetDirectoryName(_storePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(_storePath, JsonSerializer.Serialize(items, JsonOpts));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort: a failed write must never interrupt the user.
        }
    }
}
