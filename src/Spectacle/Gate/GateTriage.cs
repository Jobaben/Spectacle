using System;
using System.Collections.Generic;
using System.Linq;

namespace Spectacle.Gate;

/// <summary>
/// Session-scoped triage over a gate verdict: the reader can waive individual findings, and the
/// fix brief it copies covers only what survives.
///
/// Waiving is deliberately not suppression. An inline
/// <c>&lt;!-- spectacle-disable-next-line --&gt;</c> changes the verdict itself — for everyone,
/// durably, in the document. A waive changes nothing the pipeline sees: the badge keeps its
/// counts, <c>--gate</c> keeps its exit code, and the only thing that moves is which findings the
/// *brief* hands back to the authoring agent. That split is what lets a reviewer say "fix these
/// four, I disagree with those two" without the two silently vanishing from the record.
///
/// A waive is keyed line-insensitively — <see cref="KeyOf"/> is (check, rule, message), the same
/// identity <see cref="ReviewDelta"/> uses — so a finding stays waived while revisions move it
/// around the document, and clears on its own the moment the finding itself is gone.
/// </summary>
public static class GateTriage
{
    /// <summary>The line-insensitive identity of a finding, stable across revisions.</summary>
    public static string KeyOf(GateFinding f) => $"{f.CheckId}|{f.RuleId}|{f.Message}";

    /// <summary>
    /// The verdict with every waived finding removed and the tallies recomputed under the same
    /// threshold — the verdict the fix brief should describe. Coverage context (skipped checks,
    /// inline suppressions) is carried unchanged: waiving reduces the brief, never the caveats.
    /// </summary>
    public static GateVerdict Without(GateVerdict verdict, IReadOnlyCollection<string> waivedKeys)
    {
        if (waivedKeys.Count == 0) return verdict;

        var waived = waivedKeys as ISet<string> ?? new HashSet<string>(waivedKeys, StringComparer.Ordinal);
        var findings = verdict.Findings.Where(f => !waived.Contains(KeyOf(f))).ToList();
        if (findings.Count == verdict.Findings.Count) return verdict;

        return verdict with
        {
            Findings = findings,
            BlockingCount = findings.Count(f =>
                f.Severity != GateSeverity.Info && f.Severity >= verdict.FailOn),
        };
    }

    /// <summary>
    /// Drops waives whose finding no longer exists in <paramref name="verdict"/>, so the session's
    /// waive set tracks the document instead of accumulating ghosts. Returns the surviving keys.
    /// </summary>
    public static IReadOnlyList<string> Prune(GateVerdict verdict, IEnumerable<string> waivedKeys)
    {
        var live = verdict.Findings.Select(KeyOf).ToHashSet(StringComparer.Ordinal);
        return waivedKeys.Where(live.Contains).ToList();
    }
}
