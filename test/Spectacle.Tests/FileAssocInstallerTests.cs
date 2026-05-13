using Xunit;
using FluentAssertions;
using Microsoft.Win32;
using Spectacle.Install;

namespace Spectacle.Tests;

public class FileAssocInstallerTests : IDisposable
{
    private readonly string _rootKey;

    public FileAssocInstallerTests()
    {
        _rootKey = $"Software\\Classes\\Spectacle.Tests.{Guid.NewGuid():N}";
    }

    public void Dispose()
    {
        try { Registry.CurrentUser.DeleteSubKeyTree(_rootKey, throwOnMissingSubKey: false); }
        catch { /* test cleanup, ignore */ }
    }

    [Fact]
    public void Register_creates_progid_and_extensions()
    {
        var installer = new FileAssocInstaller(@"C:\Tools\Spectacle\Spectacle.exe", _rootKey);

        installer.Register();

        using var prog = Registry.CurrentUser.OpenSubKey($@"{_rootKey}\Spectacle.MarkdownFile\shell\open\command");
        prog.Should().NotBeNull();
        prog!.GetValue(null).Should().Be(@"""C:\Tools\Spectacle\Spectacle.exe"" ""%1""");

        using var md = Registry.CurrentUser.OpenSubKey($@"{_rootKey}\.md");
        md!.GetValue(null).Should().Be("Spectacle.MarkdownFile");

        using var markdown = Registry.CurrentUser.OpenSubKey($@"{_rootKey}\.markdown");
        markdown!.GetValue(null).Should().Be("Spectacle.MarkdownFile");
    }

    [Fact]
    public void Register_is_idempotent()
    {
        var installer = new FileAssocInstaller(@"C:\x\Spectacle.exe", _rootKey);
        installer.Register();
        installer.Register();

        using var md = Registry.CurrentUser.OpenSubKey($@"{_rootKey}\.md");
        md!.GetValue(null).Should().Be("Spectacle.MarkdownFile");
    }

    [Fact]
    public void Unregister_removes_keys()
    {
        var installer = new FileAssocInstaller(@"C:\x\Spectacle.exe", _rootKey);
        installer.Register();
        installer.Unregister();

        Registry.CurrentUser.OpenSubKey($@"{_rootKey}\.md").Should().BeNull();
        Registry.CurrentUser.OpenSubKey($@"{_rootKey}\Spectacle.MarkdownFile").Should().BeNull();
    }

    [Fact]
    public void Unregister_is_idempotent()
    {
        var installer = new FileAssocInstaller(@"C:\x\Spectacle.exe", _rootKey);
        installer.Unregister(); // never registered — should not throw
    }
}
