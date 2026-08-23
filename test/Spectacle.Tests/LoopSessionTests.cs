using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Spectacle.Annotations;
using Spectacle.Gate;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

/// <summary>
/// The reader's revision-session memory: iterations advance only on real text changes, deltas are
/// computed between consecutive reports, changed-block detection flags exactly the blocks a
/// revision touched, and the reviewer's comments are credited to the save that acted on them.
/// </summary>
public class LoopSessionTests
{
    private static readonly MdRenderer Renderer = new();
    private static readonly DateTime T0 = new(2026, 8, 23, 12, 0, 0, DateTimeKind.Utc);

    private static LoopIteration? Advance(
        LoopSession session, string text, DateTime at, IReadOnlyList<Comment>? comments = null)
    {
        var report = ReviewReport.Compute(text);
        var verdict = GateVerdict.Compute("doc.md", report, GatePolicy.Default, FrontMatter.Parse(text));
        var blocks = Renderer.Render(text).Blocks;
        var matched = AnnotationMatcher.Match(blocks, comments ?? Array.Empty<Comment>()).Matched;
        return session.Advance(text, report, verdict, blocks, matched, at);
    }

    /// <summary>A comment anchored to the block of <paramref name="text"/> containing <paramref name="fragment"/>.</summary>
    private static Comment CommentOn(
        string text, string fragment, string id, string body, DateTime? resolvedAt = null)
    {
        var block = Renderer.Render(text).Blocks.Single(b => b.OriginalText.Contains(fragment));
        var anchor = new BlockAnchor(
            block.Kind, block.Line, block.TextHash, block.OccurrenceIndex,
            block.OriginalText.Split('\n')[0]);
        return new Comment(id, anchor, block.OriginalText, body, T0, resolvedAt);
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
        it.CommentsAddressed.Should().BeEmpty("opening a document addresses nothing");
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

        var it = session.Advance(
            text, report, verdict, Renderer.Render(text).Blocks, Array.Empty<MatchedComment>(), T0);

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
    public void A_revision_that_changes_a_commented_block_addresses_the_comment()
    {
        var session = new LoopSession();
        var v1 = "# Title\n\nFirst paragraph.\n\nSecond paragraph.\n";
        var comment = CommentOn(v1, "First", "c-1", "Tighten this paragraph.");
        Advance(session, v1, T0, new[] { comment });

        var v2 = "# Title\n\nFirst paragraph, tightened.\n\nSecond paragraph.\n";
        var it = Advance(session, v2, T0.AddMinutes(1), new[] { comment });

        var addressed = it!.CommentsAddressed.Should().ContainSingle().Subject;
        addressed.Id.Should().Be("c-1");
        addressed.Body.Should().Be("Tighten this paragraph.");
        addressed.Context.Should().Be("First paragraph.");
        it.CommentsOpen.Should().Be(0, "the comment lost its anchor to this save");
    }

    [Fact]
    public void An_untouched_comment_stays_open_and_is_not_addressed()
    {
        var session = new LoopSession();
        var v1 = "# Title\n\nFirst paragraph.\n\nSecond paragraph.\n";
        var comment = CommentOn(v1, "Second", "c-1", "Name the failure modes.");
        Advance(session, v1, T0, new[] { comment });

        var v2 = "# Title\n\nFirst paragraph, edited.\n\nSecond paragraph.\n";
        var it = Advance(session, v2, T0.AddMinutes(1), new[] { comment });

        it!.CommentsAddressed.Should().BeEmpty("the save never touched the commented block");
        it.CommentsOpen.Should().Be(1);
    }

    [Fact]
    public void A_comment_resolved_between_saves_is_the_reviewers_work_not_the_next_saves()
    {
        var session = new LoopSession();
        var v1 = "# Title\n\nFirst paragraph.\n\nSecond paragraph.\n";
        var open = CommentOn(v1, "First", "c-1", "Tighten this paragraph.");
        Advance(session, v1, T0, new[] { open });

        // The reviewer resolves the comment: a re-render of the same text, not an iteration —
        // but the comment baseline must move with it.
        var resolved = open with { ResolvedAt = T0.AddMinutes(1) };
        Advance(session, v1, T0.AddMinutes(1), new[] { resolved }).Should().BeNull();

        var v2 = "# Title\n\nFirst paragraph, rewritten anyway.\n\nSecond paragraph.\n";
        var it = Advance(session, v2, T0.AddMinutes(2), new[] { resolved });

        it!.CommentsAddressed.Should().BeEmpty(
            "a comment the reviewer already resolved is work already signed off, not this save's fix");
    }

    [Fact]
    public void A_comment_added_between_saves_is_credited_to_the_save_that_acts_on_it()
    {
        var session = new LoopSession();
        var v1 = "# Title\n\nFirst paragraph.\n\nSecond paragraph.\n";
        Advance(session, v1, T0);

        // The reviewer comments after the opening render: same text, no iteration — but the
        // baseline picks the comment up so the next save can be measured against it.
        var comment = CommentOn(v1, "First", "c-1", "Tighten this paragraph.");
        Advance(session, v1, T0.AddMinutes(1), new[] { comment }).Should().BeNull();

        var v2 = "# Title\n\nFirst paragraph, tightened.\n\nSecond paragraph.\n";
        var it = Advance(session, v2, T0.AddMinutes(2), new[] { comment });

        it!.CommentsAddressed.Should().ContainSingle().Which.Id.Should().Be("c-1");
    }

    [Fact]
    public void A_comment_added_between_saves_lands_on_the_latest_iteration()
    {
        var session = new LoopSession();
        var v1 = "# Title\n\nFirst paragraph.\n\nSecond paragraph.\n";
        Advance(session, v1, T0);
        session.History[^1].CommentsOpen.Should().Be(0);

        var a = CommentOn(v1, "First", "c-1", "Tighten this paragraph.");
        var b = CommentOn(v1, "Second", "c-2", "Name the failure modes.");
        Advance(session, v1, T0.AddMinutes(1), new[] { a, b }).Should().BeNull(
            "a comment save is not a revision");

        session.History[^1].CommentsOpen.Should().Be(2,
            "the reviewer's open asks are the state the reader is looking at, so the timeline " +
            "peaks where the comments were written rather than rising on the next save");
    }

    [Fact]
    public void Open_comments_step_down_as_the_saves_answer_them()
    {
        var session = new LoopSession();
        var v1 = "# Title\n\nFirst paragraph.\n\nSecond paragraph.\n";
        Advance(session, v1, T0);

        var a = CommentOn(v1, "First", "c-1", "Tighten this paragraph.");
        var b = CommentOn(v1, "Second", "c-2", "Name the failure modes.");
        Advance(session, v1, T0.AddMinutes(1), new[] { a, b });

        var v2 = "# Title\n\nFirst paragraph, tightened.\n\nSecond paragraph.\n";
        Advance(session, v2, T0.AddMinutes(2), new[] { a, b });

        var v3 = "# Title\n\nFirst paragraph, tightened.\n\nSecond paragraph, with failure modes.\n";
        Advance(session, v3, T0.AddMinutes(3), new[] { a, b });

        session.History.Select(i => i.CommentsOpen).Should().Equal(new[] { 2, 1, 0 },
            "each save that answers an ask drops the bar, the way the gate tallies do");
    }

    [Fact]
    public void A_comment_resolved_between_saves_drops_off_the_latest_iteration()
    {
        var session = new LoopSession();
        var v1 = "# Title\n\nFirst paragraph.\n\nSecond paragraph.\n";
        var comment = CommentOn(v1, "First", "c-1", "Tighten this paragraph.");
        Advance(session, v1, T0, new[] { comment });
        session.History[^1].CommentsOpen.Should().Be(1);

        var resolved = comment with { ResolvedAt = T0.AddMinutes(1) };
        Advance(session, v1, T0.AddMinutes(1), new[] { resolved }).Should().BeNull();

        session.History[^1].CommentsOpen.Should().Be(0,
            "the reviewer signing an ask off is the ask closing, not the next save's work");
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
