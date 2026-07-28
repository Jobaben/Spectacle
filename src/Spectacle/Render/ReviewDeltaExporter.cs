using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Spectacle.Render;

/// <summary>
/// Formats a <see cref="ReviewDelta"/> — a grouped fixed / new / persisting summary
/// (default text) or structured JSON for an agent to act on across a revision.
/// </summary>
public static class ReviewDeltaExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Build(ReviewDelta delta, string sourcePath, string baselinePath, bool json) =>
        json ? Json(delta, sourcePath, baselinePath) : Text(delta, sourcePath, baselinePath);

    private static string Text(ReviewDelta d, string sourcePath, string baselinePath)
    {
        var sb = new StringBuilder();
        sb.Append(Path.GetFileName(sourcePath)).Append(" — review delta vs ").Append(Path.GetFileName(baselinePath))
          .Append(": ").Append(d.Fixed.Count).Append(" fixed, ")
          .Append(d.New.Count).Append(" new, ")
          .Append(d.Persisting.Count).AppendLine(" persisting");

        Section(sb, "fixed", d.Fixed);
        Section(sb, "new", d.New);
        Section(sb, "persisting", d.Persisting);

        sb.Append("  checklist: ").Append(d.BaselineChecklistDone).Append('/').Append(d.BaselineChecklistTotal)
          .Append(" -> ").Append(d.RevisedChecklistDone).Append('/').Append(d.RevisedChecklistTotal).Append(" complete");
        return sb.ToString();
    }

    private static void Section(StringBuilder sb, string label, IReadOnlyList<DeltaFinding> findings)
    {
        sb.Append("  ").Append(label).Append(" (").Append(findings.Count).AppendLine("):");
        foreach (var f in findings)
            sb.Append("    line ").Append(f.Line).Append("  [").Append(f.Category).Append('/').Append(f.Rule)
              .Append("] ").AppendLine(f.Message);
    }

    private static string Json(ReviewDelta d, string sourcePath, string baselinePath)
    {
        var payload = new
        {
            source = sourcePath,
            baseline = baselinePath,
            fixedCount = d.Fixed.Count,
            newCount = d.New.Count,
            persistingCount = d.Persisting.Count,
            @fixed = d.Fixed,
            @new = d.New,
            persisting = d.Persisting,
            checklist = new
            {
                baseline = new { done = d.BaselineChecklistDone, total = d.BaselineChecklistTotal },
                revised = new { done = d.RevisedChecklistDone, total = d.RevisedChecklistTotal },
            },
        };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }
}
