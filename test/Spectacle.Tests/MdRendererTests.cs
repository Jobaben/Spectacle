using System.IO;
using Xunit;
using FluentAssertions;
using Spectacle.Render;

namespace Spectacle.Tests;

public class MdRendererTests
{
    [Theory]
    [InlineData("tables")]
    [InlineData("code")]
    [InlineData("task-list")]
    [InlineData("footnotes")]
    public void Renders_fixture(string name)
    {
        var md = File.ReadAllText(Path.Combine("Fixtures", $"{name}.md"));
        var expected = File.ReadAllText(Path.Combine("Fixtures", $"{name}.html")).Trim();
        var actual = new MdRenderer().ToHtml(md).Trim();
        actual.Should().Be(expected);
    }

    [Fact]
    public void Code_block_gets_language_class()
    {
        var html = new MdRenderer().ToHtml("```cs\nvar x = 1;\n```\n");
        html.Should().Contain("language-cs");
    }

    [Fact]
    public void Soft_break_is_not_hard_break()
    {
        var html = new MdRenderer().ToHtml("line1\nline2\n");
        html.Should().NotContain("<br");
    }
}
