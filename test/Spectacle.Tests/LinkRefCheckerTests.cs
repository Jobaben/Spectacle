using FluentAssertions;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class LinkRefCheckerTests
{
    [Fact]
    public void Undefined_full_reference_is_flagged()
    {
        var refs = LinkRefChecker.Check("See [the docs][api] for details.\n");

        refs.Should().ContainSingle();
        refs[0].Line.Should().Be(1);
        refs[0].Label.Should().Be("api");
        refs[0].Reference.Should().Be("[the docs][api]");
    }

    [Fact]
    public void Defined_full_reference_is_left_alone()
    {
        LinkRefChecker.Check("See [the docs][api].\n\n[api]: https://example.com/api\n").Should().BeEmpty();
    }

    [Fact]
    public void Undefined_collapsed_reference_is_flagged()
    {
        // [text][] is the collapsed form: its label is the visible text.
        var refs = LinkRefChecker.Check("See [api docs][] here.\n");

        refs.Should().ContainSingle();
        refs[0].Label.Should().Be("api docs");
    }

    [Fact]
    public void Defined_collapsed_reference_is_left_alone()
    {
        LinkRefChecker.Check("See [api docs][].\n\n[api docs]: https://example.com\n").Should().BeEmpty();
    }

    [Fact]
    public void Undefined_shortcut_reference_is_not_flagged()
    {
        // [label] with no second bracket is, per CommonMark, ordinary bracketed prose when
        // undefined — it renders cleanly, so it is never a defect.
        LinkRefChecker.Check("This is just [bracketed text] in a sentence.\n").Should().BeEmpty();
    }

    [Fact]
    public void Inline_link_is_left_alone()
    {
        LinkRefChecker.Check("See [the docs](https://example.com).\n").Should().BeEmpty();
    }

    [Fact]
    public void Reference_in_a_code_span_is_ignored()
    {
        LinkRefChecker.Check("The syntax `[text][label]` defines a reference link.\n").Should().BeEmpty();
    }

    [Fact]
    public void Reference_in_a_fenced_code_block_is_ignored()
    {
        LinkRefChecker.Check("```\n[text][missing]\n```\n").Should().BeEmpty();
    }

    [Fact]
    public void Label_matching_is_case_and_whitespace_insensitive()
    {
        // CommonMark normalizes labels: case-fold and collapse internal whitespace.
        LinkRefChecker.Check("See [docs][API  Ref].\n\n[api ref]: https://example.com\n").Should().BeEmpty();
    }

    [Fact]
    public void Undefined_reference_image_is_flagged()
    {
        var refs = LinkRefChecker.Check("![a diagram][diagram]\n");

        refs.Should().ContainSingle();
        refs[0].Label.Should().Be("diagram");
    }

    [Fact]
    public void Empty_input_yields_no_findings()
    {
        LinkRefChecker.Check("").Should().BeEmpty();
        LinkRefChecker.Check(null).Should().BeEmpty();
    }

    [Fact]
    public void Footnote_reference_is_not_treated_as_a_link_reference()
    {
        // A footnote marker is a single bracket pair; it is FootnoteChecker's concern, never
        // a link reference, and a footnote definition is not a usable link-reference label.
        LinkRefChecker.Check("A claim.[^1]\n\n[^1]: the source.\n").Should().BeEmpty();
    }
}
