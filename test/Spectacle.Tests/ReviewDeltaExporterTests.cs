using System.Text.Json;
using FluentAssertions;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class ReviewDeltaExporterTests
{
    private static ReviewDelta Sample()
    {
        const string baseline = "# Title\n\n### Too Deep\n\nTODO finish.\n";
        const string revised = "# Title\n\n### Too Deep\n\nSee [x](#missing).\n";
        return ReviewDelta.Compute(ReviewReport.Compute(baseline), ReviewReport.Compute(revised));
    }

    [Fact]
    public void Text_summarizes_fixed_new_persisting()
    {
        var text = ReviewDeltaExporter.Build(Sample(), @"C:\path\spec.md", @"C:\path\old.md", json: false);

        text.Should().Contain("spec.md");
        text.Should().Contain("old.md");
        text.Should().Contain("fixed");
        text.Should().Contain("new");
        text.Should().Contain("persisting");
    }

    [Fact]
    public void Json_emits_classified_arrays_and_checklist()
    {
        var json = ReviewDeltaExporter.Build(Sample(), @"C:\path\spec.md", @"C:\path\old.md", json: true);

        var root = JsonDocument.Parse(json).RootElement;
        root.GetProperty("source").GetString().Should().Be(@"C:\path\spec.md");
        root.GetProperty("baseline").GetString().Should().Be(@"C:\path\old.md");
        root.GetProperty("fixed").GetArrayLength().Should().BeGreaterThan(0);
        root.GetProperty("new").GetArrayLength().Should().BeGreaterThan(0);
        root.GetProperty("persisting").GetArrayLength().Should().BeGreaterThan(0);
        root.GetProperty("new")[0].GetProperty("category").GetString().Should().Be("links");
        root.GetProperty("checklist").GetProperty("revised").GetProperty("total").GetInt32().Should().Be(0);
    }
}
