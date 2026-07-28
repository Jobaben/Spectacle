using System.Linq;
using FluentAssertions;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class MermaidCheckerTests
{
    private static string[] Rules(string markdown) =>
        MermaidChecker.Check(markdown).Select(i => i.Rule).ToArray();

    // ---------- clean diagrams ----------

    [Fact]
    public void A_described_diagram_is_clean()
    {
        var md = "# Design\n\n```mermaid\nflowchart TD\n  accTitle: Login\n"
               + "  accDescr: A client posts credentials and receives a token.\n  A-->B\n```\n";
        MermaidChecker.Check(md).Should().BeEmpty();
    }

    [Fact]
    public void An_accTitle_alone_counts_as_a_description()
    {
        // accTitle becomes the SVG's <title>, which is the diagram's accessible name — the same job
        // alt text does for an image. accDescr adds detail on top; requiring both would flag
        // diagrams that are already announced.
        Rules("```mermaid\nflowchart TD\n  accTitle: Login flow\n  A-->B\n```\n").Should().BeEmpty();
    }

    [Fact]
    public void A_braced_accDescr_counts_as_a_description()
    {
        var md = "```mermaid\nflowchart TD\n  accDescr {\n    Two paragraphs\n    of detail.\n  }\n  A-->B\n```\n";
        Rules(md).Should().BeEmpty();
    }

    [Fact]
    public void A_fence_in_another_language_is_not_a_diagram()
    {
        Rules("```json\n{\"a\": 1}\n```\n").Should().BeEmpty();
        Rules("```\nplain text\n```\n").Should().BeEmpty();
    }

    [Fact]
    public void The_language_tag_is_matched_case_insensitively()
    {
        Rules("```MERMAID\nflowchart TD\n  accDescr: x\n  A-->B\n```\n").Should().BeEmpty();
    }

    // ---------- empty ----------

    [Fact]
    public void An_empty_fence_is_reported_at_the_fence()
    {
        var issues = MermaidChecker.Check("# T\n\n```mermaid\n```\n");
        issues.Should().HaveCount(1);
        issues[0].Rule.Should().Be(MermaidChecker.EmptyRule);
        issues[0].Line.Should().Be(3);
    }

    [Fact]
    public void A_fence_holding_only_a_comment_is_empty()
    {
        Rules("```mermaid\n%% a note and nothing else\n\n```\n")
            .Should().Equal(MermaidChecker.EmptyRule);
    }

    [Fact]
    public void An_empty_diagram_reports_only_that_it_is_empty()
    {
        // It has no type to check and nothing to describe; three findings for one hole in the
        // document would bury the one instruction that fixes it.
        Rules("```mermaid\n```\n").Should().Equal(MermaidChecker.EmptyRule);
    }

    // ---------- unknown type ----------

    [Fact]
    public void A_diagram_type_mermaid_does_not_ship_is_reported()
    {
        // zenuml is in mermaid's documentation but not in the vendored bundle — it ships as a
        // separate plugin, so a zenuml diagram silently fails to draw.
        var issues = MermaidChecker.Check("line1\n\n```mermaid\nzenuml\n  A->B\n```\n");
        issues.Should().Contain(i => i.Rule == MermaidChecker.UnknownTypeRule);
        issues.Single(i => i.Rule == MermaidChecker.UnknownTypeRule).Line.Should().Be(4);
    }

    [Fact]
    public void A_misspelled_diagram_type_is_reported()
    {
        var issues = MermaidChecker.Check("```mermaid\nflowchartt TD\n  accDescr: x\n  A-->B\n```\n");
        issues.Should().HaveCount(1);
        issues[0].Rule.Should().Be(MermaidChecker.UnknownTypeRule);
        issues[0].Message.Should().Contain("flowchartt");
    }

    [Theory]
    [InlineData("flowchart TD")]
    [InlineData("graph TD;")]
    [InlineData("sequenceDiagram")]
    [InlineData("classDiagram")]
    [InlineData("stateDiagram-v2")]
    [InlineData("erDiagram")]
    [InlineData("journey")]
    [InlineData("gantt")]
    [InlineData("pie showData")]
    [InlineData("gitGraph:")]
    [InlineData("mindmap")]
    [InlineData("timeline")]
    [InlineData("quadrantChart")]
    [InlineData("xychart-beta")]
    [InlineData("C4Context")]
    [InlineData("sankey-beta")]
    [InlineData("requirementDiagram")]
    [InlineData("block-beta")]
    public void A_supported_diagram_type_is_not_reported(string head)
    {
        // The keyword may carry a direction, an option, or a trailing colon; only the first token
        // names the type.
        Rules($"```mermaid\n{head}\n  accDescr: x\n```\n")
            .Should().NotContain(MermaidChecker.UnknownTypeRule);
    }

    [Fact]
    public void A_miscapitalized_diagram_type_is_reported()
    {
        // Mermaid's detectors are case-sensitive: it draws classDiagram and refuses classdiagram. A
        // model that lowercases the keyword writes a diagram that looks right and draws nothing, so
        // the comparison is exact rather than forgiving.
        Rules("```mermaid\nclassdiagram\n  accDescr: x\n  class T\n```\n")
            .Should().Contain(MermaidChecker.UnknownTypeRule);
        Rules("```mermaid\nsequencediagram\n  accDescr: x\n  A->>B: hi\n```\n")
            .Should().Contain(MermaidChecker.UnknownTypeRule);
        Rules("```mermaid\nclassDiagram\n  accDescr: x\n  class T\n```\n")
            .Should().BeEmpty();
    }

    [Fact]
    public void The_fence_language_stays_case_insensitive_even_so()
    {
        // The keyword's casing is mermaid's grammar; the fence tag's casing is a Markdown
        // convention, and every highlighter treats it as case-insensitive.
        Rules("```MERMAID\nflowchart TD\n  accDescr: x\n  A-->B\n```\n").Should().BeEmpty();
    }

    [Fact]
    public void Mermaids_own_front_matter_is_skipped_before_the_type_is_read()
    {
        // A diagram may open with a --- YAML header carrying title/config. The diagram keyword comes
        // after it, and reading the header's first line as the type would flag every such diagram.
        var md = "```mermaid\n---\ntitle: Rollout\nconfig:\n  theme: base\n---\ngantt\n"
               + "  accDescr: The rollout schedule.\n  section S\n```\n";
        Rules(md).Should().BeEmpty();
    }

    [Fact]
    public void An_init_directive_is_skipped_before_the_type_is_read()
    {
        var md = "```mermaid\n%%{init: {\"theme\":\"base\"}}%%\ngraph TD;\n  accDescr: x\n  A-->B\n```\n";
        Rules(md).Should().BeEmpty();
    }

    // ---------- missing description ----------

    [Fact]
    public void A_diagram_with_no_description_is_reported_at_the_fence()
    {
        var issues = MermaidChecker.Check("# T\n\n```mermaid\nflowchart TD\n  A-->B\n```\n");
        issues.Should().HaveCount(1);
        issues[0].Rule.Should().Be(MermaidChecker.MissingDescriptionRule);
        issues[0].Line.Should().Be(3);
    }

    [Fact]
    public void A_word_merely_starting_with_accDescr_is_not_a_description()
    {
        // The directive needs its delimiter; a node happening to be named accDescription is prose.
        Rules("```mermaid\nflowchart TD\n  accDescription-->B\n```\n")
            .Should().Equal(MermaidChecker.MissingDescriptionRule);
    }

    // ---------- several diagrams, and line accuracy ----------

    [Fact]
    public void Findings_point_at_the_lines_of_the_real_file()
    {
        // The document's own YAML header shifts every line below it; a finding that ignored the
        // offset would send the authoring agent to the wrong place.
        var md = "---\ntitle: doc\n---\n\n# H\n\n```mermaid\nnotADiagram\n```\n";
        var issues = MermaidChecker.Check(md);
        issues.Should().HaveCount(2);
        issues.Single(i => i.Rule == MermaidChecker.MissingDescriptionRule).Line.Should().Be(7);
        issues.Single(i => i.Rule == MermaidChecker.UnknownTypeRule).Line.Should().Be(8);
    }

    [Fact]
    public void Every_diagram_in_the_document_is_checked()
    {
        var md = "```mermaid\nflowchart TD\n  A-->B\n```\n\ntext\n\n```mermaid\nbogus\n```\n";
        var issues = MermaidChecker.Check(md);
        issues.Select(i => (i.Line, i.Rule)).Should().Equal(
            (1, MermaidChecker.MissingDescriptionRule),
            (8, MermaidChecker.MissingDescriptionRule),
            (9, MermaidChecker.UnknownTypeRule));
    }

    [Fact]
    public void Findings_are_ordered_by_line()
    {
        var md = "```mermaid\nbogus\n```\n\n```mermaid\n```\n";
        MermaidChecker.Check(md).Select(i => i.Line).Should().BeInAscendingOrder();
    }

    [Fact]
    public void A_document_with_no_diagram_is_silent()
    {
        MermaidChecker.Check("# Title\n\nJust prose, and a [link](#title).\n").Should().BeEmpty();
        MermaidChecker.Check(null).Should().BeEmpty();
        MermaidChecker.Check("").Should().BeEmpty();
    }
}
