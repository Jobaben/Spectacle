using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Spectacle.Render;

/// <summary>
/// Renders findings as a JUnit XML report — the format nearly every CI system already knows how to
/// display, from Jenkins and GitLab to Azure Pipelines and CircleCI.
///
/// It is the pragmatic complement to SARIF: SARIF is the right model for static analysis but needs
/// a platform that ingests it, whereas a JUnit file drops into an existing test-report step and
/// turns a documentation gate into rows a team already reads. Each document becomes a
/// <c>testsuite</c> and each rule that fired becomes a <c>testcase</c> — so "which rules is this
/// document failing" reads as a list of failing tests, and a rule that stops firing shows up as a
/// test that started passing.
///
/// Advisory findings are emitted as <c>skipped</c> rather than <c>failure</c>: visible in the
/// report, never red.
/// </summary>
public static class JUnitExporter
{
    public static string Build(IReadOnlyList<BatchReviewEntry> entries) =>
        Build(entries, GatePolicy.Default);

    public static string Build(IReadOnlyList<BatchReviewEntry> entries, GatePolicy policy)
    {
        var suites = entries
            .Select(e => (e.Path, Findings: policy.Apply(FindingStream.All(e.Report))))
            .ToList();

        var totalFailures = suites.Sum(s => s.Findings.Count(f => f.Severity != GateSeverity.Info));
        var totalSkipped = suites.Sum(s => s.Findings.Count(f => f.Severity == GateSeverity.Info));
        // A document with no findings still contributes one (passing) test case, so a clean run
        // reports "1 test passed" per document rather than an empty, ambiguous report.
        var totalTests = suites.Sum(s => System.Math.Max(1, s.Findings.Count));

        var sb = new StringBuilder();
        sb.AppendLine("""<?xml version="1.0" encoding="utf-8"?>""");
        sb.Append("<testsuites name=\"Spectacle\" tests=\"").Append(totalTests)
          .Append("\" failures=\"").Append(totalFailures)
          .Append("\" skipped=\"").Append(totalSkipped)
          .AppendLine("\">");

        foreach (var (path, findings) in suites)
        {
            var name = path.Replace('\\', '/');
            sb.Append("  <testsuite name=\"").Append(Xml(name))
              .Append("\" tests=\"").Append(System.Math.Max(1, findings.Count))
              .Append("\" failures=\"").Append(findings.Count(f => f.Severity != GateSeverity.Info))
              .Append("\" skipped=\"").Append(findings.Count(f => f.Severity == GateSeverity.Info))
              .AppendLine("\">");

            if (findings.Count == 0)
            {
                sb.Append("    <testcase classname=\"").Append(Xml(name))
                  .AppendLine("\" name=\"spectacle gate\" />");
            }

            foreach (var f in findings)
            {
                sb.Append("    <testcase classname=\"").Append(Xml(name))
                  .Append("\" name=\"").Append(Xml($"{f.RuleId} (line {f.Line.ToString(CultureInfo.InvariantCulture)})"))
                  .AppendLine("\">");

                if (f.Severity == GateSeverity.Info)
                    sb.Append("      <skipped message=\"").Append(Xml(f.Message)).AppendLine("\" />");
                else
                    sb.Append("      <failure type=\"").Append(Xml(f.RuleId))
                      .Append("\" message=\"").Append(Xml(f.Message)).Append("\">")
                      .Append(Xml($"{name}:{f.Line} [{f.SeverityName}] {f.RuleId}\n{f.Description}\nFix: {f.Remedy}"))
                      .AppendLine("</failure>");

                sb.AppendLine("    </testcase>");
            }

            sb.AppendLine("  </testsuite>");
        }

        sb.AppendLine("</testsuites>");
        return sb.ToString().TrimEnd('\n');
    }

    // Escaped for both element text and attribute values, so one helper covers every insertion
    // point below.
    private static string Xml(string value) => value
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;")
        .Replace("'", "&apos;");
}
