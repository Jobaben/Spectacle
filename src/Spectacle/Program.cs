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
          Spectacle.exe <file> --check-sections ["A,B,C"] [--config=<cfg>] [--json] Report required sections missing from the spec (list or .spectacle.json) and exit (non-zero if any)
          Spectacle.exe <file> --check-duplication [--json] Report blocks repeated verbatim elsewhere in the spec and exit (non-zero if any)
          Spectacle.exe <file> --check-alt-text [--json] Report images missing alt text and exit (non-zero if any)
          Spectacle.exe <file> --check-link-text [--json] Report links whose text names no destination and exit (non-zero if any)
          Spectacle.exe <file> --check-emphasis-heading [--json] Report emphasized lines used as fake headings and exit (non-zero if any)
          Spectacle.exe <file> --check-prose [--json] Report vague/hedging language (advisory, always exits 0)
          Spectacle.exe <file> --check-toc [--json] Report a table of contents out of sync with the headings and exit (non-zero if any)
          Spectacle.exe <file> --check-numbering [--json] Report ordered lists whose numbering is out of sequence and exit (non-zero if any)
          Spectacle.exe <file> --review [--json|--sarif|--md] [--only=a,b|--skip=a,b] Run all checks and exit (non-zero if any issues)
          Spectacle.exe <dir> --review [--json|--sarif|--md] Review every .md/.markdown spec under a folder and exit
          Spectacle.exe <file> --review --baseline <old> [--json] Show what a revision fixed/introduced vs an older version and exit
          Spectacle.exe --init-config [path] [--force] Scaffold a documented .spectacle.json (refuses to overwrite without --force) and exit
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
            CliCommand.InitConfig init => DoInitConfig(init.Path, init.Force),
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
            CliCommand.CheckSections sections => DoCheckSections(sections.Path, sections.Required, sections.Json, sections.ConfigPath),
            CliCommand.CheckDuplication dup => DoCheckDuplication(dup.Path, dup.Json),
            CliCommand.CheckAltText alt => DoCheckAltText(alt.Path, alt.Json),
            CliCommand.CheckLinkText linkText => DoCheckLinkText(linkText.Path, linkText.Json),
            CliCommand.CheckEmphasisHeading emphasis => DoCheckEmphasisHeading(emphasis.Path, emphasis.Json),
            CliCommand.CheckProse prose => DoCheckProse(prose.Path, prose.Json),
            CliCommand.CheckToc toc => DoCheckToc(toc.Path, toc.Json),
            CliCommand.CheckNumbering numbering => DoCheckNumbering(numbering.Path, numbering.Json),
            CliCommand.Review review => DoReview(
                review.Path, review.Json, review.Baseline, review.Sarif,
                review.Only ?? Array.Empty<string>(), review.Skip ?? Array.Empty<string>(), review.Md),
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

    private static int DoCheckSections(string path, string? required, bool json, string? configPath)
    {
        if (!ValidateSource(path)) return 2;

        // An inline list wins; otherwise the required sections come from .spectacle.json
        // (an explicit --config=<path>, else the nearest config discovered above the spec).
        IReadOnlyList<string> names;
        if (required is not null)
            names = RequiredSectionsChecker.ParseRequired(required);
        else
            names = ConfigLocator.Resolve(path, configPath).RequiredSections;

        if (names.Count == 0)
        {
            Console.Error.WriteLine(
                "No required sections given. Pass a comma-separated list or declare " +
                "\"requiredSections\" in a .spectacle.json config.");
            return 2;
        }

        var missing = RequiredSectionsChecker.Check(File.ReadAllText(path), names);
        Console.WriteLine(RequiredSectionsCheckExporter.Build(missing, names.Count, path, json));
        // Non-zero when a required section is absent so --check-sections can gate a pipeline.
        return missing.Count == 0 ? 0 : 1;
    }

    private static int DoCheckDuplication(string path, bool json)
    {
        if (!ValidateSource(path)) return 2;

        var duplicates = DuplicateBlockChecker.Check(File.ReadAllText(path));
        Console.WriteLine(DuplicateBlockCheckExporter.Build(duplicates, path, json));
        // Non-zero when a block repeats so --check-duplication can gate a pipeline.
        return duplicates.Count == 0 ? 0 : 1;
    }

    private static int DoCheckAltText(string path, bool json)
    {
        if (!ValidateSource(path)) return 2;

        var images = AltTextChecker.Check(File.ReadAllText(path));
        Console.WriteLine(AltTextCheckExporter.Build(images, path, json));
        // Non-zero when an image lacks alt text so --check-alt-text can gate a pipeline.
        return images.Count == 0 ? 0 : 1;
    }

    private static int DoCheckLinkText(string path, bool json)
    {
        if (!ValidateSource(path)) return 2;

        var links = LinkTextChecker.Check(File.ReadAllText(path));
        Console.WriteLine(LinkTextCheckExporter.Build(links, path, json));
        // Non-zero when a link's text says nothing about its destination so this can gate a pipeline.
        return links.Count == 0 ? 0 : 1;
    }

    private static int DoCheckEmphasisHeading(string path, bool json)
    {
        if (!ValidateSource(path)) return 2;

        var findings = EmphasisHeadingChecker.Check(File.ReadAllText(path));
        Console.WriteLine(EmphasisHeadingCheckExporter.Build(findings, path, json));
        // Non-zero when a paragraph is used as a fake heading so this can gate a pipeline.
        return findings.Count == 0 ? 0 : 1;
    }

    private static int DoCheckProse(string path, bool json)
    {
        if (!ValidateSource(path)) return 2;

        var findings = ProseChecker.Check(File.ReadAllText(path));
        Console.WriteLine(ProseCheckExporter.Build(findings, path, json));
        // Advisory only: hedging is a judgement call, so this never gates a pipeline.
        return 0;
    }

    private static int DoCheckToc(string path, bool json)
    {
        if (!ValidateSource(path)) return 2;

        var issues = TocChecker.Check(File.ReadAllText(path));
        Console.WriteLine(TocCheckExporter.Build(issues, path, json));
        // Non-zero when the TOC drifts from the headings so --check-toc can gate a pipeline.
        return issues.Count == 0 ? 0 : 1;
    }

    private static int DoCheckNumbering(string path, bool json)
    {
        if (!ValidateSource(path)) return 2;

        var issues = NumberingChecker.Check(File.ReadAllText(path));
        Console.WriteLine(NumberingCheckExporter.Build(issues, path, json));
        // Non-zero when an ordered list is out of sequence so --check-numbering can gate a pipeline.
        return issues.Count == 0 ? 0 : 1;
    }

    private static int DoReview(
        string path, bool json, string? baseline, bool sarif,
        IReadOnlyList<string> only, IReadOnlyList<string> skip, bool md)
    {
        // A typo'd check id would otherwise be silently ignored and the check keep gating,
        // confusingly; warn (don't fail) so the misuse is visible.
        WarnUnknownChecks(only.Concat(skip));

        // A directory argument reviews every spec under it in one shot.
        if (Directory.Exists(path)) return DoBatchReview(path, json, sarif, only, skip, md);

        if (!ValidateSource(path)) return 2;

        // With a baseline, report what the revision fixed / introduced / still carries.
        // (The baseline delta is its own shape; --sarif / --md apply to the plain verdict only.)
        if (baseline is not null) return DoReviewDelta(path, baseline, json, only, skip);

        var report = ReviewReport.Compute(
            File.ReadAllText(path), RelativeTargetResolver(path), RequiredSectionsFor(path),
            ChecksFor(path, only, skip));
        // A single file is a one-entry batch, so SARIF takes the same path as a folder review.
        Console.WriteLine(sarif
            ? SarifExporter.Build(new[] { new BatchReviewEntry(path, report) }, GetVersion())
            : ReviewReportExporter.Build(report, path, json, md));
        // Non-zero when any check found an issue so --review can gate a pipeline.
        return report.IssueCount == 0 ? 0 : 1;
    }

    private static int DoBatchReview(
        string directory, bool json, bool sarif, IReadOnlyList<string> only, IReadOnlyList<string> skip, bool md)
    {
        var specs = BatchReview.EnumerateSpecs(directory);
        if (specs.Count == 0)
        {
            Console.Error.WriteLine($"No .md or .markdown specs found under {Path.GetFullPath(directory)}");
            return 0;
        }

        var result = BatchReview.Compute(
            specs.Select(p => (p, File.ReadAllText(p), RelativeTargetResolver(p), RequiredSectionsFor(p), ChecksFor(p, only, skip))));
        Console.WriteLine(sarif
            ? SarifExporter.Build(result.Entries, GetVersion())
            : BatchReviewExporter.Build(result, directory, json, md));
        // Non-zero when any spec in the set has an issue so a batch can gate a pipeline.
        return result.TotalIssues == 0 ? 0 : 1;
    }

    private static int DoReviewDelta(
        string path, string baselinePath, bool json, IReadOnlyList<string> only, IReadOnlyList<string> skip)
    {
        if (!ValidateSource(baselinePath)) return 2;

        // The same selection applies to both versions, so a check turned off is off on both
        // sides of the delta — a skipped check never reads as "fixed" or "new".
        var revised = ReviewReport.Compute(
            File.ReadAllText(path), RelativeTargetResolver(path), RequiredSectionsFor(path),
            ChecksFor(path, only, skip));
        var baseline = ReviewReport.Compute(
            File.ReadAllText(baselinePath), RelativeTargetResolver(baselinePath), RequiredSectionsFor(baselinePath),
            ChecksFor(baselinePath, only, skip));
        var delta = ReviewDelta.Compute(baseline, revised);
        Console.WriteLine(ReviewDeltaExporter.Build(delta, path, baselinePath, json));
        // Non-zero when the revision still carries any issue (new or persisting), so the
        // baseline view gates on the same "spec must be clean" rule as a plain --review.
        return delta.RemainingIssueCount == 0 ? 0 : 1;
    }

    /// <summary>
    /// Resolves the gating-check selection for a spec: the global CLI <c>--only</c>/<c>--skip</c>
    /// combined with the project's nearest <c>.spectacle.json</c> <c>disabledChecks</c>, so a team
    /// declares its gate once and a single run can still narrow it.
    /// </summary>
    private static ReviewChecks ChecksFor(string sourcePath, IReadOnlyList<string> only, IReadOnlyList<string> skip) =>
        ReviewChecks.Resolve(only, skip, ConfigLocator.Resolve(sourcePath, null).DisabledChecks);

    private static void WarnUnknownChecks(IEnumerable<string> requested)
    {
        var unknown = ReviewChecks.Unknown(requested);
        if (unknown.Count != 0)
            Console.Error.WriteLine(
                $"Unknown check id(s) ignored: {string.Join(", ", unknown)}. " +
                $"Valid checks: {string.Join(", ", ReviewChecks.All)}.");
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

    /// <summary>
    /// Resolves the required-section template a <c>--review</c> should enforce for a spec:
    /// the <c>requiredSections</c> of the nearest <c>.spectacle.json</c> above it, or an empty
    /// list when no config resolves (so a spec reviewed without a template is unaffected).
    /// </summary>
    private static IReadOnlyList<string> RequiredSectionsFor(string sourcePath) =>
        ConfigLocator.Resolve(sourcePath, null).RequiredSections;

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

    private static int DoInitConfig(string? pathArg, bool force)
    {
        var target = ConfigScaffold.ResolveTargetPath(pathArg, Directory.Exists);
        var full = Path.GetFullPath(target);

        // Overwriting a hand-tuned config is destructive, so refuse unless the caller insists.
        if (File.Exists(full) && !force)
        {
            Console.Error.WriteLine($"{full} already exists. Pass --force to overwrite.");
            return 2;
        }

        var dir = Path.GetDirectoryName(full);
        if (dir is not null) Directory.CreateDirectory(dir);
        File.WriteAllText(full, ConfigScaffold.Template());
        Console.WriteLine($"Wrote {full}");
        return 0;
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
