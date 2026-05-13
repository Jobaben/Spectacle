using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;
using Spectacle.Documents;

namespace Spectacle.Tests;

public class FileDocumentTests : IDisposable
{
    private readonly string _path;

    public FileDocumentTests()
    {
        _path = Path.Combine(Path.GetTempPath(), $"spectacle-{Guid.NewGuid():N}.md");
        File.WriteAllText(_path, "# hello");
    }

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Fact]
    public void Reads_initial_text()
    {
        using var doc = FileDocument.Open(_path);
        doc.Text.Should().Be("# hello");
    }

    [Fact]
    public void BaseDirectory_is_parent_of_file()
    {
        using var doc = FileDocument.Open(_path);
        doc.BaseDirectory.Should().Be(Path.GetDirectoryName(_path));
    }

    [Fact]
    public async Task Changed_fires_on_save()
    {
        using var doc = FileDocument.Open(_path);
        var tcs = new TaskCompletionSource();
        doc.Changed += (_, _) => tcs.TrySetResult();

        await Task.Delay(50); // let the watcher settle
        File.WriteAllText(_path, "# updated");

        var fired = await Task.WhenAny(tcs.Task, Task.Delay(2000));
        fired.Should().Be(tcs.Task, "Changed should fire within 2s of a write");
        doc.Text.Should().Be("# updated");
    }
}
