using System.IO;

namespace Spectacle.Cli;

/// <summary>
/// Finds and loads the project's <c>.spectacle.json</c>. Discovery walks up from the
/// spec's own directory to the filesystem root and takes the first config it finds —
/// the same "nearest config wins" rule editors and linters use — so a spec inherits the
/// settings of the closest enclosing project without anyone passing a path.
/// </summary>
public static class ConfigLocator
{
    public const string FileName = ".spectacle.json";

    /// <summary>
    /// Returns the path of the nearest <c>.spectacle.json</c> at or above
    /// <paramref name="startDirectory"/>, or <c>null</c> if none exists up to the root.
    /// </summary>
    public static string? Find(string startDirectory)
    {
        DirectoryInfo? dir;
        try { dir = new DirectoryInfo(Path.GetFullPath(startDirectory)); }
        catch { return null; }

        for (; dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, FileName);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>
    /// Loads and parses the config at <paramref name="path"/>. An unreadable file yields
    /// the empty config — a broken or absent config must never crash a headless check.
    /// </summary>
    public static SpectacleConfig Load(string path)
    {
        try { return SpectacleConfig.Parse(File.ReadAllText(path)); }
        catch (IOException) { return SpectacleConfig.Empty; }
        catch (UnauthorizedAccessException) { return SpectacleConfig.Empty; }
    }

    /// <summary>
    /// Resolves required sections for a spec: an explicit <c>--config=&lt;path&gt;</c>
    /// wins; otherwise the nearest discovered <c>.spectacle.json</c> is used. Returns the
    /// empty config when neither resolves.
    /// </summary>
    public static SpectacleConfig Resolve(string specPath, string? explicitConfigPath)
    {
        if (explicitConfigPath is not null) return Load(explicitConfigPath);

        var dir = Path.GetDirectoryName(Path.GetFullPath(specPath));
        if (dir is null) return SpectacleConfig.Empty;

        var found = Find(dir);
        return found is null ? SpectacleConfig.Empty : Load(found);
    }
}
