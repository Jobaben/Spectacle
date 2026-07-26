using System.Linq;
using FluentAssertions;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class AiArtifactCheckerTests
{
    [Fact]
    public void A_clean_document_has_no_findings()
    {
        const string content =
            "# Auth design\n\n## Overview\n\nThe service issues a signed token on login and " +
            "rejects an expired one with 401.\n";

        AiArtifactChecker.Check(content).Should().BeEmpty();
        AiArtifactChecker.Check(null).Should().BeEmpty();
        AiArtifactChecker.Check("").Should().BeEmpty();
    }

    // ---------- unfilled-template ----------

    [Theory]
    [InlineData("The service is called {{service_name}} here.")]
    [InlineData("Deploy version ${VERSION} to production.")]
    [InlineData("Contact <TEAM_OWNER> before merging.")]
    [InlineData("Set the scope to %SCOPE_NAME% first.")]
    [InlineData("Summary: [INSERT SUMMARY HERE]")]
    [InlineData("Owner: [YOUR NAME]")]
    [InlineData("Reviewed by: ______ on the release call.")]
    public void Reports_an_unsubstituted_template_token(string line)
    {
        var findings = AiArtifactChecker.Check("# T\n\n" + line + "\n");

        findings.Select(f => f.Rule).Should().Contain(AiArtifactChecker.UnfilledTemplateRule);
    }

    [Fact]
    public void Does_not_report_a_template_token_inside_a_code_span()
    {
        // A docs page about templating shows the syntax as a literal, which is correct writing.
        AiArtifactChecker.Check("# T\n\nUse `{{name}}` to interpolate.\n").Should().BeEmpty();
    }

    [Fact]
    public void Does_not_report_a_template_token_inside_a_fenced_block()
    {
        AiArtifactChecker.Check("# T\n\n```hbs\n<h1>{{title}}</h1>\n```\n").Should().BeEmpty();
    }

    [Fact]
    public void Does_not_report_a_thematic_break_written_with_underscores()
    {
        AiArtifactChecker.Check("# T\n\nText.\n\n______\n\nMore.\n").Should().BeEmpty();
    }

    [Fact]
    public void Does_not_report_a_snake_case_identifier()
    {
        AiArtifactChecker.Check("# T\n\nThe field is named user_account_id in the payload.\n")
            .Should().BeEmpty();
    }

    // ---------- assistant-voice ----------

    [Theory]
    [InlineData("Certainly! Below is the design you asked for.")]
    [InlineData("Sure, I can help with that.")]
    [InlineData("As an AI language model, I cannot verify the deployment.")]
    [InlineData("I hope this helps with the rollout.")]
    [InlineData("Let me know if you need another section.")]
    [InlineData("Here's the complete specification for the service.")]
    [InlineData("I've updated the acceptance criteria as discussed.")]
    [InlineData("As requested, the non-goals are listed below.")]
    [InlineData("Feel free to ask about the migration steps.")]
    [InlineData("If you would like me to expand the rollout section, say so.")]
    public void Reports_chat_framing(string line)
    {
        var findings = AiArtifactChecker.Check("# T\n\n" + line + "\n");

        findings.Select(f => f.Rule).Should().Contain(AiArtifactChecker.AssistantVoiceRule);
    }

    [Fact]
    public void Does_not_report_an_acknowledgement_word_used_mid_sentence()
    {
        // "of course" as ordinary English is not a chat opener; only a line that starts with the
        // acknowledgement is.
        AiArtifactChecker.Check("# T\n\nThe token is of course validated on every request.\n")
            .Should().BeEmpty();
    }

    [Fact]
    public void Does_not_report_an_ordinary_if_you_would_like_sentence()
    {
        AiArtifactChecker.Check("# T\n\nIf you would like a copy of the audit log, open a ticket.\n")
            .Should().BeEmpty();
    }

    // ---------- truncated-output ----------

    [Theory]
    [InlineData("The remaining endpoints follow the same shape [...]")]
    [InlineData("Response fields (truncated) are documented upstream.")]
    [InlineData("The rest of the file is unchanged.")]
    [InlineData("The remainder of the document remains the same.")]
    [InlineData("Full list omitted for brevity.")]
    [InlineData("Section continues in the appendix.")]
    public void Reports_a_truncation_marker(string line)
    {
        var findings = AiArtifactChecker.Check("# T\n\n" + line + "\n");

        findings.Select(f => f.Rule).Should().Contain(AiArtifactChecker.TruncatedOutputRule);
    }

    [Fact]
    public void Reports_a_line_that_is_only_an_ellipsis()
    {
        AiArtifactChecker.Check("# T\n\nText.\n\n...\n\nMore.\n")
            .Select(f => f.Rule).Should().Contain(AiArtifactChecker.TruncatedOutputRule);
    }

    // ---------- placeholder-target ----------

    [Theory]
    [InlineData("See [the guide](path/to/guide.md) for details.")]
    [InlineData("Clone [the repo](https://github.com/your-org/your-repo) first.")]
    [InlineData("Read [the notes](#) before starting.")]
    [InlineData("Read [the notes]({{docs_url}}) before starting.")]
    public void Reports_a_placeholder_link_target(string line)
    {
        var findings = AiArtifactChecker.Check("# T\n\n" + line + "\n");

        findings.Select(f => f.Rule).Should().Contain(AiArtifactChecker.PlaceholderTargetRule);
    }

    [Fact]
    public void Reports_a_placeholder_image_target()
    {
        var findings = AiArtifactChecker.Check("# T\n\n![Architecture](path/to/diagram.png)\n");

        findings.Should().Contain(f => f.Rule == AiArtifactChecker.PlaceholderTargetRule);
        findings.Single(f => f.Rule == AiArtifactChecker.PlaceholderTargetRule)
            .Message.Should().Contain("image");
    }

    [Fact]
    public void Does_not_report_the_reserved_example_domain()
    {
        // example.com exists so that documentation can show a URL that points nowhere on purpose;
        // flagging it would punish correct writing.
        AiArtifactChecker.Check("# T\n\nSee [the reference](https://example.com/api) for the shape.\n")
            .Should().BeEmpty();
    }

    [Fact]
    public void Does_not_report_an_ordinary_relative_link()
    {
        AiArtifactChecker.Check("# T\n\nSee [the design](docs/design.md) for details.\n")
            .Should().BeEmpty();
    }

    // ---------- shape of the results ----------

    [Fact]
    public void Reports_at_most_one_finding_per_rule_per_line()
    {
        var findings = AiArtifactChecker.Check("# T\n\nUse {{a}} and {{b}} and {{c}} together.\n");

        findings.Count(f => f.Rule == AiArtifactChecker.UnfilledTemplateRule).Should().Be(1);
    }

    [Fact]
    public void Reports_different_rules_on_the_same_line_separately()
    {
        var findings = AiArtifactChecker.Check("# T\n\nCertainly! The value is {{value}}.\n");

        findings.Select(f => f.Rule).Should().Contain(new[]
        {
            AiArtifactChecker.AssistantVoiceRule,
            AiArtifactChecker.UnfilledTemplateRule,
        });
    }

    [Fact]
    public void Carries_the_line_and_an_excerpt_of_the_match()
    {
        var findings = AiArtifactChecker.Check("# T\n\nThe id is {{run_id}} here.\n");

        findings.Should().HaveCount(1);
        findings[0].Line.Should().Be(3);
        findings[0].Excerpt.Should().Be("{{run_id}}");
        findings[0].Message.Should().Contain("{{run_id}}");
    }

    [Fact]
    public void Caps_a_long_excerpt()
    {
        var token = "{{" + new string('x', 200) + "}}";
        var findings = AiArtifactChecker.Check("# T\n\nValue: " + token + "\n");

        findings.Should().HaveCount(1);
        findings[0].Excerpt.Length.Should().BeLessThanOrEqualTo(61);
        findings[0].Excerpt.Should().EndWith("…");
    }

    [Fact]
    public void Findings_are_ordered_by_line()
    {
        const string content = "# T\n\nCertainly! Here is the draft.\n\nText.\n\nValue: {{x}}\n\n[a](path/to/b)\n";
        var findings = AiArtifactChecker.Check(content);

        findings.Select(f => f.Line).Should().BeInAscendingOrder();
        findings.Count.Should().BeGreaterThan(2);
    }
}
