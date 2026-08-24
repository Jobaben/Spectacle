using System;
using System.Collections.Generic;
using System.Linq;
using Spectacle.Gate;

namespace Spectacle.Ai;

/// <summary>Whether a document carries a usable context capsule.</summary>
public enum ArtifactContextState
{
    /// <summary>No <c>artifact_context</c> namespace — an unmanaged document.</summary>
    Absent,

    /// <summary>A well-formed capsule this session inherits.</summary>
    Present,

    /// <summary>A capsule that is there but structurally broken. Repaired, never discarded.</summary>
    Malformed,
}

/// <summary>
/// What the <c>artifact_context</c> namespace looks like right now: whether it exists, which
/// recognized sections it carries, and what is wrong with it. Read-only by design — the agent
/// writes the capsule, Spectacle reads it and says what it found.
/// </summary>
public sealed record ArtifactContextView(
    ArtifactContextState State,
    IReadOnlyList<string> Sections,
    IReadOnlyList<string> Issues)
{
    /// <summary>The reserved front-matter key holding cross-session state.</summary>
    public const string Key = "artifact_context";

    /// <summary>
    /// The sections a capsule is built from, ordered for reconstruction rather than chronology:
    /// what the artifact is for, what was decided, what bounds it, what is still open, what the
    /// evidence was, and only then how it got here.
    /// </summary>
    public static readonly string[] KnownSections =
        { "purpose", "decisions", "constraints", "unresolved", "evidence", "assumptions", "rejected", "history" };

    /// <summary>A document with no capsule.</summary>
    public static readonly ArtifactContextView None =
        new(ArtifactContextState.Absent, Array.Empty<string>(), Array.Empty<string>());

    /// <summary>
    /// Whether this document is a managed artifact — one whose revision must run under the
    /// project's artifact-context policy. A broken capsule still counts: it is evidence the
    /// document is managed, and losing it is exactly the failure this guards against.
    /// </summary>
    public bool IsManaged => State != ArtifactContextState.Absent;
}

/// <summary>
/// Reads the <c>artifact_context</c> namespace out of a document's front matter.
///
/// It works on the raw header lines rather than on <see cref="FrontMatterEntry"/> values, because
/// the front-matter parser is a deliberate YAML subset with no block-scalar support: a
/// <c>history: &gt;</c> section parses to the literal value <c>"&gt;"</c> with its continuation
/// lines dropped, and a sequence of mappings — the shape <c>decisions</c> uses — parses its second
/// line as a nested key. Teaching the parser those constructs would change what every existing
/// document's metadata card and required-key template see, for no gain here: this reader needs
/// structure, not values.
/// </summary>
public static class ArtifactContext
{
    /// <summary>Inspects <paramref name="documentText"/>'s capsule.</summary>
    public static ArtifactContextView Read(string? documentText)
    {
        var header = FrontMatter.Parse(documentText);
        if (!header.Present) return ArtifactContextView.None;

        var lines = (documentText ?? string.Empty).Split('\n');

        // Header body: everything between the opening fence and the closing one. An unclosed
        // header has no closing fence to stop at, so the whole remainder is scanned and the
        // missing fence is reported as an issue.
        const int from = 1;
        var to = header.Closed ? Math.Min(header.EndLine - 1, lines.Length) : lines.Length;

        var starts = new List<int>();
        for (var i = from; i < to; i++)
        {
            var raw = lines[i].TrimEnd('\r');
            if (raw.Length == 0 || raw[0] == ' ' || raw[0] == '\t' || raw[0] == '#') continue;
            var colon = raw.IndexOf(':');
            if (colon <= 0) continue;
            if (raw[..colon].Trim().Equals(ArtifactContextView.Key, StringComparison.OrdinalIgnoreCase))
                starts.Add(i);
        }

        if (starts.Count == 0) return ArtifactContextView.None;

        var issues = new List<string>();
        if (!header.Closed) issues.Add("the front-matter header is never closed by a --- fence");
        if (starts.Count > 1) issues.Add($"'{ArtifactContextView.Key}' is declared {starts.Count} times");

        var start = starts[0];
        var opening = lines[start].TrimEnd('\r');
        var inline = opening[(opening.IndexOf(':') + 1)..].Trim();
        var children = Children(lines, start, to);

        if (children.Count == 0)
            issues.Add(inline.Length == 0
                ? $"'{ArtifactContextView.Key}' is declared with no context sections beneath it"
                : $"'{ArtifactContextView.Key}' holds a value on its own line where a block mapping of context sections belongs");

        var sections = Sections(children);
        if (children.Count != 0 && sections.Count == 0)
            issues.Add($"'{ArtifactContextView.Key}' has no recognized context section ({string.Join(", ", ArtifactContextView.KnownSections)})");

        return new ArtifactContextView(
            issues.Count == 0 ? ArtifactContextState.Present : ArtifactContextState.Malformed,
            sections,
            issues);
    }

    /// <summary>The indented lines beneath the namespace key, up to the next unindented key.</summary>
    private static List<string> Children(string[] lines, int start, int toExclusive)
    {
        var children = new List<string>();
        for (var i = start + 1; i < toExclusive; i++)
        {
            var raw = lines[i].TrimEnd('\r');
            if (raw.Trim().Length == 0) continue;
            if (raw[0] != ' ' && raw[0] != '\t') break;
            children.Add(raw);
        }
        return children;
    }

    /// <summary>
    /// The recognized section names, taken only from keys at the first child's own indent. That
    /// indent rule is what keeps a block scalar's prose out: a sentence like "investigated three
    /// architectures: A, B and C" sits deeper than the section keys and is never read as one.
    /// </summary>
    private static IReadOnlyList<string> Sections(IReadOnlyList<string> children)
    {
        if (children.Count == 0) return Array.Empty<string>();

        var indent = Indent(children[0]);
        var found = new List<string>();
        foreach (var raw in children)
        {
            if (Indent(raw) != indent) continue;
            var trimmed = raw.Trim();
            if (trimmed[0] == '#' || trimmed.StartsWith("- ", StringComparison.Ordinal)) continue;
            var colon = trimmed.IndexOf(':');
            if (colon <= 0) continue;
            var name = trimmed[..colon].Trim();
            if (ArtifactContextView.KnownSections.Contains(name, StringComparer.OrdinalIgnoreCase)
                && !found.Contains(name, StringComparer.OrdinalIgnoreCase))
                found.Add(name.ToLowerInvariant());
        }
        return found;
    }

    private static int Indent(string raw) => raw.Length - raw.TrimStart(' ', '\t').Length;
}
