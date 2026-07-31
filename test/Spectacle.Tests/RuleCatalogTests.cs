using System;
using System.Linq;
using FluentAssertions;
using Spectacle.Checks;
using Spectacle.Gate;
using Xunit;

namespace Spectacle.Tests;

public class RuleCatalogTests
{
    [Fact]
    public void Every_gate_selectable_check_has_at_least_one_rule()
    {
        // A check with no catalogued rule would report findings nobody can describe or fix.
        foreach (var checkId in ReviewChecks.All)
            RuleCatalog.All.Should().Contain(r => r.CheckId == checkId, $"check '{checkId}' needs a rule");
    }

    [Fact]
    public void Every_rule_id_is_prefixed_with_its_check_id()
    {
        // The id is the contract shared by SARIF, the verdict, CI annotations and the delta; a rule
        // whose id disagreed with its check would break grading by check id.
        foreach (var rule in RuleCatalog.All)
        {
            var belongs = rule.Id == rule.CheckId
                || rule.Id.StartsWith(rule.CheckId + "/", StringComparison.Ordinal);
            belongs.Should().BeTrue($"'{rule.Id}' should belong to check '{rule.CheckId}'");
        }
    }

    [Fact]
    public void Rule_ids_are_unique()
    {
        RuleCatalog.All.Select(r => r.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Every_rule_has_a_description_and_a_remedy()
    {
        foreach (var rule in RuleCatalog.All)
        {
            rule.Description.Should().NotBeNullOrWhiteSpace(rule.Id);
            rule.Remedy.Should().NotBeNullOrWhiteSpace(rule.Id);
        }
    }

    [Fact]
    public void Advisory_rules_default_below_error_and_everything_else_defaults_to_error()
    {
        RuleCatalog.DefaultSeverityOf("prose/hedge").Should().Be(GateSeverity.Info);
        RuleCatalog.DefaultSeverityOf("prose/weasel").Should().Be(GateSeverity.Info);
        RuleCatalog.DefaultSeverityOf("prose/vague-directive").Should().Be(GateSeverity.Info);
        RuleCatalog.DefaultSeverityOf("fences/no-language").Should().Be(GateSeverity.Warning);

        var gating = RuleCatalog.All
            .Where(r => r.CheckId != "prose" && r.Id != "fences/no-language");
        gating.Should().AllSatisfy(r => r.DefaultSeverity.Should().Be(GateSeverity.Error));
    }

    [Fact]
    public void An_uncatalogued_id_still_gates()
    {
        // A finding Spectacle emits but forgot to catalogue must fail loudly rather than silently
        // passing: the failure mode is a missing description, not an ignored defect.
        RuleCatalog.DefaultSeverityOf("invented/rule").Should().Be(GateSeverity.Error);
        RuleCatalog.DescriptionOf("invented/rule").Should().Be("invented/rule");
        RuleCatalog.RemedyOf("invented/rule").Should().BeEmpty();
        RuleCatalog.Find("invented/rule").Should().BeNull();
    }

    [Fact]
    public void Catalogues_every_front_matter_rule_the_checker_can_emit()
    {
        foreach (var rule in new[]
        {
            FrontMatterChecker.MissingHeaderRule, FrontMatterChecker.UnclosedRule,
            FrontMatterChecker.MissingKeyRule, FrontMatterChecker.EmptyValueRule,
            FrontMatterChecker.DuplicateKeyRule, FrontMatterChecker.MisplacedRule,
        })
        {
            RuleCatalog.Find($"front-matter/{rule}").Should().NotBeNull(rule);
        }
    }

    [Fact]
    public void Catalogues_every_generation_artifact_rule_the_checker_can_emit()
    {
        foreach (var rule in new[]
        {
            AiArtifactChecker.UnfilledTemplateRule, AiArtifactChecker.AssistantVoiceRule,
            AiArtifactChecker.TruncatedOutputRule, AiArtifactChecker.PlaceholderTargetRule,
        })
        {
            RuleCatalog.Find($"ai-artifacts/{rule}").Should().NotBeNull(rule);
        }
    }

    [Fact]
    public void Catalogues_every_mermaid_rule_the_checker_can_emit()
    {
        foreach (var rule in new[]
        {
            MermaidChecker.EmptyRule, MermaidChecker.UnknownTypeRule,
            MermaidChecker.MissingDescriptionRule,
        })
        {
            RuleCatalog.Find($"mermaid/{rule}").Should().NotBeNull(rule);
        }
    }

    [Fact]
    public void Finds_a_rule_case_insensitively()
    {
        RuleCatalog.Find("LINT/PLACEHOLDER").Should().NotBeNull();
    }
}
