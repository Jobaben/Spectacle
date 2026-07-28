using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Spectacle.Annotations;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

/// <summary>
/// End-to-end coverage of the headless review path that the CLI commands drive:
/// persist a comment via <see cref="AnnotationStore"/>, reload it, generate the
/// artifact, and write it to disk — the same sequence as Program.DoRevisionPlan /
/// DoReviewSummary, minus the console/validation shell.
/// </summary>
public class HeadlessReviewE2ETests : IDisposable
{
    private readonly string _tmp =
        Path.Combine(Path.GetTempPath(), "spectacle-e2e-" + Guid.NewGuid().ToString("N"));

    public HeadlessReviewE2ETests() => Directory.CreateDirectory(_tmp);

    public void Dispose()
    {
        try { Directory.Delete(_tmp, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    private (string sourcePath, string content, string sidecarRoot) SeedReview(string body)
    {
        var sourcePath = Path.Combine(_tmp, "spec.md");
        const string content = "# Spec\n\nThe service must respond fast.\n";
        File.WriteAllText(sourcePath, content);

        var block = new MdRenderer().Render(content).Blocks.First(b => b.Kind == "paragraph");
        var anchor = new BlockAnchor(block.Kind, block.Line, block.TextHash, block.OccurrenceIndex, block.OriginalText);
        var comment = new Comment("c1", anchor, block.OriginalText, body,
            new DateTime(2026, 6, 13, 9, 0, 0, DateTimeKind.Utc), null);

        var sidecarRoot = Path.Combine(_tmp, "annotations");
        new AnnotationStore(sourcePath, sidecarRoot)
            .Save(new AnnotationFile(1, sourcePath, "", new[] { comment }));

        return (sourcePath, content, sidecarRoot);
    }

    [Fact]
    public void Revision_plan_persists_loads_generates_and_writes()
    {
        var (sourcePath, content, sidecarRoot) = SeedReview("Quantify 'fast'.");

        var loaded = new AnnotationStore(sourcePath, sidecarRoot).Load();
        var plan = RevisionPlanGenerator.Generate(
            sourcePath, content, loaded,
            new DateTime(2026, 6, 13, 10, 0, 0, DateTimeKind.Utc), RevisionPlanFormat.Markdown);

        var outPath = Path.Combine(_tmp, "spec.revisions.md");
        File.WriteAllText(outPath, plan);

        File.Exists(outPath).Should().BeTrue();
        var written = File.ReadAllText(outPath);
        written.Should().Contain("# Revision plan for spec.md");
        written.Should().Contain("Quantify 'fast'.");
        written.Should().Contain("> The service must respond fast.");
    }

    [Fact]
    public void Review_summary_persists_loads_and_computes()
    {
        var (sourcePath, content, sidecarRoot) = SeedReview("note");

        var loaded = new AnnotationStore(sourcePath, sidecarRoot).Load();
        var summary = ReviewSummary.Compute(content, loaded);
        var json = ReviewSummaryExporter.Build(
            summary, sourcePath, new DateTime(2026, 6, 13, 10, 0, 0, DateTimeKind.Utc), RevisionPlanFormat.Json);

        var root = JsonDocument.Parse(json).RootElement;
        root.GetProperty("total").GetInt32().Should().Be(1);
        root.GetProperty("open").GetInt32().Should().Be(1);
        root.GetProperty("matched").GetInt32().Should().Be(1);
        root.GetProperty("orphaned").GetInt32().Should().Be(0);
    }
}
