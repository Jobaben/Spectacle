using System;
using System.Collections.Generic;
using System.Linq;

namespace Spectacle.Render;

/// <summary>
/// The set of gating checks <c>--review</c> should run for a spec. The aggregate verdict is
/// otherwise all-or-nothing; this lets a team turn off a check it finds noisy (a project gate
/// declared once in <c>.spectacle.json</c>) or pick a subset for a single run (<c>--only</c> /
/// <c>--skip</c>), the same tuning every linter offers. A disabled check is recorded in
/// <see cref="Disabled"/> so the verdict can say a check was *off* rather than silently passing.
/// </summary>
public sealed class ReviewChecks
{
    /// <summary>
    /// The canonical check ids, in report order. These are the same stable ids the SARIF rule
    /// catalogue and the baseline-delta categories use, so an id names one thing everywhere.
    /// </summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        "lint", "structure", "links", "tables", "fences", "paths",
        "duplication", "alt-text", "link-text", "emphasis-heading", "sections", "toc",
    };

    /// <summary>Every check enabled — the default verdict, unchanged from before this feature.</summary>
    public static readonly ReviewChecks AllEnabled = new(All);

    private readonly HashSet<string> _enabled;

    public ReviewChecks(IEnumerable<string> enabled) =>
        _enabled = new HashSet<string>(enabled, StringComparer.Ordinal);

    /// <summary>Whether <paramref name="id"/> is enabled for this verdict.</summary>
    public bool Has(string id) => _enabled.Contains(id);

    /// <summary>The disabled check ids, in canonical order, for an honest "skipped" report.</summary>
    public IReadOnlyList<string> Disabled => All.Where(c => !_enabled.Contains(c)).ToList();

    /// <summary>
    /// Resolves the enabled set from the three selection inputs, in precedence order:
    /// <list type="number">
    ///   <item><paramref name="only"/> — when non-empty, restricts the universe to exactly
    ///     these checks (an allowlist); empty means "all checks".</item>
    ///   <item><paramref name="configDisabled"/> — the project's <c>.spectacle.json</c>
    ///     <c>disabledChecks</c>, subtracted from the universe.</item>
    ///   <item><paramref name="skip"/> — the CLI <c>--skip</c> list, also subtracted.</item>
    /// </list>
    /// Unknown ids in any input are ignored (tolerant, matching config parsing). Use
    /// <see cref="Unknown"/> to surface typos to the caller.
    /// </summary>
    public static ReviewChecks Resolve(
        IReadOnlyList<string> only, IReadOnlyList<string> skip, IReadOnlyList<string> configDisabled)
    {
        var known = new HashSet<string>(All, StringComparer.Ordinal);
        var set = new HashSet<string>(All, StringComparer.Ordinal);

        var onlyKnown = only.Select(Normalize).Where(known.Contains).ToList();
        if (onlyKnown.Count > 0) set.IntersectWith(onlyKnown);

        foreach (var c in configDisabled.Select(Normalize)) set.Remove(c);
        foreach (var c in skip.Select(Normalize)) set.Remove(c);

        return new ReviewChecks(set);
    }

    /// <summary>The ids in <paramref name="requested"/> that are not recognized check names.</summary>
    public static IReadOnlyList<string> Unknown(IEnumerable<string> requested)
    {
        var known = new HashSet<string>(All, StringComparer.Ordinal);
        return requested.Select(Normalize).Where(c => c.Length != 0 && !known.Contains(c)).ToList();
    }

    private static string Normalize(string id) => id.Trim().ToLowerInvariant();
}
