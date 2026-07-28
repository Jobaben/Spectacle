using System.Collections.Generic;
using System.Linq;

namespace Spectacle.Render;

/// <summary>One heading-hierarchy issue, with its 1-based line.</summary>
public sealed record StructureFinding(string Rule, int Line, string Message);

/// <summary>
/// Checks the heading hierarchy of a spec — distinct from <c>SpecLinter</c>'s
/// content checks. Three rules: <c>multiple-h1</c> (more than one top-level
/// heading), <c>skipped-level</c> (a heading that jumps more than one level
/// deeper than the previous one, e.g. h1→h3) and <c>duplicate-heading</c>
/// (identical heading text, which also yields ambiguous anchor slugs).
/// </summary>
public static class StructureChecker
{
    public static IReadOnlyList<StructureFinding> Check(string content)
    {
        var headings = new MdRenderer().Render(content).Outline;
        var findings = new List<StructureFinding>();

        var firstH1Seen = false;
        int? previousLevel = null;
        var seenText = new HashSet<string>();

        foreach (var h in headings)
        {
            if (h.Level == 1)
            {
                if (firstH1Seen)
                    findings.Add(new StructureFinding("multiple-h1", h.Line, "more than one top-level (h1) heading"));
                firstH1Seen = true;
            }

            if (previousLevel is int prev && h.Level > prev + 1)
                findings.Add(new StructureFinding(
                    "skipped-level", h.Line, $"heading jumps from level {prev} to level {h.Level}"));
            previousLevel = h.Level;

            var key = h.Text.Trim().ToLowerInvariant();
            if (!seenText.Add(key))
                findings.Add(new StructureFinding("duplicate-heading", h.Line, $"duplicate heading '{h.Text}'"));
        }

        return findings.OrderBy(f => f.Line).ToList();
    }
}
