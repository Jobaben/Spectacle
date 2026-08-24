using FluentAssertions;
using Spectacle.Ai;
using Xunit;

namespace Spectacle.Tests;

public class ArtifactContextTests
{
    private const string Managed = """
---
title: Retry design
status: draft
artifact_context:
  purpose: >
    Decide how the poller retries a failed fetch.
  decisions:
    - decision: Use a 30-second retry delay.
      reason: Telemetry showed 10 seconds was too aggressive.
  unresolved:
    - Whether the backoff should be exponential.
---

# Retry design

Body.
""";

    [Fact]
    public void A_document_with_no_front_matter_has_no_context()
    {
        ArtifactContext.Read("# Just a heading\n\nBody.").State.Should().Be(ArtifactContextState.Absent);
    }

    [Fact]
    public void A_header_without_the_namespace_has_no_context()
    {
        ArtifactContext.Read("---\ntitle: Draft\n---\n\n# Draft\n")
            .State.Should().Be(ArtifactContextState.Absent);
    }

    [Fact]
    public void A_well_formed_capsule_is_present_with_its_sections_named()
    {
        var view = ArtifactContext.Read(Managed);

        view.State.Should().Be(ArtifactContextState.Present);
        view.IsManaged.Should().BeTrue();
        view.Sections.Should().BeEquivalentTo(new[] { "purpose", "decisions", "unresolved" });
        view.Issues.Should().BeEmpty();
    }

    [Fact]
    public void A_block_scalar_continuation_is_not_mistaken_for_a_section()
    {
        // Only keys at the sections' own indent count. A block scalar's prose sits deeper, so a
        // sentence that happens to read like a section key must not invent one.
        const string trap = """
---
artifact_context:
  purpose: >
    We investigated three architectures: queue, projection, direct.
    evidence: this sentence is prose inside a block scalar, not a section.
  decisions:
    - decision: Chose the projection reader.
---

# Doc
""";

        var view = ArtifactContext.Read(trap);

        view.State.Should().Be(ArtifactContextState.Present);
        view.Sections.Should().BeEquivalentTo(new[] { "purpose", "decisions" });
        view.Sections.Should().NotContain("evidence");
    }

    [Fact]
    public void A_scalar_where_a_mapping_belongs_is_malformed()
    {
        var view = ArtifactContext.Read("---\nartifact_context: none yet\n---\n\n# Doc\n");

        view.State.Should().Be(ArtifactContextState.Malformed);
        view.IsManaged.Should().BeTrue();
        view.Issues.Should().ContainSingle().Which.Should().Contain("block mapping");
    }

    [Fact]
    public void An_empty_namespace_is_malformed()
    {
        var view = ArtifactContext.Read("---\nartifact_context:\ntitle: Draft\n---\n\n# Doc\n");

        view.State.Should().Be(ArtifactContextState.Malformed);
        view.Issues.Should().ContainSingle().Which.Should().Contain("no context sections");
    }

    [Fact]
    public void A_duplicated_namespace_is_malformed()
    {
        var text = "---\nartifact_context:\n  purpose: A\nartifact_context:\n  purpose: B\n---\n\n# Doc\n";

        var view = ArtifactContext.Read(text);

        view.State.Should().Be(ArtifactContextState.Malformed);
        view.Issues.Should().Contain(i => i.Contains("declared 2 times"));
    }

    [Fact]
    public void An_unclosed_header_carrying_the_namespace_is_malformed()
    {
        var view = ArtifactContext.Read("---\nartifact_context:\n  purpose: A\n\n# Doc\n");

        view.State.Should().Be(ArtifactContextState.Malformed);
        view.Issues.Should().Contain(i => i.Contains("never closed"));
    }

    [Fact]
    public void A_namespace_with_only_unrecognized_children_is_malformed_but_kept()
    {
        var view = ArtifactContext.Read("---\nartifact_context:\n  notes: something\n---\n\n# Doc\n");

        view.State.Should().Be(ArtifactContextState.Malformed);
        view.Sections.Should().BeEmpty();
        view.Issues.Should().Contain(i => i.Contains("no recognized context section"));
    }

    [Fact]
    public void A_crlf_document_reads_the_same_as_an_lf_one()
    {
        // Normalize first: a raw string literal keeps the source file's own line endings, which
        // .gitattributes may make either — so neither form can be assumed here.
        var lf = Managed.Replace("\r\n", "\n");

        ArtifactContext.Read(lf.Replace("\n", "\r\n")).Sections
            .Should().BeEquivalentTo(ArtifactContext.Read(lf).Sections);
    }

    [Fact]
    public void Null_and_empty_input_are_absent_rather_than_throwing()
    {
        ArtifactContext.Read(null).State.Should().Be(ArtifactContextState.Absent);
        ArtifactContext.Read("").State.Should().Be(ArtifactContextState.Absent);
    }
}
