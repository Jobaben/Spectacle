using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Spectacle.Checks;

namespace Spectacle.Render;

/// <summary>
/// Writes the gate's verdict back out as a revision brief — instructions addressed to whichever
/// tool or agent authored the document.
///
/// This is the piece that closes the write → gate → revise loop. Every other output here describes
/// findings to a *reader*: a rule id, a line, a message. An authoring agent handed that has to
/// infer what "toc/stale-toc-entry at line 40" wants done, and will sometimes infer wrong, or
/// helpfully rewrite half the document on the way past. The brief removes the inference: each
/// finding arrives with the concrete edit that resolves it (from <see cref="RuleCatalog"/>), the
/// findings are ordered bottom-up so applying one never invalidates the next one's line number, and
/// the scope is stated explicitly so a fix pass stays a fix pass.
///
/// So a workflow's revise step is: gate → if it fails, write the brief → feed the brief and the
/// document to the authoring tool → gate again. No human in the middle translating rule ids into
/// instructions, and no bespoke glue per AI tool: the brief is Markdown, which every tool reads.
/// </summary>
public static class FixBriefExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Builds the brief. <paramref name="json"/> yields the same content as a structured
    /// instruction list, for a tool that would rather be handed fields than prose.
    /// </summary>
    public static string Build(GateVerdict verdict, bool json) =>
        json ? Json(verdict) : Markdown(verdict);

    private static string Markdown(GateVerdict v)
    {
        var name = Path.GetFileName(v.SourcePath);
        // Bottom-up: an edit at line 12 can shift every line after it, so working from the end of
        // the document keeps the remaining line numbers valid for the whole pass.
        var required = v.Findings.Where(f => Blocks(f, v)).OrderByDescending(f => f.Line).ToList();
        var optional = v.Findings.Where(f => !Blocks(f, v)).OrderByDescending(f => f.Line).ToList();

        var sb = new StringBuilder();
        sb.Append("# Revision brief — ").AppendLine(name).AppendLine();

        if (v.Passed)
            sb.Append("`").Append(name).Append("` **passes** its quality gate at threshold `")
              .Append(Threshold(v)).AppendLine("`. Nothing below is required.").AppendLine();
        else
            sb.Append("`").Append(name).Append("` **does not pass** its quality gate. Apply the required fixes below, ")
              .AppendLine("then re-run the gate to confirm.").AppendLine();

        sb.Append("- Verdict: **").Append(v.Status).Append("** — ")
          .Append(v.BlockingCount).Append(" blocking (")
          .Append(v.ErrorCount).Append(" error, ").Append(v.WarningCount).Append(" warning, ")
          .Append(v.InfoCount).Append(" advisory), threshold `").Append(Threshold(v)).AppendLine("`");
        sb.Append("- Re-check with: `Spectacle.exe \"").Append(v.SourcePath).AppendLine("\" --gate`");

        if (v.Metadata.Count != 0)
            sb.Append("- Document declares: ")
              .AppendLine(string.Join(", ", v.Metadata.Select(m => $"`{m.Key}` = {m.Value}")));

        if (v.CoverageReduced)
        {
            var notes = new List<string>();
            if (v.SkippedChecks.Count != 0) notes.Add("checks disabled: " + string.Join(", ", v.SkippedChecks));
            if (v.SuppressedCount != 0) notes.Add($"{v.SuppressedCount} finding(s) suppressed inline");
            sb.Append("- Coverage note: ").AppendLine(string.Join("; ", notes));
        }

        sb.AppendLine().AppendLine("## How to apply this brief").AppendLine();
        sb.AppendLine("1. Change only what the findings below ask for. Leave every other line exactly as it is.");
        sb.AppendLine("2. Work top to bottom through this list — it is ordered from the end of the document backwards, so each line number is still correct when you reach it.");
        sb.AppendLine("3. Do not add a changelog, a summary of your edits, or a note about this brief. The revised document is the whole deliverable.");
        sb.AppendLine("4. Required items must all be resolved. Optional items are judgement calls — apply them only where they genuinely improve the document.");
        sb.AppendLine("5. If a finding cannot be fixed without losing meaning the document needs, keep the content and mark that line instead:");
        sb.AppendLine("   `<!-- spectacle-disable-next-line <check-id> -->` on the line above it. Use this sparingly, and never to clear a batch of findings at once.");

        AppendGroup(sb, "Required fixes", required, v);
        AppendGroup(sb, "Optional improvements", optional, v);

        if (required.Count == 0 && optional.Count == 0)
            sb.AppendLine().AppendLine("No findings. Leave the document unchanged.");

        return sb.ToString().TrimEnd('\n');
    }

    private static void AppendGroup(
        StringBuilder sb, string title, IReadOnlyList<GateFinding> findings, GateVerdict v)
    {
        if (findings.Count == 0) return;

        sb.AppendLine().Append("## ").Append(title).Append(" (").Append(findings.Count).AppendLine(")").AppendLine();

        var n = 0;
        foreach (var f in findings)
        {
            n++;
            sb.Append("### ").Append(n).Append(". Line ").Append(f.Line)
              .Append(" — `").Append(f.RuleId).AppendLine("`").AppendLine();
            sb.Append("- What was found: ").AppendLine(f.Message);
            sb.Append("- Why it matters: ").AppendLine(f.Description);
            if (f.Remedy.Length != 0) sb.Append("- **Do this:** ").AppendLine(f.Remedy);
            sb.AppendLine();
        }
    }

    private static string Json(GateVerdict v)
    {
        var payload = new
        {
            source = v.SourcePath,
            gate = v.Status,
            passed = v.Passed,
            failOn = Threshold(v),
            // Same bottom-up ordering as the Markdown brief, for the same reason: a consumer
            // applying these in order never invalidates a later line number.
            instructions = v.Findings
                .OrderByDescending(f => Blocks(f, v))
                .ThenByDescending(f => f.Line)
                .Select((f, i) => new
                {
                    order = i + 1,
                    required = Blocks(f, v),
                    line = f.Line,
                    rule = f.RuleId,
                    check = f.CheckId,
                    severity = f.SeverityName,
                    found = f.Message,
                    why = f.Description,
                    action = f.Remedy,
                }),
            recheckCommand = $"Spectacle.exe \"{v.SourcePath}\" --gate",
            constraints = new[]
            {
                "Change only what the instructions ask for; leave every other line unchanged.",
                "Apply the instructions in the order given so each line number stays valid.",
                "Do not add a changelog, summary, or note about this brief to the document.",
                "Resolve every required instruction; optional ones are judgement calls.",
            },
        };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static bool Blocks(GateFinding f, GateVerdict v) =>
        f.Severity != GateSeverity.Info && f.Severity >= v.FailOn;

    private static string Threshold(GateVerdict v) => v.FailOn.ToString().ToLowerInvariant();
}
