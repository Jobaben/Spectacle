using System;
using FluentAssertions;
using Spectacle.Ai;
using Xunit;

namespace Spectacle.Tests;

public class ArtifactContextPromptTests
{
    private const string DocPath = @"C:\repo\docs\architecture.md";
    private const string Brief = "1. Change the retry interval to 30 seconds.";

    private static ArtifactContextView Present(params string[] sections) =>
        new(ArtifactContextState.Present, sections, Array.Empty<string>());

    [Fact]
    public void The_brief_is_still_the_last_thing_in_the_prompt()
    {
        // The handoff section goes before the brief: the agent reads the contract, then the ask.
        var nl = Environment.NewLine;
        ClaudeRevisionPrompt.Build(DocPath, Brief, Present("decisions"))
            .Should().EndWith("The revision brief:" + nl + nl + Brief);
    }

    [Fact]
    public void The_in_place_contract_survives_the_addition()
    {
        var prompt = ClaudeRevisionPrompt.Build(DocPath, Brief, Present("decisions"));

        prompt.Should().Contain("revise it IN PLACE: " + DocPath);
        prompt.Should().Contain("Create no other file");
        prompt.Should().Contain("records each save as an iteration");
    }

    [Fact]
    public void The_default_overload_still_builds_a_prompt_without_a_capsule()
    {
        ClaudeRevisionPrompt.Build(DocPath, Brief)
            .Should().Be(ClaudeRevisionPrompt.Build(DocPath, Brief, ArtifactContextView.None));
    }

    [Fact]
    public void An_existing_capsule_is_declared_inherited_and_authoritative()
    {
        var prompt = ClaudeRevisionPrompt.Build(DocPath, Brief, Present("purpose", "decisions", "unresolved"));

        prompt.Should().Contain("independent session");
        prompt.Should().Contain("artifact_context");
        prompt.Should().Contain("inherited");
        prompt.Should().Contain("Read the complete file before");
        prompt.Should().Contain("purpose, decisions, unresolved");
    }

    [Fact]
    public void The_merge_rules_forbid_replacing_the_capsule_and_forbid_an_append_only_log()
    {
        var prompt = ClaudeRevisionPrompt.Build(DocPath, Brief, Present("decisions"));

        prompt.Should().Contain("merge");
        prompt.Should().Contain("recompress");
        prompt.Should().Contain("not an append-only event log");
        prompt.Should().Contain("two contradictory current decisions");
    }

    [Fact]
    public void A_resolved_question_must_leave_the_unresolved_section()
    {
        ClaudeRevisionPrompt.Build(DocPath, Brief, Present("unresolved"))
            .Should().Contain("no longer belongs under `unresolved`");
    }

    [Fact]
    public void The_request_is_stored_as_intent_rather_than_as_its_wording()
    {
        ClaudeRevisionPrompt.Build(DocPath, Brief, Present("history"))
            .Should().Contain("not the conversational wording");
    }

    [Fact]
    public void Unrelated_front_matter_is_declared_off_limits()
    {
        ClaudeRevisionPrompt.Build(DocPath, Brief, Present("decisions"))
            .Should().Contain("Do not overwrite or delete unrelated front matter");
    }

    [Fact]
    public void An_absent_capsule_is_seeded_rather_than_assumed()
    {
        var prompt = ClaudeRevisionPrompt.Build(DocPath, Brief, ArtifactContextView.None);

        prompt.Should().Contain("does not carry an `artifact_context`");
        prompt.Should().Contain("Create it");
    }

    [Fact]
    public void A_malformed_capsule_is_repaired_conservatively_and_never_discarded()
    {
        var broken = new ArtifactContextView(
            ArtifactContextState.Malformed,
            Array.Empty<string>(),
            new[] { "'artifact_context' is declared 2 times" });

        var prompt = ClaudeRevisionPrompt.Build(DocPath, Brief, broken);

        prompt.Should().Contain("is declared 2 times");
        prompt.Should().Contain("Do not discard it");
        prompt.Should().Contain("Preserve every readable");
    }

    [Fact]
    public void The_prompt_never_finishes_on_an_invalid_artifact()
    {
        ClaudeRevisionPrompt.Build(DocPath, Brief, Present("decisions"))
            .Should().Contain("Do not finish while the artifact is structurally invalid");
    }
}
