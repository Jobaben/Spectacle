using System.Collections.Generic;
using System.Linq;

namespace Spectacle.Render;

/// <summary>One required section the spec is missing, carrying the title as it was requested.</summary>
public sealed record MissingSection(string Required);

/// <summary>
/// Template-conformance check: verifies a spec contains every heading a team's spec
/// template requires (e.g. <c>Overview</c>, <c>Acceptance Criteria</c>, <c>Non-Goals</c>).
/// AI agents routinely omit mandatory sections, and none of the other checks notice an
/// absence — they only validate what is present. This closes that gap.
///
/// Matching is by exact heading text, case-insensitive and trimmed, at any level — a
/// required <c>Acceptance Criteria</c> is satisfied by <c>## Acceptance Criteria</c> or
/// <c>#### Acceptance Criteria</c> alike. It is a full-text match, not a substring, so
/// <c>Goals</c> is not satisfied by a <c>Non-Goals</c> heading.
/// </summary>
public static class RequiredSectionsChecker
{
    /// <summary>
    /// Parses the comma-separated required-section spec into distinct, order-preserving
    /// titles. Blank entries (from trailing or doubled commas) are dropped, and entries
    /// that differ only by case or surrounding whitespace are de-duplicated.
    /// </summary>
    public static IReadOnlyList<string> ParseRequired(string? spec)
    {
        var result = new List<string>();
        var seen = new HashSet<string>();
        foreach (var raw in (spec ?? string.Empty).Split(','))
        {
            var name = raw.Trim();
            if (name.Length == 0) continue;
            if (seen.Add(name.ToLowerInvariant())) result.Add(name);
        }
        return result;
    }

    public static IReadOnlyList<MissingSection> Check(string content, IEnumerable<string> required)
    {
        var present = new MdRenderer().Render(content).Outline
            .Select(h => h.Text.Trim().ToLowerInvariant())
            .ToHashSet();

        return required
            .Select(r => r.Trim())
            .Where(name => name.Length != 0 && !present.Contains(name.ToLowerInvariant()))
            .Select(name => new MissingSection(name))
            .ToList();
    }
}
