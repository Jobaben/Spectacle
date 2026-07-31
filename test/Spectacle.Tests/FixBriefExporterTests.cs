using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Spectacle.Checks;
using Spectacle.Export;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class FixBriefExporterTests
{
    // Two blocking findings at different lines plus one advisory, so ordering and the
    // required/optional split are both observable.
    private const string Dirty =
        "# Title\n\nTODO: decide the token lifetime.\n\nWe should probably cache it.\n\n" +
        "Certainly! Here is the revised rollout plan.\n";

    private static GateVerdict Verdict(string content = Dirty, GatePolicy? policy = null) =>
        GateVerdict.Compute(
            "spec.md", ReviewReport.Compute(content), policy ?? GatePolicy.Default, FrontMatter.Parse(content));

    [Fact]
    public void Addresses_the_authoring_tool_and_names_the_document()
    {
        var brief = FixBriefExporter.Build(Verdict(), json: false);

        brief.Should().StartWith("# Revision brief — spec.md");
        brief.Should().Contain("does not pass");
    }

    [Fact]
    public void States_the_command_that_re_checks_the_document()
    {
        // Without this the loop has no closing step: the tool fixes and nobody re-gates.
        FixBriefExporter.Build(Verdict(), false).Should().Contain("--gate");
    }

    [Fact]
    public void Separates_required_fixes_from_optional_improvements()
    {
        var brief = FixBriefExporter.Build(Verdict(), false);

        brief.Should().Contain("## Required fixes (2)");
        brief.Should().Contain("## Optional improvements (1)");
    }

    [Fact]
    public void Orders_the_required_fixes_from_the_end_of_the_document_backwards()
    {
        // An edit at line 3 shifts every line after it, so a top-down list would hand the tool
        // stale line numbers halfway through the pass.
        var brief = FixBriefExporter.Build(Verdict(), false);
        var from = brief.IndexOf("## Required fixes", System.StringComparison.Ordinal);
        var to = brief.IndexOf("## Optional improvements", System.StringComparison.Ordinal);
        var required = brief[from..to];
        var lines = required.Split('\n')
            .Where(l => l.StartsWith("### ", System.StringComparison.Ordinal))
            .Select(l => int.Parse(l.Split("Line ")[1].Split(' ')[0]))
            .ToList();

        lines.Should().BeInDescendingOrder();
        lines.Should().HaveCount(2);
    }

    [Fact]
    public void Each_finding_carries_what_was_found_why_it_matters_and_what_to_do()
    {
        var brief = FixBriefExporter.Build(Verdict(), false);

        brief.Should().Contain("- What was found:");
        brief.Should().Contain("- Why it matters:");
        brief.Should().Contain("- **Do this:**");
        // The remedy is the catalogued instruction, not a restatement of the rule id.
        brief.Should().Contain(RuleCatalog.RemedyOf("lint/placeholder"));
    }

    [Fact]
    public void Constrains_the_revision_so_a_fix_pass_stays_a_fix_pass()
    {
        var brief = FixBriefExporter.Build(Verdict(), false);

        brief.Should().Contain("Change only what the findings below ask for");
        brief.Should().Contain("Do not add a changelog");
        // The escape hatch, so a tool that genuinely cannot comply has somewhere to go other than
        // mangling the document.
        brief.Should().Contain("spectacle-disable-next-line");
    }

    [Fact]
    public void Lists_the_documents_metadata_so_the_tool_knows_what_it_is_revising()
    {
        const string content = "---\nworkflow: spec-writer\n---\n\n# T\n\nTODO: decide.\n";
        FixBriefExporter.Build(Verdict(content), false)
            .Should().Contain("Document declares:").And.Contain("`workflow` = spec-writer");
    }

    [Fact]
    public void Declares_reduced_coverage()
    {
        var checks = ReviewChecks.Resolve(new[] { "lint" }, System.Array.Empty<string>(), System.Array.Empty<string>());
        var report = ReviewReport.Compute(Dirty, _ => true, System.Array.Empty<string>(), checks);
        var brief = FixBriefExporter.Build(
            GateVerdict.Compute("spec.md", report, GatePolicy.Default), false);

        brief.Should().Contain("Coverage note:").And.Contain("checks disabled:");
    }

    [Fact]
    public void A_passing_document_is_told_to_change_nothing()
    {
        var brief = FixBriefExporter.Build(Verdict("# Title\n\nA signed token is issued.\n"), false);

        brief.Should().Contain("**passes**");
        brief.Should().Contain("Nothing below is required.");
        brief.Should().Contain("No findings. Leave the document unchanged.");
    }

    [Fact]
    public void A_passing_document_with_advisories_lists_them_as_optional_only()
    {
        var brief = FixBriefExporter.Build(Verdict("# Title\n\nWe should probably cache it.\n"), false);

        brief.Should().Contain("**passes**");
        brief.Should().Contain("## Optional improvements (1)");
        brief.Should().NotContain("## Required fixes");
    }

    // ---------- json ----------

    [Fact]
    public void Json_carries_ordered_instructions_with_the_action_for_each()
    {
        var root = JsonDocument.Parse(FixBriefExporter.Build(Verdict(), json: true)).RootElement;

        root.GetProperty("gate").GetString().Should().Be("fail");
        root.GetProperty("source").GetString().Should().Be("spec.md");
        root.GetProperty("recheckCommand").GetString().Should().Contain("--gate");
        root.GetProperty("constraints").GetArrayLength().Should().BeGreaterThan(0);

        var instructions = root.GetProperty("instructions").EnumerateArray().ToList();
        instructions.Should().HaveCount(3);
        // Required first, and within each group from the end of the document backwards.
        instructions.Select(i => i.GetProperty("required").GetBoolean())
            .Should().Equal(true, true, false);
        instructions.Take(2).Select(i => i.GetProperty("line").GetInt32())
            .Should().BeInDescendingOrder();
        instructions.Select(i => i.GetProperty("order").GetInt32()).Should().Equal(1, 2, 3);

        foreach (var instruction in instructions)
        {
            instruction.GetProperty("rule").GetString().Should().NotBeNullOrWhiteSpace();
            instruction.GetProperty("found").GetString().Should().NotBeNullOrWhiteSpace();
            instruction.GetProperty("why").GetString().Should().NotBeNullOrWhiteSpace();
            instruction.GetProperty("action").GetString().Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void Json_for_a_passing_document_has_no_required_instructions()
    {
        var root = JsonDocument.Parse(
            FixBriefExporter.Build(Verdict("# Title\n\nA signed token is issued.\n"), true)).RootElement;

        root.GetProperty("passed").GetBoolean().Should().BeTrue();
        root.GetProperty("instructions").GetArrayLength().Should().Be(0);
    }
}
