using System;
using System.IO;
using Spectacle.Checks;
using Spectacle.Cli;

namespace Spectacle.Gate;

/// <summary>A live grading: the verdict the reader shows and the report it was graded from.</summary>
public sealed record LiveGradeResult(GateVerdict Verdict, ReviewReport Report);

/// <summary>
/// Computes the gate verdict for the document currently open in the reader, using exactly the same
/// checks, project config and grading policy as the <c>--gate</c> command.
///
/// Sharing the computation is the point rather than an economy: a reader that showed its own
/// approximation of the gate would be a second opinion nobody asked for, and the moment the two
/// disagreed the reader's version would be the one people stopped trusting. Because the reader
/// renders a <see cref="GateVerdict"/>, "the badge is green" and "the pipeline will pass" are the
/// same statement.
/// </summary>
public static class LiveGate
{
    /// <summary>
    /// Grades <paramref name="text"/> as if it were the file at
    /// <paramref name="baseDirectory"/>: relative link and image targets resolve against that
    /// directory, and the project config discovered above it supplies the section template, the
    /// front-matter template, the disabled checks and the severity grades.
    ///
    /// Failures are contained: an unreadable config or a filesystem error must degrade the badge,
    /// never take down the reader, so any exception yields an ungraded verdict rather than
    /// propagating into the render.
    /// </summary>
    public static GateVerdict Evaluate(string? text, string baseDirectory, string displayName) =>
        Grade(text, baseDirectory, displayName).Verdict;

    /// <summary>
    /// <see cref="Evaluate"/>, keeping the underlying <see cref="ReviewReport"/> alongside the
    /// verdict — the revision-loop timeline diffs consecutive reports, and recomputing the review
    /// a second time per render just to diff it would double the grading cost for nothing.
    /// </summary>
    public static LiveGradeResult Grade(string? text, string baseDirectory, string displayName)
    {
        var content = text ?? string.Empty;
        try
        {
            var anchor = Path.Combine(baseDirectory, "document.md");
            var config = ConfigLocator.Resolve(anchor, null);

            var report = ReviewReport.Compute(
                content,
                TargetResolver(baseDirectory),
                config.RequiredSections,
                ReviewChecks.Resolve(Array.Empty<string>(), Array.Empty<string>(), config.DisabledChecks),
                config.RequiredFrontMatter);

            var policy = GatePolicy.Create(config.Severity, config.FailOn);
            return new LiveGradeResult(
                GateVerdict.Compute(displayName, report, policy, FrontMatter.Parse(content)), report);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Console.Error.WriteLine($"[LiveGate] Could not grade the document; showing an ungraded verdict: {ex.Message}");
            var report = ReviewReport.Compute(content);
            return new LiveGradeResult(
                GateVerdict.Compute(displayName, report, GatePolicy.Default, FrontMatter.Parse(content)), report);
        }
    }

    // Same rule the CLI applies: a relative target is resolved against the document's own
    // directory and counts as present if a file or a directory is there.
    private static Func<string, bool> TargetResolver(string baseDirectory) => relative =>
    {
        try
        {
            var full = Path.GetFullPath(Path.Combine(baseDirectory, relative));
            return File.Exists(full) || Directory.Exists(full);
        }
        catch
        {
            // A malformed target (illegal path characters) cannot resolve to a file.
            return false;
        }
    };
}
