using System;
using System.Linq;
using FluentAssertions;
using Spectacle.Checks;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class FindingStreamTests
{
    private static ReviewReport Report(
        SpecLintFinding[]? lint = null,
        FrontMatterFinding[]? frontMatter = null,
        AiArtifact[]? artifacts = null,
        ProseFinding[]? prose = null,
        FenceIssue[]? fenceWarnings = null) => new(
        Lint: lint ?? Array.Empty<SpecLintFinding>(),
        Structure: Array.Empty<StructureFinding>(),
        Links: Array.Empty<BrokenLink>(),
        Tables: Array.Empty<TableIssue>(),
        Fences: Array.Empty<FenceIssue>(),
        Paths: Array.Empty<BrokenPath>(),
        Duplication: Array.Empty<DuplicateBlock>(),
        AltText: Array.Empty<ImageWithoutAlt>(),
        EmphasisHeadings: Array.Empty<EmphasisHeading>(),
        Sections: Array.Empty<MissingSection>(),
        ChecklistTotal: 0,
        ChecklistDone: 0,
        Prose: prose,
        FenceWarnings: fenceWarnings,
        FrontMatterFindings: frontMatter,
        AiArtifacts: artifacts);

    [Fact]
    public void A_clean_report_yields_nothing()
    {
        FindingStream.All(Report()).Should().BeEmpty();
        FindingStream.Gating(Report()).Should().BeEmpty();
        FindingStream.Advisory(Report()).Should().BeEmpty();
    }

    [Fact]
    public void Flattens_a_gating_finding_with_its_rule_id_severity_and_line()
    {
        var stream = FindingStream.Gating(
            Report(lint: new[] { new SpecLintFinding("placeholder", 4, "placeholder marker 'TODO'") }));

        stream.Should().HaveCount(1);
        stream[0].CheckId.Should().Be("lint");
        stream[0].RuleId.Should().Be("lint/placeholder");
        stream[0].Severity.Should().Be(GateSeverity.Error);
        stream[0].Line.Should().Be(4);
        stream[0].Message.Should().Contain("TODO");
    }

    [Fact]
    public void Carries_front_matter_findings()
    {
        var stream = FindingStream.Gating(Report(frontMatter: new[]
        {
            new FrontMatterFinding(FrontMatterChecker.MissingKeyRule, 1, "missing required key 'status'"),
        }));

        stream.Should().HaveCount(1);
        stream[0].CheckId.Should().Be("front-matter");
        stream[0].RuleId.Should().Be("front-matter/missing-key");
        stream[0].Severity.Should().Be(GateSeverity.Error);
    }

    [Fact]
    public void Carries_generation_artifact_findings()
    {
        var stream = FindingStream.Gating(Report(artifacts: new[]
        {
            new AiArtifact(AiArtifactChecker.AssistantVoiceRule, 7, "Certainly!", "assistant framing"),
        }));

        stream.Should().HaveCount(1);
        stream[0].CheckId.Should().Be("ai-artifacts");
        stream[0].RuleId.Should().Be("ai-artifacts/assistant-voice");
    }

    [Fact]
    public void Advisories_carry_their_own_lower_severities()
    {
        var stream = FindingStream.Advisory(Report(
            prose: new[] { new ProseFinding("hedge", 12, "should probably", "hedging: 'should probably'") },
            fenceWarnings: new[] { new FenceIssue(20, "no-language", "code fence has no language tag") }));

        stream.Single(f => f.CheckId == "prose").Severity.Should().Be(GateSeverity.Info);
        stream.Single(f => f.CheckId == "fences").Severity.Should().Be(GateSeverity.Warning);
    }

    [Fact]
    public void Gating_excludes_advisories()
    {
        var report = Report(
            lint: new[] { new SpecLintFinding("placeholder", 4, "marker") },
            prose: new[] { new ProseFinding("hedge", 12, "maybe", "hedging") });

        FindingStream.Gating(report).Should().HaveCount(1);
        FindingStream.All(report).Should().HaveCount(2);
    }

    [Fact]
    public void All_orders_by_line_then_rule_id()
    {
        var report = Report(
            lint: new[]
            {
                new SpecLintFinding("placeholder", 9, "b"),
                new SpecLintFinding("empty-section", 9, "a"),
            },
            prose: new[] { new ProseFinding("hedge", 2, "maybe", "hedging") });

        var stream = FindingStream.All(report);

        stream.Select(f => f.Line).Should().Equal(2, 9, 9);
        // Same line: ordered by rule id, so output is stable run to run.
        stream[1].RuleId.Should().Be("lint/empty-section");
        stream[2].RuleId.Should().Be("lint/placeholder");
    }

    [Fact]
    public void Every_flattened_rule_carries_a_description_and_a_remedy()
    {
        // The fix brief is only actionable if each rule the stream can emit is catalogued.
        var report = Report(
            lint: new[] { new SpecLintFinding("placeholder", 1, "m") },
            frontMatter: new[] { new FrontMatterFinding(FrontMatterChecker.UnclosedRule, 1, "m") },
            artifacts: new[] { new AiArtifact(AiArtifactChecker.TruncatedOutputRule, 1, "[...]", "m") },
            prose: new[] { new ProseFinding("weasel", 1, "etc.", "m") },
            fenceWarnings: new[] { new FenceIssue(1, "no-language", "m") });

        foreach (var finding in FindingStream.All(report))
        {
            finding.Description.Should().NotBe(finding.RuleId, $"{finding.RuleId} should be catalogued");
            finding.Remedy.Should().NotBeEmpty($"{finding.RuleId} should have a remedy");
        }
    }

    [Fact]
    public void Severity_names_are_the_lowercase_tokens_used_downstream()
    {
        new GateFinding("a", "b", GateSeverity.Error, 1, "m").SeverityName.Should().Be("error");
        new GateFinding("a", "b", GateSeverity.Warning, 1, "m").SeverityName.Should().Be("warning");
        new GateFinding("a", "b", GateSeverity.Info, 1, "m").SeverityName.Should().Be("info");
    }

    [Fact]
    public void A_missing_section_is_anchored_at_line_one()
    {
        var report = ReviewReport.Compute("# Title\n\nText.\n", _ => true, new[] { "Acceptance Criteria" });

        FindingStream.Gating(report).Single(f => f.CheckId == "sections").Line.Should().Be(1);
    }
}
