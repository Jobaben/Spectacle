using System.Linq;
using FluentAssertions;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class SpecDiffTests
{
    [Fact]
    public void Identical_documents_have_no_changes()
    {
        const string doc = "# Title\n\nA paragraph.\n\n## Section\n\nMore.\n";

        var diff = SpecDiff.Compare(doc, doc);

        diff.Added.Should().BeEmpty();
        diff.Removed.Should().BeEmpty();
    }

    [Fact]
    public void Added_block_appears_in_added_only()
    {
        const string baseDoc = "# Title\n\nFirst paragraph.\n";
        const string revised = "# Title\n\nFirst paragraph.\n\nA brand new paragraph.\n";

        var diff = SpecDiff.Compare(baseDoc, revised);

        diff.Removed.Should().BeEmpty();
        diff.Added.Should().ContainSingle()
            .Which.Text.Should().Contain("brand new paragraph");
    }

    [Fact]
    public void Removed_block_appears_in_removed_only()
    {
        const string baseDoc = "# Title\n\nKeep me.\n\nDelete me.\n";
        const string revised = "# Title\n\nKeep me.\n";

        var diff = SpecDiff.Compare(baseDoc, revised);

        diff.Added.Should().BeEmpty();
        diff.Removed.Should().ContainSingle()
            .Which.Text.Should().Contain("Delete me");
    }

    [Fact]
    public void Edited_block_shows_as_one_removed_and_one_added()
    {
        const string baseDoc = "# Title\n\nThe value is 100ms.\n";
        const string revised = "# Title\n\nThe value is 250ms.\n";

        var diff = SpecDiff.Compare(baseDoc, revised);

        diff.Added.Should().ContainSingle().Which.Text.Should().Contain("250ms");
        diff.Removed.Should().ContainSingle().Which.Text.Should().Contain("100ms");
    }

    [Fact]
    public void Entries_carry_kind_and_line()
    {
        const string baseDoc = "# Title\n";
        const string revised = "# Title\n\n## New Section\n";

        var added = SpecDiff.Compare(baseDoc, revised).Added;

        added.Should().ContainSingle(e => e.Kind == "heading");
        added.Single(e => e.Kind == "heading").Line.Should().Be(3);
    }
}
