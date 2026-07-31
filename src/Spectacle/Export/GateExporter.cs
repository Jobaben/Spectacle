using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Spectacle.Render;

namespace Spectacle.Export;

/// <summary>
/// Formats a <see cref="GateBatch"/> as a terminal verdict (default), a machine-readable JSON
/// envelope, or a Markdown report that pastes into a pull request.
///
/// The three forms carry the same facts for three readers: the person watching a build, the step
/// that branches on the result, and the reviewer who has to decide what to do about it. All three
/// state the threshold and any reduced coverage alongside the verdict, so "pass" never has to be
/// taken on trust.
/// </summary>
public static class GateExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Build(GateBatch batch, string toolVersion, bool json, bool markdown = false) =>
        markdown ? Markdown(batch)
        : json ? Json(batch, toolVersion)
        : Text(batch);

    // ---------- Terminal ----------

    private static string Text(GateBatch batch)
    {
        var sb = new StringBuilder();

        if (batch.Verdicts.Count == 1)
        {
            AppendVerdictText(sb, batch.Verdicts[0], indent: "  ");
            return sb.ToString().TrimEnd('\n');
        }

        sb.Append("gate ").Append(batch.Passed ? "PASS" : "FAIL").Append(" — ")
          .Append(batch.Verdicts.Count).Append(" document(s), ")
          .Append(batch.Failed.Count).AppendLine(" failing");

        foreach (var v in batch.Verdicts)
        {
            sb.Append("  ").Append(v.Passed ? "pass" : "FAIL").Append("  ")
              .Append(Path.GetFileName(v.SourcePath));
            if (!v.Passed) sb.Append("  (").Append(v.BlockingCount).Append(" blocking)");
            sb.AppendLine();
        }

        foreach (var v in batch.Failed)
        {
            sb.AppendLine();
            AppendVerdictText(sb, v, indent: "    ");
        }

        return sb.ToString().TrimEnd('\n');
    }

    private static void AppendVerdictText(StringBuilder sb, GateVerdict v, string indent)
    {
        sb.Append(Path.GetFileName(v.SourcePath)).Append(" — GATE ")
          .AppendLine(v.Passed ? "PASS" : "FAIL");

        sb.Append(indent).Append(v.BlockingCount).Append(" blocking · ")
          .Append(v.ErrorCount).Append(" error, ")
          .Append(v.WarningCount).Append(" warning, ")
          .Append(v.InfoCount).Append(" advisory · threshold: ")
          .AppendLine(v.FailOn.ToString().ToLowerInvariant());

        if (v.Metadata.Count != 0)
            sb.Append(indent).Append("metadata: ")
              .AppendLine(string.Join(" · ", v.Metadata.Select(m => $"{m.Key}={Compact(m.Value)}")));

        // A pass earned by running fewer checks is not the same fact as a clean pass.
        if (v.CoverageReduced)
        {
            var notes = new List<string>();
            if (v.SuppressedCount != 0) notes.Add($"{v.SuppressedCount} finding(s) suppressed inline");
            if (v.SkippedChecks.Count != 0) notes.Add("checks off: " + string.Join(", ", v.SkippedChecks));
            sb.Append(indent).Append("coverage: ").AppendLine(string.Join(" · ", notes));
        }

        if (v.AppliedGrades.Count != 0)
            sb.Append(indent).Append("grades: ").AppendLine(string.Join(" · ", v.AppliedGrades));

        if (v.Findings.Count != 0)
        {
            sb.AppendLine();
            var ruleWidth = Math.Min(34, v.Findings.Max(f => f.RuleId.Length));
            var lineWidth = v.Findings.Max(f => f.Line).ToString().Length;
            foreach (var f in v.Findings)
                sb.Append(indent)
                  .Append(f.SeverityName.PadRight(7)).Append("  line ")
                  .Append(f.Line.ToString().PadLeft(lineWidth)).Append("  ")
                  .Append(f.RuleId.PadRight(ruleWidth)).Append("  ")
                  .AppendLine(Compact(f.Message));
        }

        if (v.ChecklistTotal != 0)
            sb.AppendLine().Append(indent).Append("tasks: ")
              .Append(v.ChecklistDone).Append('/').Append(v.ChecklistTotal)
              .AppendLine(" checklist item(s) complete");

        if (!v.Passed)
            sb.Append(indent).Append("next: --fix-brief writes the revision list for the authoring agent")
              .AppendLine();
    }

    // ---------- JSON ----------

    private static string Json(GateBatch batch, string toolVersion)
    {
        var documents = batch.Verdicts.Select(DocumentPayload).ToList();

        // A single document still reports as a set, so a workflow parses one shape whether it
        // gated a file or a folder.
        var payload = new Dictionary<string, object?>
        {
            ["tool"] = "spectacle",
            ["version"] = toolVersion,
            ["gate"] = batch.Status,
            ["passed"] = batch.Passed,
            ["counts"] = new
            {
                documents = batch.Verdicts.Count,
                failing = batch.Failed.Count,
                blocking = batch.BlockingCount,
                error = batch.ErrorCount,
                warning = batch.WarningCount,
                info = batch.InfoCount,
            },
            ["documents"] = documents,
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static object DocumentPayload(GateVerdict v) => new
    {
        source = v.SourcePath,
        gate = v.Status,
        passed = v.Passed,
        failOn = v.FailOn.ToString().ToLowerInvariant(),
        counts = new
        {
            blocking = v.BlockingCount,
            error = v.ErrorCount,
            warning = v.WarningCount,
            info = v.InfoCount,
            suppressed = v.SuppressedCount,
        },
        // The document's own front matter, echoed so a workflow can route on the metadata it
        // stamped without re-reading and re-parsing the file.
        metadata = MetadataObject(v.Metadata),
        coverage = new
        {
            checksDisabled = v.SkippedChecks,
            suppressed = v.SuppressedCount,
            grades = v.AppliedGrades,
        },
        checklist = new { total = v.ChecklistTotal, done = v.ChecklistDone },
        findings = v.Findings.Select(f => new
        {
            severity = f.SeverityName,
            blocking = f.Severity != GateSeverity.Info && f.Severity >= v.FailOn,
            check = f.CheckId,
            rule = f.RuleId,
            line = f.Line,
            message = f.Message,
            description = f.Description,
            remedy = f.Remedy,
        }),
    };

    // ---------- Markdown ----------

    private static string Markdown(GateBatch batch)
    {
        var sb = new StringBuilder();
        var single = batch.Verdicts.Count == 1 ? batch.Verdicts[0] : null;

        sb.Append("# Gate ").Append(batch.Passed ? "passed" : "failed");
        if (single is not null) sb.Append(" — `").Append(Path.GetFileName(single.SourcePath)).Append('`');
        sb.AppendLine().AppendLine();

        if (single is null)
        {
            sb.Append(batch.Verdicts.Count).Append(" document(s), ")
              .Append(batch.Failed.Count).Append(" failing, ")
              .Append(batch.BlockingCount).AppendLine(" blocking finding(s).").AppendLine();
            sb.AppendLine("| Document | Gate | Blocking | Errors | Warnings |");
            sb.AppendLine("| --- | --- | --- | --- | --- |");
            foreach (var v in batch.Verdicts)
                sb.Append("| `").Append(Path.GetFileName(v.SourcePath)).Append("` | ")
                  .Append(v.Passed ? "✅ pass" : "❌ fail").Append(" | ")
                  .Append(v.BlockingCount).Append(" | ").Append(v.ErrorCount).Append(" | ")
                  .Append(v.WarningCount).AppendLine(" |");
            sb.AppendLine();
        }

        foreach (var v in batch.Verdicts)
        {
            if (single is null) sb.Append("## `").Append(Path.GetFileName(v.SourcePath)).AppendLine("`").AppendLine();

            sb.Append("**").Append(v.Passed ? "Passed" : "Failed").Append("** · ")
              .Append(v.BlockingCount).Append(" blocking · ")
              .Append(v.ErrorCount).Append(" error, ").Append(v.WarningCount).Append(" warning, ")
              .Append(v.InfoCount).Append(" advisory · threshold `")
              .Append(v.FailOn.ToString().ToLowerInvariant()).AppendLine("`").AppendLine();

            if (v.Metadata.Count != 0)
            {
                foreach (var (key, value) in v.Metadata)
                    sb.Append("- `").Append(key).Append("`: ").AppendLine(MarkdownCell(value));
                sb.AppendLine();
            }

            if (v.CoverageReduced)
            {
                sb.Append("> Coverage: ");
                var notes = new List<string>();
                if (v.SuppressedCount != 0) notes.Add($"{v.SuppressedCount} finding(s) suppressed inline");
                if (v.SkippedChecks.Count != 0) notes.Add("checks off: " + string.Join(", ", v.SkippedChecks));
                sb.AppendLine(string.Join("; ", notes)).AppendLine();
            }

            if (v.Findings.Count == 0)
            {
                sb.AppendLine("No findings.").AppendLine();
                continue;
            }

            sb.AppendLine("| Severity | Line | Rule | Finding |");
            sb.AppendLine("| --- | --- | --- | --- |");
            foreach (var f in v.Findings)
                sb.Append("| ").Append(f.SeverityName).Append(" | ").Append(f.Line)
                  .Append(" | `").Append(f.RuleId).Append("` | ")
                  .Append(MarkdownCell(f.Message)).AppendLine(" |");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd('\n');
    }

    /// <summary>
    /// The metadata pairs as a JSON object. A duplicate front-matter key is a defect the gate
    /// reports rather than a reason to fail: last value wins, matching what a YAML parser would
    /// hand the workflow, so building this must not throw the way a plain <c>ToDictionary</c> would.
    /// </summary>
    private static Dictionary<string, string> MetadataObject(
        IReadOnlyList<KeyValuePair<string, string>> metadata)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in metadata) map[key] = value;
        return map;
    }

    private static string Compact(string text)
    {
        var flat = text.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return flat.Length <= 100 ? flat : flat[..97] + "…";
    }

    // A finding's text can carry a pipe (a malformed-table message quotes the row) or a backtick,
    // either of which would break the table it lands in.
    private static string MarkdownCell(string text) =>
        Compact(text).Replace("|", "\\|");
}
