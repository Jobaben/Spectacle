using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Spectacle.Checks;

namespace Spectacle.Render;

/// <summary>
/// Renders one or more <see cref="ReviewReport"/>s as a SARIF 2.1.0 log — the static
/// analysis interchange format that GitHub code scanning, Azure DevOps, and other CI
/// dashboards ingest natively. <c>--review --json</c> is Spectacle's own shape; this is
/// the lingua franca, so the whole existing check battery becomes a first-class CI
/// analyzer (inline annotations, security/quality tabs) without bespoke glue.
///
/// Every report is one set of <c>results</c> sharing the same artifact URI, so a single
/// file and a whole batch take the same path. The checklist tally is informational, not a
/// defect, so it is not emitted as a result. Each finding becomes one result with a
/// <c>category/rule</c> rule id, a level mapped from its graded severity, a message, and a
/// one-based line location — advisory findings included, at SARIF's <c>note</c> level, so a
/// dashboard shows the guidance without failing on it.
///
/// The findings come from <see cref="FindingStream"/> and the rule descriptions from
/// <see cref="RuleCatalog"/>, so a new check appears in SARIF — result and catalogue entry
/// both — without this file changing at all.
/// </summary>
public static class SarifExporter
{
    private const string SchemaUri = "https://json.schemastore.org/sarif-2.1.0.json";
    private const string InformationUri = "https://github.com/Jobaben/Spectacle";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Builds the log with every rule at its catalogued default severity.
    /// </summary>
    public static string Build(IReadOnlyList<BatchReviewEntry> entries, string toolVersion) =>
        Build(entries, toolVersion, GatePolicy.Default);

    /// <summary>
    /// Builds the log with severities graded by <paramref name="policy"/>, so a rule a project has
    /// downgraded to a warning arrives in the CI dashboard as a warning rather than an error.
    /// </summary>
    public static string Build(
        IReadOnlyList<BatchReviewEntry> entries, string toolVersion, GatePolicy policy)
    {
        var results = entries.SelectMany(e => ResultsFor(e.Path, e.Report, policy)).ToList();

        var run = new
        {
            tool = new
            {
                driver = new
                {
                    name = "Spectacle",
                    informationUri = InformationUri,
                    version = toolVersion,
                    // The full catalogue up front, so a consumer gets a description and a fix for
                    // every rule Spectacle knows — including the ones that did not fire in this run.
                    rules = RuleCatalog.All.Select(r => new
                    {
                        id = r.Id,
                        shortDescription = new { text = r.Description },
                        help = new { text = r.Remedy },
                        defaultConfiguration = new { level = SarifLevel(r.DefaultSeverity) },
                    }).ToArray(),
                },
            },
            results,
        };

        // The SARIF schema pointer is the reserved "$schema" property, which an anonymous
        // type can't express; an ordered dictionary carries the literal key cleanly.
        var log = new Dictionary<string, object>
        {
            ["$schema"] = SchemaUri,
            ["version"] = "2.1.0",
            ["runs"] = new[] { run },
        };

        return JsonSerializer.Serialize(log, JsonOptions);
    }

    private static IEnumerable<object> ResultsFor(string path, ReviewReport report, GatePolicy policy)
    {
        var uri = path.Replace('\\', '/');
        return policy.Apply(FindingStream.All(report))
            .Select(f => Result(f.RuleId, SarifLevel(f.Severity), f.Message, uri, f.Line));
    }

    // SARIF's own vocabulary: it calls the lowest reporting level "note", not "info".
    private static string SarifLevel(GateSeverity severity) => severity switch
    {
        GateSeverity.Error => "error",
        GateSeverity.Warning => "warning",
        _ => "note",
    };

    private static object Result(string ruleId, string level, string message, string uri, int line) => new
    {
        ruleId,
        level,
        message = new { text = message },
        locations = new[]
        {
            new
            {
                physicalLocation = new
                {
                    artifactLocation = new { uri },
                    region = new { startLine = line },
                },
            },
        },
    };
}
