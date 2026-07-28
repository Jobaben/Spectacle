using System.Text.Json;
using FluentAssertions;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class SpecDiffExporterTests
{
    private static readonly DiffResult Result = new(
        Added: new[] { new DiffEntry("paragraph", 5, "A new paragraph.") },
        Removed: new[] { new DiffEntry("heading", 2, "## Old Section") });

    [Fact]
    public void Text_shows_added_and_removed_with_markers()
    {
        var text = SpecDiffExporter.Build(Result, @"C:\path\spec.md", @"C:\path\old.md", json: false);

        text.Should().Contain("spec.md");
        text.Should().Contain("old.md");
        text.Should().Contain("+");
        text.Should().Contain("A new paragraph.");
        text.Should().Contain("-");
        text.Should().Contain("Old Section");
    }

    [Fact]
    public void Json_emits_added_and_removed_arrays()
    {
        var json = SpecDiffExporter.Build(Result, @"C:\path\spec.md", @"C:\path\old.md", json: true);

        var root = JsonDocument.Parse(json).RootElement;
        root.GetProperty("source").GetString().Should().Be(@"C:\path\spec.md");
        root.GetProperty("comparedTo").GetString().Should().Be(@"C:\path\old.md");
        root.GetProperty("addedCount").GetInt32().Should().Be(1);
        root.GetProperty("removedCount").GetInt32().Should().Be(1);
        root.GetProperty("added")[0].GetProperty("text").GetString().Should().Be("A new paragraph.");
        root.GetProperty("removed")[0].GetProperty("kind").GetString().Should().Be("heading");
    }
}
