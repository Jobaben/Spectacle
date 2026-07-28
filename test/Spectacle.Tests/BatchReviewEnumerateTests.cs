using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

/// <summary>Filesystem coverage for the directory walk that backs <c>--review &lt;dir&gt;</c>.</summary>
public class BatchReviewEnumerateTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "spectacle-batch-" + Guid.NewGuid().ToString("N"));

    public BatchReviewEnumerateTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    private void Write(string relative, string content = "# x\n")
    {
        var full = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    [Fact]
    public void Finds_specs_recursively_and_ignores_non_specs()
    {
        Write("top.md");
        Write("sub/nested.markdown");
        Write("sub/notes.txt");
        Write("readme.rst");

        var specs = BatchReview.EnumerateSpecs(_root);

        specs.Select(Path.GetFileName).Should().BeEquivalentTo(new[] { "top.md", "nested.markdown" });
    }

    [Fact]
    public void Returns_specs_in_stable_order()
    {
        Write("b.md");
        Write("a.md");
        Write("c.md");

        var specs = BatchReview.EnumerateSpecs(_root).Select(Path.GetFileName).ToList();

        specs.Should().Equal("a.md", "b.md", "c.md");
    }

    [Fact]
    public void Empty_folder_yields_no_specs()
    {
        BatchReview.EnumerateSpecs(_root).Should().BeEmpty();
    }
}
