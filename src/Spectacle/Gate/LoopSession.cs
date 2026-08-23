using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Spectacle.Annotations;
using Spectacle.Render;

namespace Spectacle.Gate;

/// <summary>
/// A reviewer comment a revision acted on: it was unresolved and anchored before the save, and the
/// save changed (or removed) the block it pointed at. <see cref="Line"/> is where the block sat in
/// the text the comment was written against — the best landing the reader has, since the block
/// itself no longer exists in that form. <see cref="Context"/> is the anchor's leading text, the
/// same snippet the orphan tray uses to say what a comment was about.
/// </summary>
public sealed record AddressedComment(string Id, string Body, string Context, int Line);

/// <summary>
/// One pass through the write → gate → revise loop, as the reader saw it: the graded tallies at
/// that moment, what changed in the review since the previous pass, which rendered blocks the
/// revision touched, and which of the reviewer's comment blocks it acted on. <see cref="Delta"/>
/// is <c>null</c> for the first iteration — there is nothing to compare the opening render
/// against. <see cref="CommentsOpen"/> counts the unresolved, still-anchored comments after this
/// pass — the same set the comment brief is built from, so the HUD and the brief can never
/// disagree about what is still being asked. It is a live tally on the latest iteration: comments
/// the reviewer adds or resolves between saves land on that iteration rather than waiting for the
/// next one.
/// </summary>
public sealed record LoopIteration(
    int Number,
    DateTime At,
    int Blocking,
    int Errors,
    int Warnings,
    int Advisories,
    ReviewDelta? Delta,
    IReadOnlyList<string> ChangedBlockIds,
    IReadOnlyList<AddressedComment> CommentsAddressed,
    int CommentsOpen);

/// <summary>
/// The reader's memory of a revision session. The preview already re-renders and re-grades on
/// every save, but each render used to replace the last one wholesale — open a document, let an
/// agent rewrite it four times, and the reader could say what the document *is* but nothing about
/// where it had *been*. This type keeps that history: every time the document's text actually
/// changes, the session records a new iteration carrying the gate tallies, the review delta
/// against the previous text (what the revision fixed, what it introduced), and the ids of the
/// rendered blocks the revision touched.
///
/// Only text changes advance the session. A theme flip or a comment save re-renders the same
/// text, and counting those as iterations would make the timeline lie about how many passes the
/// author took — so <see cref="Advance"/> hashes the text and returns <c>null</c> when it has not
/// moved.
/// </summary>
public sealed class LoopSession
{
    /// <summary>
    /// Iterations kept in full. Beyond this the oldest are dropped (numbering is preserved), so a
    /// reader left open against a very chatty workflow cannot grow without bound.
    /// </summary>
    public const int MaxHistory = 200;

    private readonly List<LoopIteration> _history = new();
    private string? _lastTextHash;
    private ReviewReport? _lastReport;
    private IReadOnlyList<TaggedBlock> _lastBlocks = Array.Empty<TaggedBlock>();
    private IReadOnlyList<MatchedComment> _lastUnresolved = Array.Empty<MatchedComment>();

    /// <summary>The recorded iterations, oldest first.</summary>
    public IReadOnlyList<LoopIteration> History => _history;

    /// <summary>The number of the latest iteration, or 0 before the first render.</summary>
    public int CurrentIteration => _history.Count == 0 ? 0 : _history[^1].Number;

    /// <summary>
    /// Records a pass, or returns <c>null</c> when <paramref name="text"/> is byte-identical to the
    /// previous pass (a re-render that is not a revision). The caller supplies the same
    /// <paramref name="report"/> and <paramref name="verdict"/> the render was built from, so the
    /// timeline can never disagree with the badge — and the same <paramref name="matched"/>
    /// comments the cards render from, so the timeline can never disagree with the comment brief.
    /// </summary>
    public LoopIteration? Advance(
        string text, ReviewReport report, GateVerdict verdict, IReadOnlyList<TaggedBlock> blocks,
        IReadOnlyList<MatchedComment> matched, DateTime atUtc)
    {
        var unresolvedNow = matched.Where(m => m.Comment.ResolvedAt is null).ToList();
        var hash = Sha256Hex(text);
        if (hash != _lastTextHash) return AdvanceChanged(hash, report, verdict, blocks, unresolvedNow, atUtc);

        // A re-render that is not a revision (a theme flip, a comment save or resolve) still
        // refreshes the comment baseline: a comment the reviewer resolved between saves is the
        // reviewer's work, and a comment added between saves is the next save's to address —
        // neither may be credited to (or hidden from) the iteration the next save records.
        _lastUnresolved = unresolvedNow;
        // The open-comment count is a live tally, not a delta: the latest iteration is the state
        // the reader is looking at, so a comment added or resolved between saves belongs on that
        // iteration's bar immediately. Without this the timeline would start flat at zero and
        // rise on the first save after a comment — the reviewer's asks would look like work the
        // saves created instead of work waiting to be answered, and no amount of answering them
        // could draw the descending step the gate tallies draw. It stays a re-render: no new
        // iteration, no delta, no toast.
        if (_history.Count > 0)
            _history[^1] = _history[^1] with { CommentsOpen = unresolvedNow.Count };
        return null;
    }

    private LoopIteration AdvanceChanged(
        string hash, ReviewReport report, GateVerdict verdict, IReadOnlyList<TaggedBlock> blocks,
        IReadOnlyList<MatchedComment> unresolvedNow, DateTime atUtc)
    {
        var isFirst = _lastTextHash is null;
        var delta = _lastReport is null ? null : ReviewDelta.Compute(_lastReport, report);
        // The opening render "changed" every block only in the vacuous sense; flashing the whole
        // document on open would teach the reader to ignore the markers.
        var changed = isFirst ? Array.Empty<string>() : ChangedBlockIds(_lastBlocks, blocks);
        // An unresolved comment counts as addressed when the save left it without its block: the
        // anchor is (kind, text-hash, occurrence), so a comment that dropped out of the
        // unresolved-anchored set had its block changed or removed by this exact revision. That is
        // the same signal that orphans it in the tray — the loop just says *which save* did it.
        var addressed = isFirst
            ? Array.Empty<AddressedComment>()
            : AddressedComments(_lastUnresolved, unresolvedNow);

        var iteration = new LoopIteration(
            Number: CurrentIteration + 1,
            At: atUtc,
            Blocking: verdict.BlockingCount,
            Errors: verdict.ErrorCount,
            Warnings: verdict.WarningCount,
            Advisories: verdict.InfoCount,
            Delta: delta,
            ChangedBlockIds: changed,
            CommentsAddressed: addressed,
            CommentsOpen: unresolvedNow.Count);

        _history.Add(iteration);
        if (_history.Count > MaxHistory) _history.RemoveAt(0);
        _lastTextHash = hash;
        _lastReport = report;
        _lastBlocks = blocks;
        _lastUnresolved = unresolvedNow;
        return iteration;
    }

    private static IReadOnlyList<AddressedComment> AddressedComments(
        IReadOnlyList<MatchedComment> previous, IReadOnlyList<MatchedComment> current)
    {
        var stillOpen = new HashSet<string>(current.Select(m => m.Comment.Id), StringComparer.Ordinal);
        return previous
            .Where(m => !stillOpen.Contains(m.Comment.Id))
            .Select(m => new AddressedComment(
                Id: m.Comment.Id,
                Body: m.Comment.Body,
                Context: m.Comment.BlockAnchor.LeadingText,
                // The block's line in the previous render — where the comment's target sat when
                // the reviewer could last see it, and the closest landing the new text offers.
                Line: m.CurrentBlock.Line))
            .ToList();
    }

    /// <summary>
    /// The blocks of the new render that the revision touched. A block is unchanged when the
    /// previous render carried a block of the same kind with the same normalized-text hash; the
    /// tagger's occurrence index makes that a multiset comparison, so two identical paragraphs
    /// where there used to be one flags exactly the surplus one.
    /// </summary>
    private static IReadOnlyList<string> ChangedBlockIds(
        IReadOnlyList<TaggedBlock> previous, IReadOnlyList<TaggedBlock> current)
    {
        var budget = new Dictionary<(string Kind, string Hash), int>();
        foreach (var b in previous)
        {
            var key = (b.Kind, b.TextHash);
            budget[key] = budget.GetValueOrDefault(key) + 1;
        }

        return current
            .Where(b => b.OccurrenceIndex >= budget.GetValueOrDefault((b.Kind, b.TextHash)))
            .Select(b => b.BlockId)
            .ToList();
    }

    private static string Sha256Hex(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
