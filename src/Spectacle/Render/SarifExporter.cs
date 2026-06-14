using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

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
/// <c>category/rule</c> rule id, an <c>error</c> level (these are the issues that fail the
/// <c>--review</c> gate), a message, and a one-based line location.
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

    // The full rule catalogue, by stable rule id, with a human-readable description. Listed
    // up front in the tool driver so consumers get descriptions even for rules that did not
    // fire in this run.
    private static readonly (string Id, string Description)[] Catalog =
    {
        ("lint/placeholder", "Leftover placeholder marker (TODO, TBD, FIXME, …) in spec prose."),
        ("lint/empty-section", "A heading with no content of its own and no subsection beneath it."),
        ("structure/multiple-h1", "More than one top-level (h1) heading."),
        ("structure/skipped-level", "A heading skips a level (e.g. h1 jumps to h3)."),
        ("structure/duplicate-heading", "Duplicate heading text, which also yields ambiguous anchors."),
        ("links", "A broken internal link (unresolved anchor or empty target)."),
        ("tables", "A malformed GFM pipe table (row cell count differs from the header)."),
        ("fences/unclosed-fence", "A fenced code block opened but never closed."),
        ("paths", "A relative link/image target that does not exist on disk."),
        ("duplication", "A block (paragraph, list item, code, table) repeated verbatim elsewhere."),
        ("alt-text", "An image with no alt text (empty description)."),
        ("emphasis-heading", "An emphasized line used as a fake heading instead of a real heading."),
        ("sections", "A required section (by the spec template) is missing from the document."),
    };

    public static string Build(IReadOnlyList<BatchReviewEntry> entries, string toolVersion)
    {
        var results = entries.SelectMany(e => ResultsFor(e.Path, e.Report)).ToList();

        var run = new
        {
            tool = new
            {
                driver = new
                {
                    name = "Spectacle",
                    informationUri = InformationUri,
                    version = toolVersion,
                    rules = Catalog.Select(r => new
                    {
                        id = r.Id,
                        shortDescription = new { text = r.Description },
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

    private static IEnumerable<object> ResultsFor(string path, ReviewReport r)
    {
        var uri = path.Replace('\\', '/');

        foreach (var f in r.Lint) yield return Result($"lint/{f.Rule}", f.Message, uri, f.Line);
        foreach (var f in r.Structure) yield return Result($"structure/{f.Rule}", f.Message, uri, f.Line);
        foreach (var b in r.Links) yield return Result("links", $"{b.Target}: {b.Reason}", uri, b.Line);
        foreach (var t in r.Tables) yield return Result("tables", t.Message, uri, t.Line);
        foreach (var f in r.Fences) yield return Result($"fences/{f.Rule}", f.Message, uri, f.Line);
        foreach (var p in r.Paths) yield return Result("paths", $"{p.Target}: {p.Reason}", uri, p.Line);
        foreach (var d in r.Duplication)
            yield return Result("duplication", $"{d.Kind} duplicates line {d.FirstLine}", uri, d.Line);
        foreach (var a in r.AltText)
            yield return Result("alt-text", $"image missing alt text: {(a.Target.Length == 0 ? "(no target)" : a.Target)}", uri, a.Line);
        foreach (var e in r.EmphasisHeadings)
            yield return Result("emphasis-heading", $"emphasized line used as heading: '{e.Text}'", uri, e.Line);
        // A missing section is a document-level defect with no line; anchor it at line 1 so it
        // still carries a valid SARIF region (startLine must be >= 1).
        foreach (var s in r.Sections)
            yield return Result("sections", $"missing required section: '{s.Required}'", uri, 1);
    }

    private static object Result(string ruleId, string message, string uri, int line) => new
    {
        ruleId,
        level = "error",
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
