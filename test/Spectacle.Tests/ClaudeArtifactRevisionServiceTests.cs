using System.Collections.Generic;
using FluentAssertions;
using Spectacle.Ai;
using Xunit;

namespace Spectacle.Tests;

public class ClaudeArtifactRevisionServiceTests
{
    private const string Root = @"C:\repo";
    private const string DocDir = @"C:\repo\docs";
    private const string DocPath = @"C:\repo\docs\architecture.md";
    private const string Brief = "1. Change the retry interval to 30 seconds.";

    private const string Managed = """
---
title: Architecture
artifact_context:
  decisions:
    - decision: Use the projection reader.
      reason: The queue reader could not replay.
---

# Architecture
""";

    private const string Unmanaged = "---\ntitle: Notes\n---\n\n# Notes\n";

    private sealed class Spy
    {
        public readonly List<(string WorkingDirectory, string Prompt)> Runs = new();
        public bool Accept = true;

        public bool Start(string workingDirectory, string prompt)
        {
            Runs.Add((workingDirectory, prompt));
            return Accept;
        }
    }

    private static ClaudeArtifactRevisionService Service(Spy spy, string? document, ClaudeProjectRootResult root) =>
        new(spy.Start, _ => document, _ => root);

    private static ClaudeProjectRootResult Found => new(Root, @"CLAUDE.md in 'C:\repo'");

    private static ClaudeProjectRootResult NotFound =>
        new(null, @"no CLAUDE.md or .claude directory above 'C:\repo\docs'");

    [Fact]
    public void A_resolved_project_root_is_the_working_directory_not_the_document_folder()
    {
        // Supplying an absolute filename does not load that file's project configuration; the
        // working directory does.
        var spy = new Spy();

        var outcome = Service(spy, Managed, Found).Revise(DocPath, DocDir, Brief);

        outcome.Status.Should().Be(ArtifactRevisionStatus.Started);
        outcome.ProjectRoot.Should().Be(Root);
        outcome.WorkingDirectory.Should().Be(Root);
        spy.Runs.Should().ContainSingle().Which.WorkingDirectory.Should().Be(Root);
    }

    [Fact]
    public void The_prompt_carries_the_documents_own_capsule_state()
    {
        var spy = new Spy();

        Service(spy, Managed, Found).Revise(DocPath, DocDir, Brief);

        var prompt = spy.Runs[0].Prompt;
        prompt.Should().Contain("inherited");
        prompt.Should().Contain("It currently carries: decisions.");
        prompt.Should().EndWith(Brief);
    }

    [Fact]
    public void A_managed_artifact_with_no_project_root_still_runs_under_user_scope()
    {
        // Claude Code loads ~/.claude whatever the working directory is, so a fallback run is
        // governed by less rather than ungoverned. Refusing would block every managed artifact
        // living outside a configured project — a worse failure than a run that says its scope.
        var spy = new Spy();

        var outcome = Service(spy, Managed, NotFound).Revise(DocPath, DocDir, Brief);

        outcome.Status.Should().Be(ArtifactRevisionStatus.Started);
        outcome.ProjectRoot.Should().BeNull();
        outcome.WorkingDirectory.Should().Be(DocDir);
        spy.Runs.Should().ContainSingle();
    }

    [Fact]
    public void A_fallback_run_hands_its_scope_note_over_before_the_process_starts()
    {
        // Before, not after: the runner raises Started on a worker thread, and a note arriving
        // later would be overwritten by the running chip instead of shown on it.
        var spy = new Spy();
        string? note = "unset";
        var order = new List<string>();

        var service = new ClaudeArtifactRevisionService(
            (w, p) => { order.Add("start"); return spy.Start(w, p); },
            _ => Managed,
            _ => NotFound);
        service.Revise(DocPath, DocDir, Brief, n => { note = n; order.Add("note"); });

        note.Should().Be("user scope only — no project root for this artifact");
        order.Should().Equal("note", "start");
    }

    [Fact]
    public void A_fully_scoped_run_has_no_scope_note_to_show()
    {
        var spy = new Spy();
        string? note = "unset";

        Service(spy, Managed, Found).Revise(DocPath, DocDir, Brief, n => note = n);

        note.Should().BeNull();
    }

    [Fact]
    public void A_malformed_capsule_still_counts_as_managed_in_the_scope_note()
    {
        // A broken capsule is evidence the document is managed, so the note names the artifact
        // rather than reading as a generic unscoped run.
        var spy = new Spy();
        string? note = null;

        var outcome = Service(spy, "---\nartifact_context: none\n---\n\n# Doc\n", NotFound)
            .Revise(DocPath, DocDir, Brief, n => note = n);

        outcome.Status.Should().Be(ArtifactRevisionStatus.Started);
        note.Should().Be("user scope only — no project root for this artifact");
    }

    [Fact]
    public void An_unmanaged_document_with_no_project_root_still_runs_from_its_own_folder()
    {
        // Pressing "a" on a loose .md outside any repository works today and keeps working.
        var spy = new Spy();
        string? note = null;

        var outcome = Service(spy, Unmanaged, NotFound).Revise(DocPath, DocDir, Brief, n => note = n);

        outcome.Status.Should().Be(ArtifactRevisionStatus.Started);
        outcome.ProjectRoot.Should().BeNull();
        outcome.WorkingDirectory.Should().Be(DocDir);
        spy.Runs.Should().ContainSingle().Which.WorkingDirectory.Should().Be(DocDir);
        note.Should().Be("user scope only — no project root");
    }

    [Fact]
    public void The_fallback_reason_is_surfaced_rather_than_swallowed()
    {
        var spy = new Spy();

        Service(spy, Unmanaged, NotFound).Revise(DocPath, DocDir, Brief)
            .Detail.Should().Contain("no CLAUDE.md or .claude");
    }

    [Fact]
    public void An_unmanaged_document_seeds_a_capsule_when_a_root_does_resolve()
    {
        var spy = new Spy();

        Service(spy, Unmanaged, Found).Revise(DocPath, DocDir, Brief);

        spy.Runs[0].Prompt.Should().Contain("does not carry an `artifact_context`");
    }

    [Fact]
    public void A_run_already_in_flight_is_reported_as_busy_not_as_a_failure()
    {
        // The chip must keep showing the running run rather than flipping to failed.
        var spy = new Spy { Accept = false };

        Service(spy, Managed, Found).Revise(DocPath, DocDir, Brief)
            .Status.Should().Be(ArtifactRevisionStatus.Busy);
    }

    [Fact]
    public void An_unreadable_document_is_treated_as_unmanaged_and_says_so()
    {
        var spy = new Spy();

        var outcome = new ClaudeArtifactRevisionService(spy.Start, _ => null, _ => Found)
            .Revise(DocPath, DocDir, Brief);

        outcome.Status.Should().Be(ArtifactRevisionStatus.Started);
        outcome.Detail.Should().Contain("could not be read");
    }
}
