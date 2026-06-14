using System.IO;
using System.Windows;
using Spectacle.Annotations;
using Spectacle.Cli;
using Spectacle.Files;
using Spectacle.Install;
using Spectacle.Render;

namespace Spectacle;

public static class Program
{
    private const string UsageText = """
        Spectacle — Markdown viewer

        Usage:
          Spectacle.exe <file.md|file.markdown>   Open and render a Markdown file
          Spectacle.exe <file> --stats            Print document statistics and exit
          Spectacle.exe <file> --export-html [out] Export rendered HTML and exit
          Spectacle.exe <file> --revision-plan [out] [--json] [--unresolved] Export the review's revision plan and exit
          Spectacle.exe <file> --review-summary [--json] Print review status (open/resolved/orphaned) and exit
          Spectacle.exe <file> --lint [--json]    Report spec readiness issues (placeholders, empty sections) and exit
          Spectacle.exe <file> --outline [--json] Print the heading outline and exit
          Spectacle.exe <file> --checklist [--json] Report acceptance-criteria/task-list completion and exit
          Spectacle.exe <file> --check-links [--json] Report broken internal links and exit (non-zero if any)
          Spectacle.exe <file> --diff <other> [--json] Show block-level changes vs another spec and exit
          Spectacle.exe <file> --check-structure [--json] Report heading-hierarchy issues and exit (non-zero if any)
          Spectacle.exe <file> --check-tables [--json] Report malformed tables and exit (non-zero if any)
          Spectacle.exe <file> --check-fences [--json] Report fenced-code-block issues (unclosed, untagged) and exit
          Spectacle.exe <file> --check-paths [--json] Report relative link/image targets missing on disk and exit (non-zero if any)
          Spectacle.exe <file> --review [--json]  Run all checks and exit (non-zero if any issues)
          Spectacle.exe --register                Register as default handler for .md/.markdown (per-user)
          Spectacle.exe --unregister              Remove the file association
          Spectacle.exe --help, -h                Show this help
          Spectacle.exe --version                 Show version
        """;

    [STAThread]
    public static int Main(string[] args)
    {
        var command = CliArgs.Parse(args);
        return command switch
        {
            CliCommand.Help => Print(UsageText, 0),
            CliCommand.Version => Print(GetVersion(), 0),
            CliCommand.Register => DoRegister(),
            CliCommand.Unregister => DoUnregister(),
            CliCommand.Stats stats => DoStats(stats.Path),
            CliCommand.ExportHtml export => DoExportHtml(export.Path, export.OutputPath),
            CliCommand.RevisionPlan plan => DoRevisionPlan(plan.Path, plan.OutputPath, plan.Json, plan.UnresolvedOnly),
            CliCommand.ReviewSummary summary => DoReviewSummary(summary.Path, summary.Json),
            CliCommand.Lint lint => DoLint(lint.Path, lint.Json),
            CliCommand.Outline outline => DoOutline(outline.Path, outline.Json),
            CliCommand.Checklist checklist => DoChecklist(checklist.Path, checklist.Json),
            CliCommand.CheckLinks check => DoCheckLinks(check.Path, check.Json),
            CliCommand.Diff diff => DoDiff(diff.Path, diff.OtherPath, diff.Json),
            CliCommand.CheckStructure structure => DoCheckStructure(structure.Path, structure.Json),
            CliCommand.CheckTables tables => DoCheckTables(tables.Path, tables.Json),
            CliCommand.CheckFences fences => DoCheckFences(fences.Path, fences.Json),
            CliCommand.CheckPaths paths => DoCheckPaths(paths.Path, paths.Json),
            CliCommand.Review review => DoReview(review.Path, review.Json),
            CliCommand.Open open => DoOpen(open.Path),
            _ => Print(UsageText, 0),
        };
    }

    private static int DoOpen(string path)
    {
        if (!ValidateSource(path)) return 2;

        var app = new App();
        var window = new MainWindow(path);
        return app.Run(window);
    }

    private static int DoStats(string path)
    {
        if (!ValidateSource(path)) return 2;

        var stats = DocumentStats.Compute(File.ReadAllText(path));
        Console.WriteLine($"""
            {Path.GetFileName(path)}
              Words:        {stats.Words:N0}
              Reading time: ~{stats.ReadingTimeMinutes} min
              Characters:   {stats.Characters:N0}
              Lines:        {stats.Lines:N0}
              Headings:     {stats.Headings:N0}
              Code blocks:  {stats.CodeBlocks:N0}
              Links:        {stats.Links:N0}
              Images:       {stats.Images:N0}
            """);
        return 0;
    }

    private static int DoExportHtml(string path, string? outputPath)
    {
        if (!ValidateSource(path)) return 2;

        var title = Path.GetFileNameWithoutExtension(path) ?? "document";
        var html = HtmlExporter.FromMarkdown(File.ReadAllText(path), PreviewTheme.Dark, title);
        var target = outputPath ?? Path.ChangeExtension(path, ".html");
        File.WriteAllText(target, html);
        Console.WriteLine($"Exported {Path.GetFullPath(target)}");
        return 0;
    }

    private static int DoRevisionPlan(string path, string? outputPath, bool json, bool unresolvedOnly)
    {
        if (!ValidateSource(path)) return 2;

        var content = File.ReadAllText(path);
        var annotations = new AnnotationStore(path).Load();
        if (annotations.Comments.Count == 0)
            Console.Error.WriteLine($"No review comments found for {Path.GetFileName(path)}; writing an empty plan.");

        var format = json ? RevisionPlanFormat.Json : RevisionPlanFormat.Markdown;
        var text = RevisionPlanGenerator.Generate(path, content, annotations, DateTime.UtcNow, format, unresolvedOnly);

        var target = outputPath ?? Path.ChangeExtension(path, json ? ".revisions.json" : ".revisions.md");
        File.WriteAllText(target, text);
        Console.WriteLine($"Exported {Path.GetFullPath(target)}");
        return 0;
    }

    private static int DoReviewSummary(string path, bool json)
    {
        if (!ValidateSource(path)) return 2;

        var content = File.ReadAllText(path);
        var annotations = new AnnotationStore(path).Load();
        var summary = ReviewSummary.Compute(content, annotations);
        var format = json ? RevisionPlanFormat.Json : RevisionPlanFormat.Markdown;
        Console.WriteLine(ReviewSummaryExporter.Build(summary, path, DateTime.UtcNow, format));
        return 0;
    }

    private static int DoLint(string path, bool json)
    {
        if (!ValidateSource(path)) return 2;

        var findings = SpecLinter.Lint(File.ReadAllText(path));
        Console.WriteLine(SpecLintExporter.Build(findings, path, json));
        // Non-zero when issues are found so --lint can gate a pipeline.
        return findings.Count == 0 ? 0 : 1;
    }

    private static int DoOutline(string path, bool json)
    {
        if (!ValidateSource(path)) return 2;

        var outline = new MdRenderer().Render(File.ReadAllText(path)).Outline;
        Console.WriteLine(OutlineExporter.Build(outline, path, json));
        return 0;
    }

    private static int DoChecklist(string path, bool json)
    {
        if (!ValidateSource(path)) return 2;

        var items = ChecklistAnalyzer.Analyze(File.ReadAllText(path));
        Console.WriteLine(ChecklistExporter.Build(items, path, json));
        return 0;
    }

    private static int DoCheckLinks(string path, bool json)
    {
        if (!ValidateSource(path)) return 2;

        var broken = LinkChecker.Check(File.ReadAllText(path));
        Console.WriteLine(LinkCheckExporter.Build(broken, path, json));
        // Non-zero when links are broken so --check-links can gate a pipeline.
        return broken.Count == 0 ? 0 : 1;
    }

    private static int DoDiff(string path, string otherPath, bool json)
    {
        if (!ValidateSource(path)) return 2;
        if (!ValidateSource(otherPath)) return 2;

        // The current file is the revised version; <other> is the baseline.
        var diff = SpecDiff.Compare(File.ReadAllText(otherPath), File.ReadAllText(path));
        Console.WriteLine(SpecDiffExporter.Build(diff, path, otherPath, json));
        return 0;
    }

    private static int DoCheckStructure(string path, bool json)
    {
        if (!ValidateSource(path)) return 2;

        var findings = StructureChecker.Check(File.ReadAllText(path));
        Console.WriteLine(StructureCheckExporter.Build(findings, path, json));
        // Non-zero when issues are found so --check-structure can gate a pipeline.
        return findings.Count == 0 ? 0 : 1;
    }

    private static int DoCheckTables(string path, bool json)
    {
        if (!ValidateSource(path)) return 2;

        var issues = TableChecker.Check(File.ReadAllText(path));
        Console.WriteLine(TableCheckExporter.Build(issues, path, json));
        return issues.Count == 0 ? 0 : 1;
    }

    private static int DoCheckFences(string path, bool json)
    {
        if (!ValidateSource(path)) return 2;

        var issues = FenceChecker.Check(File.ReadAllText(path));
        Console.WriteLine(FenceCheckExporter.Build(issues, path, json));
        // Non-zero only for the rendering defect (an unclosed fence) so --check-fences can
        // gate a pipeline; a missing language tag is advisory and does not fail the gate.
        return issues.Any(i => i.Rule == FenceChecker.UnclosedRule) ? 1 : 0;
    }

    private static int DoCheckPaths(string path, bool json)
    {
        if (!ValidateSource(path)) return 2;

        var broken = LinkPathChecker.Check(File.ReadAllText(path), RelativeTargetResolver(path));
        Console.WriteLine(LinkPathCheckExporter.Build(broken, path, json));
        // Non-zero when a relative target is missing so --check-paths can gate a pipeline.
        return broken.Count == 0 ? 0 : 1;
    }

    private static int DoReview(string path, bool json)
    {
        if (!ValidateSource(path)) return 2;

        var report = ReviewReport.Compute(File.ReadAllText(path), RelativeTargetResolver(path));
        Console.WriteLine(ReviewReportExporter.Build(report, path, json));
        // Non-zero when any check found an issue so --review can gate a pipeline.
        return report.IssueCount == 0 ? 0 : 1;
    }

    /// <summary>
    /// Resolves a cleaned, document-relative target against the spec's own directory and
    /// reports whether it exists on disk (file or directory). Used by --check-paths and
    /// --review to validate relative link/image references.
    /// </summary>
    private static Func<string, bool> RelativeTargetResolver(string sourcePath)
    {
        var baseDir = Path.GetDirectoryName(Path.GetFullPath(sourcePath)) ?? ".";
        return relative =>
        {
            try
            {
                var full = Path.GetFullPath(Path.Combine(baseDir, relative));
                return File.Exists(full) || Directory.Exists(full);
            }
            catch
            {
                // A malformed target (illegal path characters) cannot resolve to a file.
                return false;
            }
        };
    }

    private static bool ValidateSource(string path)
    {
        if (!FileGuard.IsAllowed(path))
        {
            Console.Error.WriteLine($"Spectacle only opens .md and .markdown files. Refusing: {path}");
            return false;
        }
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"File not found: {path}");
            return false;
        }
        return true;
    }

    private static int DoRegister()
    {
        var exe = Environment.ProcessPath
            ?? throw new InvalidOperationException("Could not resolve own executable path.");
        new FileAssocInstaller(exe).Register();
        Console.WriteLine("Registered .md and .markdown to Spectacle for the current user.");
        return 0;
    }

    private static int DoUnregister()
    {
        var exe = Environment.ProcessPath ?? "";
        new FileAssocInstaller(exe).Unregister();
        Console.WriteLine("Removed Spectacle file associations for the current user.");
        return 0;
    }

    private static int Print(string text, int code) { Console.WriteLine(text); return code; }

    private static string GetVersion() =>
        typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0";
}
