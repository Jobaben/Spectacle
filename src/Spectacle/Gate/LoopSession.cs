using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Spectacle.Render;

namespace Spectacle.Gate;

/// <summary>
/// One pass through the write → gate → revise loop, as the reader saw it: the graded tallies at
/// that moment, what changed in the review since the previous pass, and which rendered blocks the
/// revision touched. <see cref="Delta"/> is <c>null</c> for the first iteration — there is nothing
/// to compare the opening render against.
/// </summary>
public sealed record LoopIteration(
    int Number,
    DateTime At,
    int Blocking,
    int Errors,
    int Warnings,
    int Advisories,
    ReviewDelta? Delta,
    IReadOnlyList<string> ChangedBlockIds);

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

    /// <summary>The recorded iterations, oldest first.</summary>
    public IReadOnlyList<LoopIteration> History => _history;

    /// <summary>The number of the latest iteration, or 0 before the first render.</summary>
    public int CurrentIteration => _history.Count == 0 ? 0 : _history[^1].Number;

    /// <summary>
    /// Records a pass, or returns <c>null</c> when <paramref name="text"/> is byte-identical to the
    /// previous pass (a re-render that is not a revision). The caller supplies the same
    /// <paramref name="report"/> and <paramref name="verdict"/> the render was built from, so the
    /// timeline can never disagree with the badge.
    /// </summary>
    public LoopIteration? Advance(
        string text, ReviewReport report, GateVerdict verdict, IReadOnlyList<TaggedBlock> blocks,
        DateTime atUtc)
    {
        var hash = Sha256Hex(text);
        if (hash == _lastTextHash) return null;

        var isFirst = _lastTextHash is null;
        var delta = _lastReport is null ? null : ReviewDelta.Compute(_lastReport, report);
        // The opening render "changed" every block only in the vacuous sense; flashing the whole
        // document on open would teach the reader to ignore the markers.
        var changed = isFirst ? Array.Empty<string>() : ChangedBlockIds(_lastBlocks, blocks);

        var iteration = new LoopIteration(
            Number: CurrentIteration + 1,
            At: atUtc,
            Blocking: verdict.BlockingCount,
            Errors: verdict.ErrorCount,
            Warnings: verdict.WarningCount,
            Advisories: verdict.InfoCount,
            Delta: delta,
            ChangedBlockIds: changed);

        _history.Add(iteration);
        if (_history.Count > MaxHistory) _history.RemoveAt(0);
        _lastTextHash = hash;
        _lastReport = report;
        _lastBlocks = blocks;
        return iteration;
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
