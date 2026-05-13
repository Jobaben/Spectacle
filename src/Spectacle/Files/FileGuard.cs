namespace Spectacle.Files;

public static class FileGuard
{
    private static readonly string[] Allowed = { ".md", ".markdown" };

    public static bool IsAllowed(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var ext = System.IO.Path.GetExtension(path);
        return Allowed.Any(a => string.Equals(a, ext, StringComparison.OrdinalIgnoreCase));
    }
}
