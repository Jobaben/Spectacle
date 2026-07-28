using System.Linq;
using FluentAssertions;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class ChecklistAnalyzerTests
{
    [Fact]
    public void No_task_items_returns_empty()
    {
        const string content = "# Spec\n\nJust prose and a normal - bullet list.\n\n- not a task\n";

        ChecklistAnalyzer.Analyze(content).Should().BeEmpty();
    }

    [Fact]
    public void Detects_checked_and_unchecked_items_with_line_numbers()
    {
        const string content = "# Acceptance criteria\n\n- [ ] Supports OAuth2\n- [x] Returns problem+json\n";

        var items = ChecklistAnalyzer.Analyze(content);

        items.Should().HaveCount(2);
        items[0].Checked.Should().BeFalse();
        items[0].Text.Should().Be("Supports OAuth2");
        items[0].Line.Should().Be(3);
        items[1].Checked.Should().BeTrue();
        items[1].Text.Should().Be("Returns problem+json");
        items[1].Line.Should().Be(4);
    }

    [Fact]
    public void Accepts_uppercase_X_and_various_bullets()
    {
        const string content = "* [X] one\n+ [ ] two\n- [x] three\n";

        var items = ChecklistAnalyzer.Analyze(content);

        items.Should().HaveCount(3);
        items.Count(i => i.Checked).Should().Be(2);
    }

    [Fact]
    public void Detects_indented_task_items()
    {
        const string content = "- [ ] parent\n    - [x] nested child\n";

        ChecklistAnalyzer.Analyze(content).Should().HaveCount(2);
    }

    [Fact]
    public void Ignores_task_syntax_inside_code_fences()
    {
        const string content = "- [x] real item\n\n```\n- [ ] example in code\n```\n";

        ChecklistAnalyzer.Analyze(content).Should().ContainSingle()
            .Which.Text.Should().Be("real item");
    }
}
