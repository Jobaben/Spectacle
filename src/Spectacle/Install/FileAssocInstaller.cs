using Microsoft.Win32;

namespace Spectacle.Install;

public sealed class FileAssocInstaller
{
    private const string ProgId = "Spectacle.MarkdownFile";
    private readonly string _exePath;
    private readonly string _rootSubKey;

    public FileAssocInstaller(string exePath)
        : this(exePath, @"Software\Classes") { }

    public FileAssocInstaller(string exePath, string rootSubKey)
    {
        _exePath = exePath;
        _rootSubKey = rootSubKey;
    }

    public void Register()
    {
        using (var prog = Registry.CurrentUser.CreateSubKey($@"{_rootSubKey}\{ProgId}"))
            prog!.SetValue(null, "Markdown Document");

        using (var cmd = Registry.CurrentUser.CreateSubKey(
                   $@"{_rootSubKey}\{ProgId}\shell\open\command"))
            cmd!.SetValue(null, $"\"{_exePath}\" \"%1\"");

        foreach (var ext in new[] { ".md", ".markdown" })
        {
            using var key = Registry.CurrentUser.CreateSubKey($@"{_rootSubKey}\{ext}");
            key!.SetValue(null, ProgId);
        }
    }

    public void Unregister()
    {
        foreach (var ext in new[] { ".md", ".markdown" })
            Registry.CurrentUser.DeleteSubKeyTree($@"{_rootSubKey}\{ext}", throwOnMissingSubKey: false);

        Registry.CurrentUser.DeleteSubKeyTree($@"{_rootSubKey}\{ProgId}", throwOnMissingSubKey: false);
    }
}
