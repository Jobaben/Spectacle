using System;
using System.Collections.Generic;
using FluentAssertions;
using Spectacle.Annotations;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

/// <summary>
/// The revision brief built from the reviewer's unresolved comments — the collapsed-panel twin of
/// the fix brief. What matters: the agent-addressed contract is stated, the revisions are ordered
/// bottom-up so applying one never moves the next one's block, and each instruction travels with
/// its block quoted verbatim.
/// </summary>
public class CommentBriefExporterTests
{
    private static MatchedComment At(int line, string kind, string original, string body)
    {
        var comment = new Comment(
            Id: $"c-{line}",
            BlockAnchor: new BlockAnchor(kind, line, $"h{line}", 0, original),
            OriginalText: original,
            Body: body,
            CreatedAt: DateTime.UtcNow,
            ResolvedAt: null);
        var block = new TaggedBlock($"b{line}", kind, line, $"h{line}", 0, original);
        return new MatchedComment(comment, block);
    }

    [Fact]
    public void The_brief_is_addressed_to_the_authoring_agent_with_an_explicit_contract()
    {
        var brief = CommentBriefExporter.Build(@"C:\specs\design.md", new List<MatchedComment>
        {
            At(12, "paragraph", "The capture flow is simple.", "Name the failure modes."),
        });

        brief.Should().StartWith("# Revision brief — design.md (reviewer comments)");
        brief.Should().Contain("1 unresolved comment on `design.md`");
        brief.Should().Contain("## How to apply this brief");
        brief.Should().Contain("Change only the blocks quoted below.");
        brief.Should().Contain("skip that revision and report it");
        brief.Should().Contain("Do not add a changelog");
        brief.Should().Contain("## Revisions (1)");
    }

    [Fact]
    public void Each_revision_quotes_its_block_verbatim_and_carries_the_comment_body()
    {
        var brief = CommentBriefExporter.Build("design.md", new List<MatchedComment>
        {
            At(8, "paragraph", "First line of the block.\nSecond line of the block.", "Tighten this."),
        });

        brief.Should().Contain("### 1. Line 8 — paragraph");
        brief.Should().Contain("> First line of the block.");
        brief.Should().Contain("> Second line of the block.");
        brief.Should().Contain("**Do this:**");
        brief.Should().Contain("Tighten this.");
    }

    [Fact]
    public void Revisions_are_ordered_bottom_up_whatever_order_the_comments_arrive_in()
    {
        var brief = CommentBriefExporter.Build("design.md", new List<MatchedComment>
        {
            At(5, "heading", "Overview", "Rename this section."),
            At(40, "paragraph", "Captures expire.", "State the expiry window."),
            At(18, "paragraph", "The ledger is updated.", "Which ledger?"),
        });

        var first = brief.IndexOf("Line 40", StringComparison.Ordinal);
        var second = brief.IndexOf("Line 18", StringComparison.Ordinal);
        var third = brief.IndexOf("Line 5 ", StringComparison.Ordinal);
        first.Should().BePositive();
        second.Should().BeGreaterThan(first, "line 18 must come after line 40");
        third.Should().BeGreaterThan(second, "line 5 must come last");
        brief.Should().Contain("## Revisions (3)");
        brief.Should().Contain("3 unresolved comments");
    }

    [Fact]
    public void An_empty_list_yields_an_explicit_leave_it_unchanged_brief()
    {
        var brief = CommentBriefExporter.Build("design.md", Array.Empty<MatchedComment>());

        brief.Should().Contain("## Revisions (0)");
        brief.Should().Contain("No unresolved comments. Leave the document unchanged.");
    }
}
