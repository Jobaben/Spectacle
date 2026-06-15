using FluentAssertions;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class BareUrlCheckerTests
{
    [Fact]
    public void Bare_url_in_prose_is_flagged()
    {
        var urls = BareUrlChecker.Check("See https://example.com for details.\n");

        urls.Should().ContainSingle();
        urls[0].Line.Should().Be(1);
        urls[0].Url.Should().Contain("example.com");
    }

    [Fact]
    public void Explicit_autolink_is_left_alone()
    {
        // <url> is the CommonMark "render this URL as a link on purpose" syntax — intentional.
        BareUrlChecker.Check("See <https://example.com> for details.\n").Should().BeEmpty();
    }

    [Fact]
    public void Proper_descriptive_link_is_left_alone()
    {
        BareUrlChecker.Check("See [the docs](https://example.com) for details.\n").Should().BeEmpty();
    }

    [Fact]
    public void Url_in_a_code_span_is_left_alone()
    {
        // A URL written as a literal value (an endpoint, an example) belongs in code — never a link.
        BareUrlChecker.Check("Call `https://example.com/v1/users` to list users.\n").Should().BeEmpty();
    }

    [Fact]
    public void Url_in_a_fenced_code_block_is_ignored()
    {
        BareUrlChecker.Check("```\ncurl https://example.com\n```\n").Should().BeEmpty();
    }

    [Fact]
    public void Scheme_less_www_url_is_flagged_with_the_text_as_written()
    {
        var urls = BareUrlChecker.Check("Visit www.example.com today.\n");

        urls.Should().ContainSingle();
        // The reported text is the literal as authored, not Markdig's protocol-normalized Url.
        urls[0].Url.Should().Be("www.example.com");
    }

    [Fact]
    public void Multiple_bare_urls_are_reported_in_line_order()
    {
        var urls = BareUrlChecker.Check("First https://a.com line.\n\nThen https://b.com line.\n");

        urls.Should().HaveCount(2);
        urls[0].Line.Should().Be(1);
        urls[1].Line.Should().Be(3);
    }

    [Fact]
    public void Null_or_empty_input_is_clean()
    {
        BareUrlChecker.Check(null).Should().BeEmpty();
        BareUrlChecker.Check("").Should().BeEmpty();
    }
}
