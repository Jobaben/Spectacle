using System;
using System.Collections.Generic;
using System.Linq;
using Spectacle.Checks;

namespace Spectacle.Gate;

/// <summary>
/// How a project grades findings: the severity it assigns each rule, and the severity at which the
/// gate stops the pipeline.
///
/// A single all-or-nothing verdict forces a team to choose between blocking on everything and
/// checking nothing, and a check that is too strict for one project gets turned off entirely —
/// after which nobody sees it again. Grading separates *is this a defect* from *should this stop
/// the pipeline*: downgrade a rule to a warning and it keeps appearing in every report and every CI
/// annotation while the gate goes green. A rule at <see cref="GateSeverity.Info"/> is advice, never
/// blocking, whatever the threshold — the lowest setting still reports.
///
/// Grades are declared once in <c>.spectacle.json</c>:
/// <code>
/// { "failOn": "error", "severity": { "bare-urls": "warning", "prose/hedge": "off" } }
/// </code>
/// A key names either a whole check (<c>bare-urls</c>) or one rule (<c>bare-urls/bare-url</c>),
/// with the rule-level grade winning — the same specificity rule linters use.
/// </summary>
public sealed class GatePolicy
{
    /// <summary>Every rule at its catalogued default, blocking on errors.</summary>
    public static readonly GatePolicy Default = new(new Dictionary<string, GateSeverity>(), GateSeverity.Error);

    private readonly Dictionary<string, GateSeverity> _overrides;

    private GatePolicy(Dictionary<string, GateSeverity> overrides, GateSeverity failOn)
    {
        _overrides = overrides;
        FailOn = failOn;
    }

    /// <summary>The severity at or above which a finding fails the gate.</summary>
    public GateSeverity FailOn { get; }

    /// <summary>Whether this policy re-grades anything, for an honest report of what was applied.</summary>
    public bool HasOverrides => _overrides.Count != 0;

    /// <summary>The re-graded rules, as <c>id=severity</c> pairs in id order, for the verdict's report.</summary>
    public IReadOnlyList<string> OverrideSummary =>
        _overrides.OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"{kv.Key}={kv.Value.ToString().ToLowerInvariant()}")
            .ToList();

    /// <summary>
    /// Builds a policy from a config's <c>severity</c> map and <c>failOn</c> value. Unparseable
    /// severity names are dropped (the rule keeps its default) — a typo in a config must not crash
    /// a headless gate; <see cref="UnknownSeverities"/> surfaces them to the caller instead.
    /// </summary>
    public static GatePolicy Create(
        IReadOnlyDictionary<string, string> severityOverrides, string? failOn)
    {
        var graded = new Dictionary<string, GateSeverity>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in severityOverrides)
        {
            var severity = GateSeverities.Parse(value);
            if (severity is not null && key.Trim().Length != 0) graded[key.Trim()] = severity.Value;
        }

        return new GatePolicy(graded, GateSeverities.Parse(failOn) ?? GateSeverity.Error);
    }

    /// <summary>This policy with a different threshold — a single run's <c>--fail-on</c> override.</summary>
    public GatePolicy WithFailOn(GateSeverity failOn) =>
        failOn == FailOn ? this : new GatePolicy(_overrides, failOn);

    /// <summary>
    /// The severity to report a rule at: its rule-level grade, else its check-level grade, else the
    /// catalogued default.
    /// </summary>
    public GateSeverity SeverityOf(string checkId, string ruleId)
    {
        if (_overrides.TryGetValue(ruleId, out var byRule)) return byRule;
        if (_overrides.TryGetValue(checkId, out var byCheck)) return byCheck;
        return RuleCatalog.DefaultSeverityOf(ruleId);
    }

    /// <summary>Re-grades a stream of findings, preserving order.</summary>
    public IReadOnlyList<GateFinding> Apply(IReadOnlyList<GateFinding> findings) =>
        findings.Select(f => f.WithSeverity(SeverityOf(f.CheckId, f.RuleId))).ToList();

    /// <summary>Whether a finding at <paramref name="severity"/> fails this gate.</summary>
    public bool Blocks(GateSeverity severity) =>
        // Info is advice by definition: even a threshold of Info reports without blocking, so the
        // lowest setting can't turn hedging prose into a build failure.
        severity != GateSeverity.Info && severity >= FailOn;

    /// <summary>
    /// The severity values in <paramref name="severityOverrides"/> that could not be parsed, as
    /// <c>key=value</c> pairs, so a caller can warn about a typo instead of silently ignoring it.
    /// </summary>
    public static IReadOnlyList<string> UnknownSeverities(IReadOnlyDictionary<string, string> severityOverrides) =>
        severityOverrides
            .Where(kv => GateSeverities.Parse(kv.Value) is null)
            .Select(kv => $"{kv.Key}={kv.Value}")
            .ToList();

    /// <summary>
    /// The keys in <paramref name="severityOverrides"/> that name neither a known check nor a known
    /// rule — a grade that will never apply to anything.
    /// </summary>
    public static IReadOnlyList<string> UnknownRules(IReadOnlyDictionary<string, string> severityOverrides)
    {
        var known = new HashSet<string>(ReviewChecks.All, StringComparer.OrdinalIgnoreCase);
        foreach (var rule in RuleCatalog.All) known.Add(rule.Id);
        // The advisory checks never appear in ReviewChecks.All (they are not gate-selectable) but
        // are perfectly valid grading targets.
        known.Add("prose");

        return severityOverrides.Keys
            .Select(k => k.Trim())
            .Where(k => k.Length != 0 && !known.Contains(k))
            .ToList();
    }
}
