using System.Linq;
using FluentAssertions;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class LinkCheckerTests
{
    [Fact]
    public void Anchor_to_existing_heading_resolves()
    {
        // Markdig auto-id turns "Section One" into "section-one".
        const string content = "# Section One\n\nSee [the section](#section-one).\n";

        LinkChecker.Check(content).Should().BeEmpty();
    }

    [Fact]
    public void Anchor_to_missing_heading_is_flagged()
    {
        const string content = "# Section One\n\nSee [other](#does-not-exist).\n";

        var findings = LinkChecker.Check(content);

        findings.Should().ContainSingle();
        findings[0].Target.Should().Be("#does-not-exist");
        findings[0].Line.Should().Be(3);
    }

    [Fact]
    public void Empty_link_target_is_flagged()
    {
        const string content = "A [dangling link]() here.\n";

        LinkChecker.Check(content).Should().ContainSingle()
            .Which.Target.Should().Be("");
    }

    [Fact]
    public void External_and_relative_links_are_not_checked()
    {
        const string content =
            "[ext](https://example.com) and [rel](./other.md) and [mail](mailto:x@y.z)\n";

        LinkChecker.Check(content).Should().BeEmpty();
    }

    [Fact]
    public void Image_anchors_are_ignored()
    {
        const string content = "![diagram](#missing)\n";

        LinkChecker.Check(content).Should().BeEmpty();
    }

    [Fact]
    public void Explicit_heading_id_resolves()
    {
        // GenericAttributes lets a heading declare its own id.
        const string content = "## Custom Title {#custom-id}\n\n[jump](#custom-id)\n";

        LinkChecker.Check(content).Should().BeEmpty();
    }

    [Fact]
    public void Multiple_broken_anchors_are_all_reported_in_order()
    {
        const string content = "# H\n\n[a](#x)\n\n[b](#y)\n";

        var lines = LinkChecker.Check(content).Select(f => f.Line).ToList();

        lines.Should().Equal(3, 5);
    }
}
