using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Spectacle.Annotations;

public sealed class AnnotationStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _sourcePath;

    public AnnotationStore(string sourcePath) : this(sourcePath, DefaultSidecarRoot()) { }

    public AnnotationStore(string sourcePath, string sidecarRoot)
    {
        _sourcePath = Path.GetFullPath(sourcePath);
        SidecarDirectory = sidecarRoot;
        SidecarPath = Path.Combine(sidecarRoot, HashPath(_sourcePath) + ".json");
    }

    public string SidecarDirectory { get; }
    public string SidecarPath { get; }

    public AnnotationFile Load()
    {
        if (!File.Exists(SidecarPath))
            return Empty();

        string json;
        try { json = File.ReadAllText(SidecarPath); }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"[AnnotationStore] I/O error reading sidecar {SidecarPath}: {ex.Message}");
            throw;
        }

        try
        {
            var file = JsonSerializer.Deserialize<AnnotationFile>(json, JsonOpts);
            return file ?? Empty();
        }
        catch (JsonException)
        {
            var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var dest = SidecarPath + $".corrupt-{stamp}";
            Console.Error.WriteLine(
                $"[AnnotationStore] Corrupt sidecar at {SidecarPath}; renamed to {dest}");
            try { File.Move(SidecarPath, dest, overwrite: true); }
            catch (IOException) { /* best-effort */ }
            return Empty();
        }
    }

    public void Save(AnnotationFile file)
    {
        Directory.CreateDirectory(SidecarDirectory);
        var tmp = SidecarPath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(file, JsonOpts));
        File.Move(tmp, SidecarPath, overwrite: true);
    }

    private AnnotationFile Empty() =>
        new(FileVersion: 1, SourcePath: _sourcePath, SourceHashAtWrite: string.Empty,
            Comments: Array.Empty<Comment>());

    private static string DefaultSidecarRoot() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Spectacle", "annotations");

    private static string HashPath(string path)
    {
        var key = path.ToLowerInvariant();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
