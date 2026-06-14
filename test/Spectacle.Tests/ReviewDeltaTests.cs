using System.Linq;
using FluentAssertions;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class ReviewDeltaTests
{
    // h1 -> h3 (skipped-level) plus a TODO placeholder.
    private const string Baseline = "# Title\n\n### Too Deep\n\nTODO finish.\n";

    [Fact]
    public void Fixed_finding_is_one_the_revision_removed()
    {
        // Revision drops the TODO but keeps the skipped heading level.
        const string revised = "# Title\n\n### Too Deep\n\nAll done now.\n";

        var delta = ReviewDelta.Compute(ReviewReport.Compute(Baseline), ReviewReport.Compute(revised));

        delta.Fixed.Should().ContainSingle(f => f.Category == "lint" && f.Rule == "placeholder");
        delta.Persisting.Should().ContainSingle(f => f.Rule == "skipped-level");
        delta.New.Should().BeEmpty();
    }

    [Fact]
    public void New_finding_is_one_the_revision_introduced()
    {
        // Revision fixes the TODO but introduces a broken anchor link.
        const string revised = "# Title\n\n### Too Deep\n\nSee [x](#missing).\n";

        var delta = ReviewDelta.Compute(ReviewReport.Compute(Baseline), ReviewReport.Compute(revised));

        delta.Fixed.Should().ContainSingle(f => f.Rule == "placeholder");
        delta.New.Should().ContainSingle(f => f.Category == "links");
        delta.Persisting.Should().ContainSingle(f => f.Rule == "skipped-level");
    }

    [Fact]
    public void A_finding_that_only_moved_lines_is_persisting_not_fixed_plus_new()
    {
        // Same TODO, pushed down by extra leading content — identity ignores line number.
        const string moved = "# Title\n\nIntro paragraph added.\n\n### Too Deep\n\nTODO finish.\n";

        var delta = ReviewDelta.Compute(ReviewReport.Compute(Baseline), ReviewReport.Compute(moved));

        delta.Persisting.Should().Contain(f => f.Rule == "placeholder");
        delta.Fixed.Should().NotContain(f => f.Rule == "placeholder");
        delta.New.Should().NotContain(f => f.Rule == "placeholder");
    }

    [Fact]
    public void Fixing_one_of_several_identical_findings_counts_one_fixed_one_persisting()
    {
        // Two placeholders share the identity (category, rule, message); the revision resolves one.
        // A set diff would report 0 fixed — this guards the multiset diff.
        const string twoTodos = "# Title\n\nTODO one.\n\nTODO two.\n";
        const string oneTodo = "# Title\n\nTODO one.\n\nDone two.\n";

        var delta = ReviewDelta.Compute(ReviewReport.Compute(twoTodos), ReviewReport.Compute(oneTodo));

        delta.Fixed.Count(f => f.Rule == "placeholder").Should().Be(1);
        delta.Persisting.Count(f => f.Rule == "placeholder").Should().Be(1);
        delta.New.Should().BeEmpty();
    }

    [Fact]
    public void Adding_another_identical_finding_counts_one_new_one_persisting()
    {
        const string oneTodo = "# Title\n\nTODO one.\n";
        const string twoTodos = "# Title\n\nTODO one.\n\nTODO two.\n";

        var delta = ReviewDelta.Compute(ReviewReport.Compute(oneTodo), ReviewReport.Compute(twoTodos));

        delta.New.Count(f => f.Rule == "placeholder").Should().Be(1);
        delta.Persisting.Count(f => f.Rule == "placeholder").Should().Be(1);
        delta.Fixed.Should().BeEmpty();
    }

    [Fact]
    public void Clean_revision_of_clean_baseline_has_no_delta()
    {
        const string clean = "# Title\n\n## Section\n\nComplete prose.\n";

        var delta = ReviewDelta.Compute(ReviewReport.Compute(clean), ReviewReport.Compute(clean));

        delta.Fixed.Should().BeEmpty();
        delta.New.Should().BeEmpty();
        delta.Persisting.Should().BeEmpty();
        delta.RemainingIssueCount.Should().Be(0);
    }

    [Fact]
    public void Remaining_issue_count_is_new_plus_persisting()
    {
        const string revised = "# Title\n\n### Too Deep\n\nSee [x](#missing).\n";

        var delta = ReviewDelta.Compute(ReviewReport.Compute(Baseline), ReviewReport.Compute(revised));

        delta.RemainingIssueCount.Should().Be(delta.New.Count + delta.Persisting.Count);
    }

    [Fact]
    public void Tracks_checklist_progress_across_revisions()
    {
        const string before = "# Spec\n\n- [ ] one\n- [ ] two\n";
        const string after = "# Spec\n\n- [x] one\n- [ ] two\n";

        var delta = ReviewDelta.Compute(ReviewReport.Compute(before), ReviewReport.Compute(after));

        delta.BaselineChecklistDone.Should().Be(0);
        delta.RevisedChecklistDone.Should().Be(1);
        delta.RevisedChecklistTotal.Should().Be(2);
    }
}
