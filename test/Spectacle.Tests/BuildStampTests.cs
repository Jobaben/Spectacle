using System.Text.RegularExpressions;
using FluentAssertions;
using Spectacle.Cli;
using Xunit;

namespace Spectacle.Tests;

/// <summary>
/// What <c>--version</c> answers with.
///
/// The shape is asserted, never the literal revision: the sha in the running test assembly is
/// whatever the last build stamped, so pinning it would fail the moment the tree moves one commit
/// ahead of the binary — the very situation the stamp exists to reveal.
/// </summary>
public class BuildStampTests
{
    [Fact]
    public void Prefers_the_informational_version_when_the_build_stamped_a_revision()
    {
        BuildStamp.Describe("1.0.0+dbfc048.2026-08-23", "1.0.0.0")
            .Should().Be("1.0.0+dbfc048.2026-08-23");
    }

    [Fact]
    public void Falls_back_to_the_assembly_version_without_an_informational_one()
    {
        BuildStamp.Describe(null, "1.0.0.0").Should().Be("1.0.0.0");
        BuildStamp.Describe("   ", "1.0.0.0").Should().Be("1.0.0.0");
    }

    [Fact]
    public void Reports_a_version_even_with_nothing_to_read()
    {
        BuildStamp.Describe(null, null).Should().Be("0.0.0");
    }

    [Fact]
    public void Trims_what_the_build_stamped()
    {
        BuildStamp.Describe(" 1.0.0+dbfc048.2026-08-23 ", null)
            .Should().Be("1.0.0+dbfc048.2026-08-23");
    }

    /// <summary>
    /// Both branches are asserted rather than one being skipped: a build from a tree with no
    /// <c>.git</c> is a supported outcome, not an untested one, and the fallback is the only thing
    /// standing between that build and a crash on a version string it cannot parse.
    /// </summary>
    [Fact]
    public void Carries_this_build_s_commit_and_date_when_the_tree_is_a_repository()
    {
        if (!BuildStamp.Current.Contains('+'))
        {
            BuildStamp.Current.Should().MatchRegex(
                @"^\d+\.\d+\.\d+(\.\d+)?$",
                "with no .git to read, the stamp falls back to the plain version");
            return;
        }

        BuildStamp.Current.Should().MatchRegex(
            @"^\d+\.\d+\.\d+\+[0-9a-f]{7,40}\.\d{4}-\d{2}-\d{2}(\.dirty)?$",
            "the build stamps SourceRevisionId as <sha>.<commit date>, optionally marked dirty");
    }

    [Fact]
    public void Names_the_revision_exactly_once()
    {
        var parts = BuildStamp.Current.Split('+');
        if (parts.Length < 2)
        {
            return;
        }

        var revision = parts[1].Split('.')[0];

        Regex.Matches(BuildStamp.Current, Regex.Escape(revision)).Should().HaveCount(
            1, "setting SourceRevisionId *and* InformationalVersion appends the sha twice");
    }
}
