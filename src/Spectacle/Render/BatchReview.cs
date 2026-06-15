using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spectacle.Files;

namespace Spectacle.Render;

/// <summary>One spec in a batch review: its path and the verdict <see cref="ReviewReport"/> for it.</summary>
public sealed record BatchReviewEntry(string Path, ReviewReport Report);

/// <summary>
/// The result of reviewing every spec in a folder at once. AI agents routinely emit a
/// whole directory of specs; this turns the per-file <see cref="ReviewReport"/> into a
/// single roll-up so a reviewer or CI step gets one verdict for the set.
/// </summary>
public sealed record BatchReviewResult(IReadOnlyList<BatchReviewEntry> Entries)
{
    public int FileCount => Entries.Count;
    public int FilesWithIssues => Entries.Count(e => e.Report.IssueCount > 0);
    public int TotalIssues => Entries.Sum(e => e.Report.IssueCount);
}

/// <summary>
/// Runs the consolidated <see cref="ReviewReport"/> over many specs. The enumeration and
/// per-file <see cref="ReviewReport.Compute(string, Func{string, bool})"/> are split so the
/// aggregation stays pure (no filesystem) and the directory walk is the only IO surface.
/// </summary>
public static class BatchReview
{
    /// <summary>
    /// Lists the spec files under <paramref name="directory"/> (recursive), keeping only the
    /// extensions Spectacle reviews and returning them in a stable, case-insensitive order so
    /// the batch output is deterministic.
    /// </summary>
    public static IReadOnlyList<string> EnumerateSpecs(string directory) =>
        Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(FileGuard.IsAllowed)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Computes a <see cref="ReviewReport"/> for each supplied spec, preserving input order.
    /// Each spec carries its own <c>TargetExists</c> resolver so relative link/image targets
    /// are validated against that file's own directory.
    /// </summary>
    public static BatchReviewResult Compute(
        IEnumerable<(string Path, string Content, Func<string, bool> TargetExists)> specs) =>
        Compute(specs.Select(s =>
            (s.Path, s.Content, s.TargetExists, (IReadOnlyList<string>)Array.Empty<string>())));

    /// <summary>
    /// As above, but each spec also carries its own required-section template (resolved from
    /// the nearest <c>.spectacle.json</c> by the caller) so a folder review enforces each
    /// spec's enclosing project template. Pass an empty list to enforce none.
    /// </summary>
    public static BatchReviewResult Compute(
        IEnumerable<(string Path, string Content, Func<string, bool> TargetExists, IReadOnlyList<string> RequiredSections)> specs) =>
        new(specs
            .Select(s => new BatchReviewEntry(
                s.Path, ReviewReport.Compute(s.Content, s.TargetExists, s.RequiredSections)))
            .ToList());

    /// <summary>
    /// As above, but each spec also carries its own gating-check selection (the project gate
    /// from its nearest <c>.spectacle.json</c> combined with the global <c>--only</c>/<c>--skip</c>),
    /// so a folder review honours each spec's enclosing project gate.
    /// </summary>
    public static BatchReviewResult Compute(
        IEnumerable<(string Path, string Content, Func<string, bool> TargetExists, IReadOnlyList<string> RequiredSections, ReviewChecks Checks)> specs) =>
        new(specs
            .Select(s => new BatchReviewEntry(
                s.Path, ReviewReport.Compute(s.Content, s.TargetExists, s.RequiredSections, s.Checks)))
            .ToList());
}
