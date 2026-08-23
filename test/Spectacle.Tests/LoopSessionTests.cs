using System;
using System.Linq;
using FluentAssertions;
using Spectacle.Gate;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

/// <summary>
/// The reader's revision-session memory: iterations advance only on real text changes, deltas are
/// computed between consecutive reports, and changed-block detection flags exactly the blocks a
/// revision touched.
/// </summary>
public class LoopSessionTests
{
    private static readonly MdRenderer Renderer = new();
    private static readonly DateTime T0 = new(2026, 8, 23, 12, 0, 0, DateTimeKind.Utc);

    private static LoopIteration? Advance(LoopSession session, string text, DateTime at)
    {
        var report = ReviewReport.Compute(text);
        var verdict = GateVerdict.Compute("doc.md", report, GatePolicy.Default, FrontMatter.Parse(text));
        var blocks = Renderer.Render(text).Blocks;
        return session.Advance(text, report, verdict, blocks, at);
    }

    [Fact]
    public void First_advance_is_iteration_one_with_no_delta_and_no_changed_blocks()
    {
        var session = new LoopSession();

        var it = Advance(session, "# Title\n\nA paragraph with a TODO placeholder.\n", T0);

        it.Should().NotBeNull();
        it!.Number.Should().Be(1);
        it.Delta.Should().BeNull("there is nothing to compare the opening render against");
        it.ChangedBlockIds.Should().BeEmpty("flashing every block on open would be noise");
        session.CurrentIteration.Should().Be(1);
    }

    [Fact]
    public void Rerendering_identical_text_does_not_advance()
    {
        var session = new LoopSession();
        const string text = "# Title\n\nBody.\n";

        Advance(session, text, T0);
        var again = Advance(session, text, T0.AddMinutes(1));

        again.Should().BeNull("a theme flip or comment save re-renders the same text");
        session.CurrentIteration.Should().Be(1);
        session.History.Should().HaveCount(1);
    }

    [Fact]
    public void A_revision_advances_and_carries_the_review_delta()
    {
        var session = new LoopSession();
        Advance(session, "# Title\n\nStill TODO.\n", T0);

        var it = Advance(session, "# Title\n\nAll done here.\n", T0.AddMinutes(2));

        it.Should().NotBeNull();
        it!.Number.Should().Be(2);
        it.At.Should().Be(T0.AddMinutes(2));
        it.Delta.Should().NotBeNull();
        it.Delta!.Fixed.Should().Contain(f => f.Rule == "placeholder", "the TODO was removed");
        it.Delta.New.Should().BeEmpty();
    }

    [Fact]
    public void Iteration_counts_mirror_the_verdict()
    {
        var session = new LoopSession();
        var text = "# Title\n\nA TODO left behind.\n";
        var report = ReviewReport.Compute(text);
        var verdict = GateVerdict.Compute("doc.md", report, GatePolicy.Default, FrontMatter.Parse(text));

        var it = session.Advance(text, report, verdict, Renderer.Render(text).Blocks, T0);

        it!.Blocking.Should().Be(verdict.BlockingCount);
        it.Errors.Should().Be(verdict.ErrorCount);
        it.Warnings.Should().Be(verdict.WarningCount);
        it.Advisories.Should().Be(verdict.InfoCount);
    }

    [Fact]
    public void Changed_blocks_flag_only_what_the_revision_touched()
    {
        var session = new LoopSession();
        Advance(session, "# Title\n\nFirst paragraph.\n\nSecond paragraph.\n", T0);

        var it = Advance(session, "# Title\n\nFirst paragraph, edited.\n\nSecond paragraph.\n", T0.AddMinutes(1));

        var blocks = Renderer.Render("# Title\n\nFirst paragraph, edited.\n\nSecond paragraph.\n").Blocks;
        var editedId = blocks.Single(b => b.OriginalText.Contains("edited")).BlockId;
        it!.ChangedBlockIds.Should().ContainSingle().Which.Should().Be(editedId);
    }

    [Fact]
    public void A_duplicated_block_flags_exactly_the_surplus_occurrence()
    {
        var session = new LoopSession();
        Advance(session, "# Title\n\nRepeated paragraph.\n", T0);

        var revised = "# Title\n\nRepeated paragraph.\n\nRepeated paragraph.\n";
        var it = Advance(session, revised, T0.AddMinutes(1));

        var blocks = Renderer.Render(revised).Blocks;
        var surplus = blocks.Single(b => b.OriginalText.Contains("Repeated") && b.OccurrenceIndex == 1);
        it!.ChangedBlockIds.Should().ContainSingle().Which.Should().Be(surplus.BlockId);
    }

    [Fact]
    public void History_is_capped_but_numbering_is_preserved()
    {
        var session = new LoopSession();
        for (var i = 0; i < LoopSession.MaxHistory + 5; i++)
            Advance(session, $"# Title\n\nRevision {i}.\n", T0.AddSeconds(i));

        session.History.Should().HaveCount(LoopSession.MaxHistory);
        session.CurrentIteration.Should().Be(LoopSession.MaxHistory + 5);
        session.History[0].Number.Should().Be(6, "the oldest iterations are dropped, not renumbered");
    }
}
