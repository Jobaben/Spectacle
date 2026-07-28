using System.Linq;
using FluentAssertions;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class TocCheckerTests
{
    [Fact]
    public void No_toc_section_is_a_no_op()
    {
        const string content = "# Spec\n\n## Overview\n\ntext\n\n## Details\n\ntext\n";

        TocChecker.Check(content).Should().BeEmpty();
    }

    [Fact]
    public void Contents_heading_with_no_links_is_not_a_toc()
    {
        // A "Contents" heading whose body is prose, not a link list, is not a TOC.
        const string content = "# Spec\n\n## Contents\n\nSee below.\n\n## Overview\n";

        TocChecker.Check(content).Should().BeEmpty();
    }

    [Fact]
    public void Complete_and_correct_toc_passes()
    {
        const string content =
            "# Spec\n\n## Table of Contents\n\n- [Overview](#overview)\n- [Details](#details)\n\n" +
            "## Overview\n\ntext\n\n## Details\n\ntext\n";

        TocChecker.Check(content).Should().BeEmpty();
    }

    [Fact]
    public void Toc_entry_to_missing_heading_is_flagged_stale()
    {
        const string content =
            "# Spec\n\n## Contents\n\n- [Overview](#overview)\n- [Removed](#removed-section)\n\n## Overview\n\ntext\n";

        var issues = TocChecker.Check(content);

        issues.Should().ContainSingle();
        issues[0].Rule.Should().Be(TocChecker.StaleEntryRule);
        issues[0].Anchor.Should().Be("#removed-section");
        issues[0].Line.Should().Be(6);
    }

    [Fact]
    public void Heading_missing_from_toc_is_flagged()
    {
        const string content =
            "# Spec\n\n## Contents\n\n- [Overview](#overview)\n\n## Overview\n\ntext\n\n## Details\n\ntext\n";

        var issues = TocChecker.Check(content);

        issues.Should().ContainSingle();
        issues[0].Rule.Should().Be(TocChecker.MissingEntryRule);
        issues[0].Anchor.Should().Be("#details");
        issues[0].Message.Should().Contain("Details");
    }

    [Fact]
    public void Subsection_below_the_toc_covered_depth_is_not_flagged()
    {
        // The TOC lists only H2s, so a deeper H3 it never meant to index is left alone.
        const string content =
            "# Spec\n\n## Contents\n\n- [Overview](#overview)\n\n## Overview\n\n### Sub Detail\n\ntext\n";

        TocChecker.Check(content).Should().BeEmpty();
    }

    [Fact]
    public void Toc_title_variants_are_recognized()
    {
        // "TOC" as a heading is recognized, and a stale entry beneath it is still caught.
        const string content =
            "# Spec\n\n## TOC\n\n- [Gone](#gone)\n\n## Overview\n";

        var issues = TocChecker.Check(content);

        issues.Should().ContainSingle();
        issues[0].Rule.Should().Be(TocChecker.StaleEntryRule);
    }

    [Fact]
    public void Reports_both_stale_and_missing_in_line_order()
    {
        const string content =
            "# Spec\n\n## Contents\n\n- [Overview](#overview)\n- [Gone](#gone)\n\n" +
            "## Overview\n\ntext\n\n## Details\n\ntext\n";

        var issues = TocChecker.Check(content);

        issues.Should().HaveCount(2);
        issues.Select(i => i.Rule).Should().Equal(TocChecker.StaleEntryRule, TocChecker.MissingEntryRule);
        issues.Should().BeInAscendingOrder(i => i.Line);
    }

    [Fact]
    public void Headings_before_the_toc_are_not_required_entries()
    {
        // The title and any intro heading above the TOC are not body sections it must index.
        const string content =
            "# Spec\n\n## Intro\n\ntext\n\n## Contents\n\n- [Overview](#overview)\n\n## Overview\n\ntext\n";

        TocChecker.Check(content).Should().BeEmpty();
    }
}
