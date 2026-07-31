using System;
using System.Linq;
using FluentAssertions;
using Spectacle.Checks;
using Xunit;

namespace Spectacle.Tests;

public class FrontMatterCheckerTests
{
    private static readonly string[] None = Array.Empty<string>();

    [Fact]
    public void A_document_with_no_header_and_no_template_is_clean()
    {
        // A project that does not use front matter must be completely unaffected.
        FrontMatterChecker.Check("# Title\n\nText.\n", None).Should().BeEmpty();
    }

    [Fact]
    public void Reports_a_missing_header_when_a_template_is_declared()
    {
        var findings = FrontMatterChecker.Check("# Title\n", new[] { "status" });

        findings.Should().HaveCount(1);
        findings[0].Rule.Should().Be(FrontMatterChecker.MissingHeaderRule);
        findings[0].Line.Should().Be(1);
        findings[0].Message.Should().Contain("status");
    }

    [Fact]
    public void Names_every_required_key_when_the_header_is_missing()
    {
        var findings = FrontMatterChecker.Check("# Title\n", new[] { "status", "agent" });

        findings[0].Message.Should().Contain("status").And.Contain("agent");
    }

    [Fact]
    public void Reports_an_unclosed_header()
    {
        var findings = FrontMatterChecker.Check("---\ntitle: Auth\n\n# Body\n", None);

        findings.Should().HaveCount(1);
        findings[0].Rule.Should().Be(FrontMatterChecker.UnclosedRule);
        findings[0].Line.Should().Be(1);
    }

    [Fact]
    public void An_unclosed_header_does_not_also_report_missing_keys()
    {
        // The header is unusable; one clear defect beats a pile of consequences.
        var findings = FrontMatterChecker.Check("---\ntitle: Auth\n\n# Body\n", new[] { "status" });

        findings.Select(f => f.Rule).Should().Equal(FrontMatterChecker.UnclosedRule);
    }

    [Fact]
    public void Reports_a_required_key_that_is_absent()
    {
        var findings = FrontMatterChecker.Check("---\ntitle: Auth\n---\n", new[] { "status" });

        findings.Should().HaveCount(1);
        findings[0].Rule.Should().Be(FrontMatterChecker.MissingKeyRule);
        findings[0].Message.Should().Contain("status");
    }

    [Fact]
    public void Reports_a_required_key_that_is_present_but_blank()
    {
        var findings = FrontMatterChecker.Check("---\ntitle: Auth\nstatus:\n---\n", new[] { "status" });

        findings.Should().HaveCount(1);
        findings[0].Rule.Should().Be(FrontMatterChecker.EmptyValueRule);
        // Anchored at the offending key, not at the fence.
        findings[0].Line.Should().Be(3);
    }

    [Fact]
    public void A_filled_template_is_clean()
    {
        var findings = FrontMatterChecker.Check(
            "---\ntitle: Auth\nstatus: draft\nrun:\n  model: opus\n---\n\n# Auth\n",
            new[] { "title", "status", "run.model" });

        findings.Should().BeEmpty();
    }

    [Fact]
    public void A_required_nested_key_is_matched_by_its_dotted_path()
    {
        var findings = FrontMatterChecker.Check("---\nrun:\n  model: opus\n---\n", new[] { "run.id" });

        findings.Select(f => f.Rule).Should().Equal(FrontMatterChecker.MissingKeyRule);
    }

    [Fact]
    public void A_required_key_may_be_satisfied_by_a_sequence()
    {
        FrontMatterChecker.Check("---\ntags: [a, b]\n---\n", new[] { "tags" }).Should().BeEmpty();
    }

    [Fact]
    public void Reports_a_duplicate_key()
    {
        var findings = FrontMatterChecker.Check("---\nstatus: draft\nstatus: final\n---\n", None);

        findings.Should().HaveCount(1);
        findings[0].Rule.Should().Be(FrontMatterChecker.DuplicateKeyRule);
        findings[0].Line.Should().Be(3);
    }

    [Fact]
    public void Reports_a_front_matter_block_below_the_top_of_the_document()
    {
        // The signature of concatenated generator output: it renders as prose, not metadata.
        const string content = "# First\n\nText.\n\n---\ntitle: Second\nstatus: draft\n---\n\n# Second\n";
        var findings = FrontMatterChecker.Check(content, None);

        findings.Should().HaveCount(1);
        findings[0].Rule.Should().Be(FrontMatterChecker.MisplacedRule);
        findings[0].Line.Should().Be(5);
    }

    [Fact]
    public void Does_not_report_a_horizontal_rule_around_a_single_key_like_line()
    {
        // One "Note: something" line between two rules is something a person writes; only a
        // multi-key block is the shape of a generator header.
        const string content = "# Title\n\n---\nNote: this matters\n---\n\nText.\n";
        FrontMatterChecker.Check(content, None).Should().BeEmpty();
    }

    [Fact]
    public void Does_not_report_a_yaml_sample_inside_a_code_fence()
    {
        const string content = "# Title\n\n```yaml\n---\ntitle: Example\nstatus: draft\n---\n```\n";
        FrontMatterChecker.Check(content, None).Should().BeEmpty();
    }

    [Fact]
    public void Does_not_report_the_documents_own_header_as_misplaced()
    {
        FrontMatterChecker.Check("---\ntitle: Auth\nstatus: draft\n---\n\n# Auth\n", None)
            .Should().BeEmpty();
    }

    [Fact]
    public void The_misplaced_message_carries_no_line_number()
    {
        // The baseline delta keys a finding's identity on its message, so a line number inside it
        // would make an unmoved defect read as one fixed plus one new after any edit above.
        const string content = "# First\n\n---\na: 1\nb: 2\n---\n";
        var findings = FrontMatterChecker.Check(content, None);

        findings.Should().HaveCount(1);
        findings[0].Message.Should().NotContain("3");
    }

    [Fact]
    public void Findings_are_ordered_by_line()
    {
        const string content = "---\nstatus: draft\nstatus: final\n---\n\n# T\n\n---\na: 1\nb: 2\n---\n";
        var findings = FrontMatterChecker.Check(content, Array.Empty<string>());

        findings.Select(f => f.Line).Should().BeInAscendingOrder();
    }

    [Fact]
    public void ParseRequired_splits_and_trims_a_comma_separated_list()
    {
        FrontMatterChecker.ParseRequired(" title , status ,, run.model ")
            .Should().Equal("title", "status", "run.model");
        FrontMatterChecker.ParseRequired(null).Should().BeEmpty();
        FrontMatterChecker.ParseRequired("").Should().BeEmpty();
    }

    [Fact]
    public void Blank_and_whitespace_only_required_keys_are_ignored()
    {
        FrontMatterChecker.Check("---\ntitle: Auth\n---\n", new[] { "  ", "" }).Should().BeEmpty();
    }
}
