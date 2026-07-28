using System.Linq;
using FluentAssertions;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class SpecLinterTests
{
    [Fact]
    public void Clean_spec_has_no_findings()
    {
        const string content = "# Title\n\nReal, complete content here.\n\n## Details\n\nMore prose.\n";

        SpecLinter.Lint(content).Should().BeEmpty();
    }

    [Fact]
    public void Flags_todo_marker_with_line_number()
    {
        const string content = "# Title\n\nIntro paragraph.\n\nTODO: finish the error handling section.\n";

        var findings = SpecLinter.Lint(content);

        findings.Should().ContainSingle(f => f.Rule == "placeholder")
            .Which.Line.Should().Be(5);
    }

    [Fact]
    public void Flags_multiple_placeholder_markers()
    {
        const string content = "Intro.\n\nThis is TBD and also FIXME later.\n\nAnd a <placeholder> too.\n";

        var findings = SpecLinter.Lint(content).Where(f => f.Rule == "placeholder").ToList();

        findings.Should().HaveCount(3);
        findings.Select(f => f.Message).Should().Contain(m => m.Contains("TBD"));
        findings.Select(f => f.Message).Should().Contain(m => m.Contains("FIXME"));
        findings.Select(f => f.Message).Should().Contain(m => m.Contains("placeholder"));
    }

    [Fact]
    public void Ignores_markers_inside_fenced_code_blocks()
    {
        const string content = "# Title\n\nProse.\n\n```\n// TODO: this is example code, not a real gap\n```\n";

        SpecLinter.Lint(content).Should().NotContain(f => f.Rule == "placeholder");
    }

    [Fact]
    public void Does_not_flag_marker_as_substring_of_a_word()
    {
        // "TODOlist" / " XXXL" should not match — markers are whole words.
        const string content = "The TODOlist feature ships in size XXXL.\n";

        SpecLinter.Lint(content).Should().NotContain(f => f.Rule == "placeholder");
    }

    [Fact]
    public void Flags_empty_section_when_heading_followed_by_sibling_heading()
    {
        const string content = "## Overview\n## Details\n\nActual content.\n";

        var findings = SpecLinter.Lint(content).Where(f => f.Rule == "empty-section").ToList();

        findings.Should().ContainSingle().Which.Line.Should().Be(1);
    }

    [Fact]
    public void Flags_trailing_heading_with_no_content()
    {
        const string content = "# Spec\n\nIntro.\n\n## Appendix\n";

        SpecLinter.Lint(content).Where(f => f.Rule == "empty-section")
            .Should().ContainSingle().Which.Line.Should().Be(5);
    }

    [Fact]
    public void Does_not_flag_heading_that_has_a_subsection()
    {
        // "# A" has a level-2 child, so it is not itself empty; "## A.1" has prose.
        const string content = "# A\n\n## A.1\n\nContent under the subsection.\n";

        SpecLinter.Lint(content).Should().NotContain(f => f.Rule == "empty-section");
    }

    [Fact]
    public void Findings_are_ordered_by_line()
    {
        const string content = "## Empty\n## Has content\n\nTODO finish this\n";

        var lines = SpecLinter.Lint(content).Select(f => f.Line).ToList();

        lines.Should().BeInAscendingOrder();
    }
}
