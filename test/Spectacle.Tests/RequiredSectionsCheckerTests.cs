using System.Linq;
using FluentAssertions;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class RequiredSectionsCheckerTests
{
    [Fact]
    public void All_required_sections_present_yields_no_missing()
    {
        const string content = "# Spec\n\n## Overview\n\nText.\n\n## Acceptance Criteria\n\n- [ ] a\n";

        RequiredSectionsChecker.Check(content, new[] { "Overview", "Acceptance Criteria" })
            .Should().BeEmpty();
    }

    [Fact]
    public void Reports_missing_section()
    {
        const string content = "# Spec\n\n## Overview\n\nText.\n";

        RequiredSectionsChecker.Check(content, new[] { "Overview", "Non-Goals" })
            .Should().ContainSingle().Which.Required.Should().Be("Non-Goals");
    }

    [Fact]
    public void Matching_is_case_insensitive_and_trimmed()
    {
        const string content = "# Spec\n\n##   acceptance CRITERIA  \n\nText.\n";

        RequiredSectionsChecker.Check(content, new[] { "Acceptance Criteria" })
            .Should().BeEmpty();
    }

    [Fact]
    public void Matches_heading_at_any_level()
    {
        const string content = "# Spec\n\n#### Non-Goals\n\nText.\n";

        RequiredSectionsChecker.Check(content, new[] { "Non-Goals" }).Should().BeEmpty();
    }

    [Fact]
    public void Match_is_full_text_not_substring()
    {
        // A required "Goals" must not be satisfied by a "Non-Goals" heading.
        const string content = "# Spec\n\n## Non-Goals\n\nText.\n";

        RequiredSectionsChecker.Check(content, new[] { "Goals" })
            .Should().ContainSingle().Which.Required.Should().Be("Goals");
    }

    [Fact]
    public void Missing_sections_preserve_requested_order()
    {
        const string content = "# Spec\n\n## Overview\n";

        var missing = RequiredSectionsChecker.Check(content, new[] { "Risks", "Overview", "Non-Goals" });

        missing.Select(m => m.Required).Should().Equal("Risks", "Non-Goals");
    }

    [Fact]
    public void Parse_splits_trims_and_dedups()
    {
        RequiredSectionsChecker.ParseRequired(" Overview , Acceptance Criteria ,,overview ")
            .Should().Equal("Overview", "Acceptance Criteria");
    }

    [Fact]
    public void Parse_of_empty_is_empty() =>
        RequiredSectionsChecker.ParseRequired("").Should().BeEmpty();
}
