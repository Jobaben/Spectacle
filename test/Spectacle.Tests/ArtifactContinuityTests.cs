using System;
using System.IO;
using FluentAssertions;
using Spectacle.Ai;
using Spectacle.Gate;
using Xunit;

namespace Spectacle.Tests;

/// <summary>
/// The primary use case, asserted on the artifact rather than on a model: session A wrote a
/// capsule, session A ended, and session B — a brand-new process with no conversational memory —
/// revised the document. What must hold is a property of the file, so it holds whichever model
/// wrote it.
/// </summary>
public class ArtifactContinuityTests
{
    // Line endings are normalized on read: .gitattributes decides what lands on disk, and no
    // assertion here is about newlines.
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "artifact-context", name))
            .Replace("\r\n", "\n");

    private static readonly string SessionA = Fixture("session-a.md");
    private static readonly string SessionB = Fixture("session-b.md");

    [Fact]
    public void Both_sessions_leave_a_well_formed_capsule()
    {
        ArtifactContext.Read(SessionA).State.Should().Be(ArtifactContextState.Present);
        ArtifactContext.Read(SessionB).State.Should().Be(ArtifactContextState.Present);
    }

    [Fact]
    public void Session_B_preserved_the_decision_session_A_made_and_its_reason()
    {
        // The new request said nothing about the consumption architecture. Losing it — or losing
        // why it was chosen — is the flattening this whole design exists to prevent.
        SessionB.Should().Contain("Consume changes through a projection reader.");
        SessionB.Should().Contain("replay a window after an outage without upstream");
    }

    [Fact]
    public void Session_B_preserved_why_the_alternatives_were_rejected()
    {
        SessionB.Should().Contain("Queue reader with at-least-once delivery.");
        SessionB.Should().Contain("retention window of one hour");
        SessionB.Should().Contain("Direct polling of the changes endpoint.");
        SessionB.Should().Contain("breaches the rate limit above 40 tenants");
    }

    [Fact]
    public void Session_B_preserved_the_constraints()
    {
        ArtifactContext.Read(SessionB).Sections.Should().Contain("constraints");
        SessionB.Should().Contain("rate limited to 60 requests per minute per tenant");
        SessionB.Should().Contain("replay at least 24 hours");
    }

    [Fact]
    public void The_superseded_value_is_history_not_a_second_current_decision()
    {
        // The capsule is current state plus causal history, not an append-only log: two
        // contradictory current decisions is the failure mode.
        var capsule = Capsule(SessionB);
        var decisions = Section(capsule, "decisions");
        var history = Section(capsule, "history");

        decisions.Should().Contain("30 seconds");
        decisions.Should().NotContain("10-second");
        decisions.Should().NotContain("10 seconds");
        history.Should().Contain("10 seconds");
        history.Should().Contain("30");
    }

    [Fact]
    public void The_change_carries_its_reason_rather_than_the_request_wording()
    {
        Section(Capsule(SessionB), "decisions").Should().Contain("Production telemetry");
        SessionB.Should().NotContain("way too aggressive");
        SessionB.Should().NotContain("Change it to 30 sec");
    }

    [Fact]
    public void The_question_session_A_left_open_is_gone_because_session_B_answered_it()
    {
        ArtifactContext.Read(SessionA).Sections.Should().Contain("unresolved");
        ArtifactContext.Read(SessionB).Sections.Should().NotContain("unresolved");
        SessionB.Should().NotContain("Determine the retry interval from production telemetry");
    }

    [Fact]
    public void Unrelated_front_matter_survived_the_revision()
    {
        var header = FrontMatter.Parse(SessionB);

        header.Find("title")!.Value.Should().Be("Poller architecture");
        header.Find("status")!.Value.Should().Be("draft");
        header.Find("owner")!.Value.Should().Be("platform");
    }

    [Fact]
    public void The_body_states_the_current_value_the_capsule_records()
    {
        FrontMatter.Strip(SessionB).Should().Contain("retries a\nfailed fetch after 30 seconds");
    }

    [Fact]
    public void The_capsule_did_not_grow_unboundedly_across_the_two_sessions()
    {
        // Merge-and-recompress, not append. A capsule that doubles per session stops being a
        // handoff and becomes a transcript.
        Capsule(SessionB).Length.Should().BeLessThan((int)(Capsule(SessionA).Length * 1.5));
    }

    /// <summary>The raw <c>artifact_context</c> region of a document's front matter.</summary>
    private static string Capsule(string document)
    {
        var lines = document.Split('\n');
        var start = Array.FindIndex(lines, l => l.StartsWith("artifact_context:", StringComparison.Ordinal));
        start.Should().BeGreaterThan(-1);

        var end = start + 1;
        while (end < lines.Length && (lines[end].Length == 0 || lines[end][0] == ' ' || lines[end][0] == '\t')) end++;
        return string.Join("\n", lines[start..end]);
    }

    /// <summary>One section of a capsule, from its key line to the next key at the same indent.</summary>
    private static string Section(string capsule, string name)
    {
        var lines = capsule.Split('\n');
        var start = Array.FindIndex(lines, l => l.TrimStart().StartsWith(name + ":", StringComparison.Ordinal));
        start.Should().BeGreaterThan(-1, $"the capsule should carry a '{name}' section");

        var indent = lines[start].Length - lines[start].TrimStart().Length;
        var end = start + 1;
        while (end < lines.Length)
        {
            var line = lines[end];
            if (line.Trim().Length != 0 && line.Length - line.TrimStart().Length <= indent) break;
            end++;
        }
        return string.Join("\n", lines[start..end]);
    }
}
