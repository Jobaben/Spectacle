using System.Linq;
using FluentAssertions;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class StructureCheckerTests
{
    [Fact]
    public void Well_formed_hierarchy_has_no_findings()
    {
        const string content = "# Title\n\n## Section A\n\n### Detail\n\n## Section B\n";

        StructureChecker.Check(content).Should().BeEmpty();
    }

    [Fact]
    public void Flags_skipped_heading_level()
    {
        const string content = "# Title\n\n### Too Deep\n";

        var findings = StructureChecker.Check(content);

        findings.Should().ContainSingle(f => f.Rule == "skipped-level")
            .Which.Line.Should().Be(3);
    }

    [Fact]
    public void Flags_multiple_top_level_headings()
    {
        const string content = "# First\n\n# Second\n";

        StructureChecker.Check(content).Where(f => f.Rule == "multiple-h1")
            .Should().ContainSingle().Which.Line.Should().Be(3);
    }

    [Fact]
    public void Flags_duplicate_heading_text()
    {
        const string content = "# Title\n\n## Setup\n\nText.\n\n## Setup\n\nMore.\n";

        StructureChecker.Check(content).Where(f => f.Rule == "duplicate-heading")
            .Should().ContainSingle().Which.Line.Should().Be(7);
    }

    [Fact]
    public void Does_not_flag_document_that_intentionally_starts_deep()
    {
        // First heading sets the baseline; starting at h2 is allowed.
        const string content = "## Embedded Section\n\n### Detail\n";

        StructureChecker.Check(content).Should().NotContain(f => f.Rule == "skipped-level");
    }

    [Fact]
    public void Findings_are_ordered_by_line()
    {
        const string content = "# A\n\n# B\n\n### C\n";

        var lines = StructureChecker.Check(content).Select(f => f.Line).ToList();

        lines.Should().BeInAscendingOrder();
    }
}
