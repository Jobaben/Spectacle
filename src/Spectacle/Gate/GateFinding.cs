using System;
using Spectacle.Checks;

namespace Spectacle.Gate;

/// <summary>
/// How much a finding matters. The ordering is deliberate — <c>Info &lt; Warning &lt; Error</c> —
/// so a gate threshold is a simple comparison.
/// </summary>
public enum GateSeverity
{
    /// <summary>Guidance. Reported, never blocking, whatever the threshold.</summary>
    Info = 0,

    /// <summary>A real defect that a team may choose not to block on.</summary>
    Warning = 1,

    /// <summary>A defect that fails the gate by default.</summary>
    Error = 2,
}

/// <summary>
/// One finding in a normalized shape: which check produced it, which rule it broke, how much it
/// matters, where it is, and what is wrong.
///
/// Every check has its own finding record with its own field names, which is right for the check
/// but wrong for everything downstream — six exporters each re-walking twenty-odd typed
/// collections is six places to forget a new check. <see cref="FindingStream"/> flattens a
/// <see cref="ReviewReport"/> into these once, and SARIF, GitHub annotations, JUnit, the fix
/// brief, the terminal verdict and the reader's overlay all consume the same stream. A new check
/// is wired into the stream in one place and appears in every output.
/// </summary>
public sealed record GateFinding(
    string CheckId,
    string RuleId,
    GateSeverity Severity,
    int Line,
    string Message)
{
    /// <summary>The rule's human-readable one-line description, from <see cref="RuleCatalog"/>.</summary>
    public string Description => RuleCatalog.DescriptionOf(RuleId);

    /// <summary>The concrete edit that resolves this rule, from <see cref="RuleCatalog"/>.</summary>
    public string Remedy => RuleCatalog.RemedyOf(RuleId);

    /// <summary>The severity as the lowercase token used in JSON, SARIF and CI annotations.</summary>
    public string SeverityName => Severity switch
    {
        GateSeverity.Error => "error",
        GateSeverity.Warning => "warning",
        _ => "info",
    };

    /// <summary>This finding re-graded by a policy, leaving everything else untouched.</summary>
    public GateFinding WithSeverity(GateSeverity severity) =>
        Severity == severity ? this : this with { Severity = severity };
}

/// <summary>Parsing helpers for the severity names used in config and on the command line.</summary>
public static class GateSeverities
{
    /// <summary>
    /// Parses a severity name, tolerating the common synonyms other linters use (<c>warn</c>,
    /// <c>note</c>, <c>off</c>). Returns <c>null</c> for anything unrecognized so the caller can
    /// report the typo instead of silently guessing.
    /// </summary>
    public static GateSeverity? Parse(string? name) => (name ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "error" or "err" or "fail" => GateSeverity.Error,
        "warning" or "warn" => GateSeverity.Warning,
        "info" or "note" or "notice" or "advisory" or "off" => GateSeverity.Info,
        _ => null,
    };

    /// <summary>The accepted names, for a help or error message.</summary>
    public const string Accepted = "error, warning, info";
}
