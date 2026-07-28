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
        Spectacle — Markdown reader and quality gate for generated documents

        Open a .md file to read it. Pass a check flag instead and Spectacle answers headlessly,
        with an exit code a pipeline can branch on: 0 clean, 1 findings, 2 bad input. Anywhere
        <file> appears, "-" reads the document from standard input.

        THE GATE (start here)
          Spectacle.exe <file|dir> --gate [--json|--md|--sarif|--github|--junit]
                                         [--fail-on=error|warning] [--only=a,b|--skip=a,b]
                                         Run every check, grade each finding by the project's
                                         severity policy, and exit non-zero only for findings at
                                         or above the threshold. One document or a whole folder.
          Spectacle.exe <file> --fix-brief [out] [--json]
                                         Rewrite the gate's findings as revision instructions for
                                         the tool that authored the document, ordered so applying
                                         them never invalidates a later line number.
          Spectacle.exe --init-config [path] [--force]
                                         Scaffold a documented .spectacle.json and exit.

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
          Spectacle.exe <file> --check-bare-urls [--json] Report bare (auto-linked) URLs that should be descriptive links and exit (non-zero if any)
          Spectacle.exe <file> --check-heading-numbering [--json] Report manually numbered headings out of sequence and exit (non-zero if any)
          Spectacle.exe <file> --check-link-refs [--json] Report reference-style links whose label has no definition and exit (non-zero if any)
          Spectacle.exe <file> --check-footnotes [--json] Report footnote references with no matching definition and exit (non-zero if any)
          Spectacle.exe <file> --check-front-matter ["a,b"] [--config=<cfg>] [--json] Report a missing/unclosed/incomplete YAML metadata header (keys from the list or .spectacle.json) and exit (non-zero if any)
          Spectacle.exe <file> --check-ai-artifacts [--json] Report generation residue — unfilled template tokens, chat framing, truncation markers, placeholder link targets — and exit (non-zero if any)
          Spectacle.exe <file> --check-mermaid [--json] Report Mermaid diagrams that cannot be drawn (empty, unknown type) or carry no description, and exit (non-zero if any)
          Spectacle.exe <file> --review [--json|--sarif|--md|--github|--junit] [--only=a,b|--skip=a,b] Run all checks and exit (non-zero if any issues)
          Spectacle.exe <dir> --review [--json|--sarif|--md|--github|--junit] Review every .md/.markdown spec under a folder and exit
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
            CliCommand.CheckBareUrls bareUrls => DoCheckBareUrls(bareUrls.Path, bareUrls.Json),
            CliCommand.CheckHeadingNumbering headingNum => DoCheckHeadingNumbering(headingNum.Path, headingNum.Json),
            CliCommand.CheckLinkRefs linkRefs => DoCheckLinkRefs(linkRefs.Path, linkRefs.Json),
            CliCommand.CheckFootnotes footnotes => DoCheckFootnotes(footnotes.Path, footnotes.Json),
            CliCommand.CheckFrontMatter fm => DoCheckFrontMatter(fm.Path, fm.Required, fm.Json, fm.ConfigPath),
            CliCommand.CheckAiArtifacts ai => DoCheckAiArtifacts(ai.Path, ai.Json),
            CliCommand.CheckMermaid mermaid => DoCheckMermaid(mermaid.Path, mermaid.Json),
            CliCommand.Gate gate => DoGate(gate),
            CliCommand.FixBrief brief => DoFixBrief(brief),
            CliCommand.Review review => DoReview(
                review.Path, review.Json, review.Baseline, review.Sarif,
                review.Only ?? Array.Empty<string>(), review.Skip ?? Array.Empty<string>(), review.Md,
                review.Github, review.Junit),
            CliCommand.Open open => DoOpen(open.Path),
            _ => Print(UsageText, 0),
        };
    }

    private static int DoOpen(string path)
    {
        // The reader watches a file for changes and re-renders; a stream has nothing to watch, so
        // standard input is a headless-only source.
        if (IsStdin(path))
        {
            Console.Error.WriteLine(
                "Reading from standard input is for headless checks only — the reader needs a file to watch. " +
                "Pass a path, or add a check flag such as --gate.");
            return 2;
        }

        if (!ValidateSource(path)) return 2;

        var app = new App();
        var window = new MainWindow(path);
        return app.Run(window);
    }

    private static int DoStats(string path)
    {
        if (!ValidateSource(path)) return 2;

        var stats = DocumentStats.Compute(ReadBody(path));
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
        var html = HtmlExporter.FromMarkdown(ReadRaw(path), PreviewTheme.Dark, title);
        var target = outputPath ?? Path.ChangeExtension(path, ".html");
        File.WriteAllText(target, html);
        Console.WriteLine($"Exported {Path.GetFullPath(target)}");
        return 0;
    }

    private static int DoRevisionPlan(string path, string? outputPath, bool json, bool unresolvedOnly)
    {
        if (!ValidateSource(path)) return 2;

        var content = ReadRaw(path);
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

        var content = ReadRaw(path);
        var annotations = new AnnotationStore(path).Load();
        var summary = ReviewSummary.Compute(content, annotations);
        var format = json ? RevisionPlanFormat.Json : RevisionPlanFormat.Markdown;
        Console.WriteLine(ReviewSummaryExporter.Build(summary, path, DateTime.UtcNow, format));
        return 0;
    }

    private static int DoLint(string path, bool json)
    {
        if (!ValidateSource(path)) return 2;

        var findings = SpecLinter.Lint(ReadBody(path));
        Console.WriteLine(SpecLintExporter.Build(findings, path, json));
        // Non-zero when issues are found so --lint can gate a pipeline.
        return findings.Count == 0 ? 0 : 1;
    }

    private static int DoOutline(string path, bool json)
    {
        if (!ValidateSource(path)) return 2;

        var outline = new MdRenderer().Render(ReadRaw(path)).Outline;
        Console.WriteLine(OutlineExporter.Build(outline, path, json));
        return 0;
    }

    private static int DoChecklist(string path, bool json)
    {
        if (!ValidateSource(path)) return 2;

        var items = ChecklistAnalyzer.Analyze(ReadBody(path));
        Console.WriteLine(ChecklistExporter.Build(items, path, json));
        return 0;
    }

    private static int DoCheckLinks(string path, bool json)
    {
        if (!ValidateSource(path)) return 2;

        var broken = LinkChecker.Check(ReadBody(path));
        Console.WriteLine(LinkCheckExporter.Build(broken, path, json));
        // Non-zero when links are broken so --check-links can gate a pipeline.
        return broken.Count == 0 ? 0 : 1;
    }

    private static int DoDiff(string path, string otherPath, bool json)
    {
        if (!ValidateSource(path)) return 2;
        if (!ValidateSource(otherPath)) return 2;

        // The current file is the revised version; <other> is the baseline.
        var diff = SpecDiff.Compare(ReadRaw(otherPath), ReadRaw(path));
        Console.WriteLine(SpecDiffExporter.Build(diff, path, otherPath, json));
        return 0;
    }

    private static int DoCheckStructure(string path, bool json)
    {
        if (!ValidateSource(path)) return 2;

        var findings = StructureChecker.Check(ReadBody(path));
        Console.WriteLine(StructureCheckExporter.Build(findings, path, json));
        // Non-zero when issues are found so --check-structure can gate a pipeline.
        return findings.Count == 0 ? 0 : 1;
    }

    private static int DoCheckTables(string path, bool json)
    {
        if (!ValidateSource(path)) return 2;

        var issues = TableChecker.Check(ReadBody(path));
        Console.WriteLine(TableCheckExporter.Build(issues, path, json));
        return issues.Count == 0 ? 0 : 1;
    }

    private static int DoCheckFences(string path, bool json)
    {
        if (!ValidateSource(path)) return 2;

        var issues = FenceChecker.Check(ReadBody(path));
        Console.WriteLine(FenceCheckExporter.Build(issues, path, json));
        // Non-zero only for the rendering defect (an unclosed fence) so --check-fences can
        // gate a pipeline; a missing language tag is advisory and does not fail the gate.
        return issues.Any(i => i.Rule == FenceChecker.UnclosedRule) ? 1 : 0;
    }

    private static int DoCheckPaths(string path, bool json)
    {
        if (!ValidateSource(path)) return 2;

        var broken = LinkPathChecker.Check(ReadBody(path), RelativeTargetResolver(path));
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

        var missing = RequiredSectionsChecker.Check(ReadBody(path), names);
        Console.WriteLine(RequiredSectionsCheckExporter.Build(missing, names.Count, path, json));
        // Non-zero when a required section is absent so --check-sections can gate a pipeline.
        return missing.Count == 0 ? 0 : 1;
    }

    private static int DoCheckDuplication(string path, bool json)
    {
        if (!ValidateSource(path)) return 2;

        var duplicates = DuplicateBlockChecker.Check(ReadBody(path));
        Console.WriteLine(DuplicateBlockCheckExporter.Build(duplicates, path, json));
        // Non-zero when a block repeats so --check-duplication can gate a pipeline.
        return duplicates.Count == 0 ? 0 : 1;
    }

    private static int DoCheckAltText(string path, bool json)
    {
        if (!ValidateSource(path)) return 2;

        var images = AltTextChecker.Check(ReadBody(path));
        Console.WriteLine(AltTextCheckExporter.Build(images, path, json));
        // Non-zero when an image lacks alt text so --check-alt-text can gate a pipeline.
        return images.Count == 0 ? 0 : 1;
    }

    private static int DoCheckLinkText(string path, bool json)
    {
        if (!ValidateSource(path)) return 2;

        var links = LinkTextChecker.Check(ReadBody(path));
        Console.WriteLine(LinkTextCheckExporter.Build(links, path, json));
        // Non-zero when a link's text says nothing about its destination so this can gate a pipeline.
        return links.Count == 0 ? 0 : 1;
    }

    private static int DoCheckEmphasisHeading(string path, bool json)
    {
        if (!ValidateSource(path)) return 2;

        var findings = EmphasisHeadingChecker.Check(ReadBody(path));
        Console.WriteLine(EmphasisHeadingCheckExporter.Build(findings, path, json));
        // Non-zero when a paragraph is used as a fake heading so this can gate a pipeline.
        return findings.Count == 0 ? 0 : 1;
    }

    private static int DoCheckProse(string path, bool json)
    {
        if (!ValidateSource(path)) return 2;

        var findings = ProseChecker.Check(ReadBody(path));
        Console.WriteLine(ProseCheckExporter.Build(findings, path, json));
        // Advisory only: hedging is a judgement call, so this never gates a pipeline.
        return 0;
    }

    private static int DoCheckToc(string path, bool json)
    {
        if (!ValidateSource(path)) return 2;

        var issues = TocChecker.Check(ReadBody(path));
        Console.WriteLine(TocCheckExporter.Build(issues, path, json));
        // Non-zero when the TOC drifts from the headings so --check-toc can gate a pipeline.
        return issues.Count == 0 ? 0 : 1;
    }

    private static int DoCheckNumbering(string path, bool json)
    {
        if (!ValidateSource(path)) return 2;

        var issues = NumberingChecker.Check(ReadBody(path));
        Console.WriteLine(NumberingCheckExporter.Build(issues, path, json));
        // Non-zero when an ordered list is out of sequence so --check-numbering can gate a pipeline.
        return issues.Count == 0 ? 0 : 1;
    }

    private static int DoCheckBareUrls(string path, bool json)
    {
        if (!ValidateSource(path)) return 2;

        var urls = BareUrlChecker.Check(ReadBody(path));
        Console.WriteLine(BareUrlCheckExporter.Build(urls, path, json));
        // Non-zero when a bare URL is found so --check-bare-urls can gate a pipeline.
        return urls.Count == 0 ? 0 : 1;
    }

    private static int DoCheckHeadingNumbering(string path, bool json)
    {
        if (!ValidateSource(path)) return 2;

        var issues = HeadingNumberingChecker.Check(ReadBody(path));
        Console.WriteLine(HeadingNumberingCheckExporter.Build(issues, path, json));
        // Non-zero when a numbered-heading run is out of sequence so this can gate a pipeline.
        return issues.Count == 0 ? 0 : 1;
    }

    private static int DoCheckLinkRefs(string path, bool json)
    {
        if (!ValidateSource(path)) return 2;

        var refs = LinkRefChecker.Check(ReadBody(path));
        Console.WriteLine(LinkRefCheckExporter.Build(refs, path, json));
        // Non-zero when a reference-style link has no definition so this can gate a pipeline.
        return refs.Count == 0 ? 0 : 1;
    }

    private static int DoCheckFootnotes(string path, bool json)
    {
        if (!ValidateSource(path)) return 2;

        var footnotes = FootnoteChecker.Check(ReadBody(path));
        Console.WriteLine(FootnoteCheckExporter.Build(footnotes, path, json));
        // Non-zero when a footnote reference has no definition so this can gate a pipeline.
        return footnotes.Count == 0 ? 0 : 1;
    }

    private static int DoCheckFrontMatter(string path, string? required, bool json, string? configPath)
    {
        if (!ValidateSource(path)) return 2;

        // An inline list wins; otherwise the template comes from `requiredFrontMatter` in the
        // resolved config — the same precedence --check-sections uses for its section template.
        var keys = required is not null
            ? FrontMatterChecker.ParseRequired(required)
            : ConfigFor(path, configPath).RequiredFrontMatter;

        // The one check that reads the raw document: its subject is the header the others skip.
        var content = ReadRaw(path);
        var findings = FrontMatterChecker.Check(content, keys);
        Console.WriteLine(FrontMatterCheckExporter.Build(
            findings, FrontMatter.Parse(content), DisplayPath(path), json));
        // Non-zero when the metadata contract is broken so this can gate a pipeline.
        return findings.Count == 0 ? 0 : 1;
    }

    private static int DoCheckAiArtifacts(string path, bool json)
    {
        if (!ValidateSource(path)) return 2;

        var artifacts = AiArtifactChecker.Check(ReadBody(path));
        Console.WriteLine(AiArtifactCheckExporter.Build(artifacts, DisplayPath(path), json));
        // Non-zero when generation residue is found so this can gate a pipeline.
        return artifacts.Count == 0 ? 0 : 1;
    }

    private static int DoCheckMermaid(string path, bool json)
    {
        if (!ValidateSource(path)) return 2;

        var issues = MermaidChecker.Check(ReadBody(path));
        Console.WriteLine(MermaidCheckExporter.Build(issues, DisplayPath(path), json));
        // Non-zero when a diagram cannot be drawn or carries no description, so this gates a pipeline.
        return issues.Count == 0 ? 0 : 1;
    }

    /// <summary>
    /// The graded gate: the same checks as <c>--review</c>, but every finding reported at its
    /// configured severity and only findings at or above the threshold failing the run. This is the
    /// command a workflow calls — one exit code, and a verdict that says what it did and did not check.
    /// </summary>
    private static int DoGate(CliCommand.Gate gate)
    {
        var only = gate.Only ?? Array.Empty<string>();
        var skip = gate.Skip ?? Array.Empty<string>();
        WarnUnknownChecks(only.Concat(skip));

        var entries = GateEntries(gate.Path, only, skip);
        if (entries is null) return 2;
        if (entries.Count == 0) return 0;

        var policy = PolicyFor(gate.Path, gate.ConfigPath, gate.FailOn);
        var batch = new GateBatch(entries
            .Select(e => GateVerdict.Compute(e.Name, e.Report, policy, FrontMatter.Parse(e.Content)))
            .ToList());

        var reportEntries = entries.Select(e => new BatchReviewEntry(e.Name, e.Report)).ToList();
        Console.WriteLine(
            gate.Sarif ? SarifExporter.Build(reportEntries, GetVersion(), policy)
            : gate.Github ? GitHubAnnotationExporter.Build(reportEntries, policy)
            : gate.Junit ? JUnitExporter.Build(reportEntries, policy)
            : GateExporter.Build(batch, GetVersion(), gate.Json, gate.Md));

        // The gate's whole contract: non-zero exactly when something at or above the threshold was
        // found. A warning under an error threshold is reported and does not stop the pipeline.
        return batch.Passed ? 0 : 1;
    }

    /// <summary>
    /// Writes the gate's findings back out as revision instructions for the tool that authored the
    /// document — the step that turns a failed gate into an actionable next prompt.
    /// </summary>
    private static int DoFixBrief(CliCommand.FixBrief brief)
    {
        if (!ValidateSource(brief.Path)) return 2;

        var only = brief.Only ?? Array.Empty<string>();
        var skip = brief.Skip ?? Array.Empty<string>();
        WarnUnknownChecks(only.Concat(skip));

        var content = ReadRaw(brief.Path);
        var display = DisplayPath(brief.Path);
        var report = ReviewReport.Compute(
            content, RelativeTargetResolver(brief.Path), RequiredSectionsFor(brief.Path),
            ChecksFor(brief.Path, only, skip), RequiredFrontMatterFor(brief.Path));

        var policy = PolicyFor(brief.Path, brief.ConfigPath, brief.FailOn);
        var verdict = GateVerdict.Compute(display, report, policy, FrontMatter.Parse(content));
        var text = FixBriefExporter.Build(verdict, brief.Json);

        if (brief.OutputPath is null)
        {
            Console.WriteLine(text);
        }
        else
        {
            File.WriteAllText(brief.OutputPath, text);
            Console.WriteLine($"Wrote {Path.GetFullPath(brief.OutputPath)}");
        }

        // The brief describes what to fix, so its exit code mirrors the gate it reports on: a
        // pipeline can write the brief and branch on the same call.
        return verdict.Passed ? 0 : 1;
    }

    /// <summary>
    /// One document a gate run covers: the name to report it under, its raw text (the gate echoes the
    /// front matter, so it needs the header), and its review.
    /// </summary>
    private sealed record GateSource(string Name, string Content, ReviewReport Report);

    /// <summary>
    /// The documents a gate run covers: one for a file, one per document for a directory.
    /// Returns <c>null</c> when the source is invalid (exit 2) and an empty list when a directory
    /// holds no documents (nothing to gate, which is not a failure).
    /// </summary>
    private static IReadOnlyList<GateSource>? GateEntries(
        string path, IReadOnlyList<string> only, IReadOnlyList<string> skip)
    {
        if (Directory.Exists(path))
        {
            var specs = BatchReview.EnumerateSpecs(path);
            if (specs.Count == 0)
            {
                Console.Error.WriteLine($"No .md or .markdown documents found under {Path.GetFullPath(path)}");
                return Array.Empty<GateSource>();
            }

            return specs.Select(ReadGateSource).ToList();
        }

        if (!ValidateSource(path)) return null;
        return new[] { ReadGateSource(path) };

        GateSource ReadGateSource(string source)
        {
            var content = ReadRaw(source);
            return new GateSource(
                DisplayPath(source), content,
                ReviewReport.Compute(
                    content, RelativeTargetResolver(source), RequiredSectionsFor(source),
                    ChecksFor(source, only, skip), RequiredFrontMatterFor(source)));
        }
    }

    private static int DoReview(
        string path, bool json, string? baseline, bool sarif,
        IReadOnlyList<string> only, IReadOnlyList<string> skip, bool md,
        bool github, bool junit)
    {
        // A typo'd check id would otherwise be silently ignored and the check keep gating,
        // confusingly; warn (don't fail) so the misuse is visible.
        WarnUnknownChecks(only.Concat(skip));

        // A directory argument reviews every spec under it in one shot.
        if (Directory.Exists(path)) return DoBatchReview(path, json, sarif, only, skip, md, github, junit);

        if (!ValidateSource(path)) return 2;

        // With a baseline, report what the revision fixed / introduced / still carries.
        // (The baseline delta is its own shape; --sarif / --md apply to the plain verdict only.)
        if (baseline is not null) return DoReviewDelta(path, baseline, json, only, skip);

        var report = ReviewReport.Compute(
            ReadRaw(path), RelativeTargetResolver(path), RequiredSectionsFor(path),
            ChecksFor(path, only, skip), RequiredFrontMatterFor(path));
        // A single file is a one-entry batch, so every set-shaped format (SARIF, CI annotations,
        // JUnit) takes the same path as a folder review.
        var entries = new[] { new BatchReviewEntry(DisplayPath(path), report) };
        Console.WriteLine(
            sarif ? SarifExporter.Build(entries, GetVersion())
            : github ? GitHubAnnotationExporter.Build(entries)
            : junit ? JUnitExporter.Build(entries)
            : ReviewReportExporter.Build(report, DisplayPath(path), json, md));
        // Non-zero when any check found an issue so --review can gate a pipeline.
        return report.IssueCount == 0 ? 0 : 1;
    }

    private static int DoBatchReview(
        string directory, bool json, bool sarif, IReadOnlyList<string> only, IReadOnlyList<string> skip,
        bool md, bool github, bool junit)
    {
        var specs = BatchReview.EnumerateSpecs(directory);
        if (specs.Count == 0)
        {
            Console.Error.WriteLine($"No .md or .markdown specs found under {Path.GetFullPath(directory)}");
            return 0;
        }

        var result = BatchReview.Compute(
            specs.Select(p => (p, ReadRaw(p), RelativeTargetResolver(p), RequiredSectionsFor(p),
                ChecksFor(p, only, skip), RequiredFrontMatterFor(p))));
        Console.WriteLine(
            sarif ? SarifExporter.Build(result.Entries, GetVersion())
            : github ? GitHubAnnotationExporter.Build(result.Entries)
            : junit ? JUnitExporter.Build(result.Entries)
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
            ReadRaw(path), RelativeTargetResolver(path), RequiredSectionsFor(path),
            ChecksFor(path, only, skip), RequiredFrontMatterFor(path));
        var baseline = ReviewReport.Compute(
            ReadRaw(baselinePath), RelativeTargetResolver(baselinePath), RequiredSectionsFor(baselinePath),
            ChecksFor(baselinePath, only, skip), RequiredFrontMatterFor(baselinePath));
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
        ReviewChecks.Resolve(only, skip, ConfigFor(sourcePath, null).DisabledChecks);

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
        ConfigFor(sourcePath, null).RequiredSections;

    /// <summary>
    /// Resolves the required front-matter template for a document: the <c>requiredFrontMatter</c>
    /// of the nearest <c>.spectacle.json</c> above it, or an empty list when no config resolves —
    /// so a project that does not use metadata headers is unaffected.
    /// </summary>
    private static IReadOnlyList<string> RequiredFrontMatterFor(string sourcePath) =>
        ConfigFor(sourcePath, null).RequiredFrontMatter;

    /// <summary>
    /// The project config governing a document, discovered from the document's own location (or the
    /// working directory, for standard input) unless an explicit <c>--config=</c> path overrides it.
    /// </summary>
    private static SpectacleConfig ConfigFor(string sourcePath, string? explicitConfigPath) =>
        ConfigLocator.Resolve(ConfigAnchor(sourcePath), explicitConfigPath);

    /// <summary>
    /// Resolves the grading policy for a document: the project's <c>severity</c> map and
    /// <c>failOn</c> threshold, with a single run's <c>--fail-on</c> taking precedence. A typo in
    /// either is warned about rather than guessed at, so a grade that will never apply is visible.
    /// </summary>
    private static GatePolicy PolicyFor(string sourcePath, string? explicitConfigPath, string? failOnFlag)
    {
        var config = ConfigFor(sourcePath, explicitConfigPath);

        var badValues = GatePolicy.UnknownSeverities(config.Severity);
        if (badValues.Count != 0)
            Console.Error.WriteLine(
                $"Ignoring unrecognized severity value(s) in config: {string.Join(", ", badValues)}. " +
                $"Accepted: {GateSeverities.Accepted}.");

        var badKeys = GatePolicy.UnknownRules(config.Severity);
        if (badKeys.Count != 0)
            Console.Error.WriteLine(
                $"Severity set for unknown check/rule id(s): {string.Join(", ", badKeys)}. " +
                "These grades will never apply.");

        var policy = GatePolicy.Create(config.Severity, config.FailOn);
        if (failOnFlag is null) return policy;

        var threshold = GateSeverities.Parse(failOnFlag);
        if (threshold is null)
        {
            Console.Error.WriteLine(
                $"Unrecognized --fail-on value '{failOnFlag}'; keeping {policy.FailOn.ToString().ToLowerInvariant()}. " +
                $"Accepted: {GateSeverities.Accepted}.");
            return policy;
        }
        return policy.WithFailOn(threshold.Value);
    }

    /// <summary>
    /// The conventional name for standard input. A generator can pipe a document straight into a
    /// check — <c>my-agent write | Spectacle.exe - --gate</c> — so gating an artifact does not
    /// require writing it to disk first.
    /// </summary>
    private const string StdinPath = "-";

    private static bool IsStdin(string path) => path == StdinPath;

    /// <summary>The document exactly as authored, front matter included.</summary>
    private static string ReadRaw(string path) =>
        IsStdin(path) ? Console.In.ReadToEnd() : File.ReadAllText(path);

    /// <summary>
    /// The document's prose body, with any YAML metadata header blanked out. Every content check
    /// reads this: the header is data, and a CommonMark parser would otherwise read its closing
    /// <c>---</c> as a setext heading and put a phantom h2 in the document. Blanking preserves the
    /// line count, so a finding's line number still points at the right line of the real file.
    /// </summary>
    private static string ReadBody(string path) => FrontMatter.Strip(ReadRaw(path));

    /// <summary>
    /// The name a report should call this document. Standard input has no filename, so it gets a
    /// readable stand-in rather than a bare "-" in every heading.
    /// </summary>
    private static string DisplayPath(string path) => IsStdin(path) ? "(stdin)" : path;

    /// <summary>
    /// The path config discovery should walk up from. Standard input has no location of its own, so
    /// it inherits the project config of the working directory — which is the directory the
    /// pipeline step is running in, and so the project whose rules should apply.
    /// </summary>
    private static string ConfigAnchor(string path) =>
        IsStdin(path) ? Path.Combine(Directory.GetCurrentDirectory(), "stdin.md") : path;

    private static bool ValidateSource(string path)
    {
        // Standard input carries no extension to check and no file to find.
        if (IsStdin(path)) return true;

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
