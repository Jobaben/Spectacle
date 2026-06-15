using System;
using FluentAssertions;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class ReviewChecksTests
{
    private static readonly string[] None = Array.Empty<string>();

    [Fact]
    public void All_enabled_has_every_check_and_none_disabled()
    {
        foreach (var id in ReviewChecks.All)
            ReviewChecks.AllEnabled.Has(id).Should().BeTrue();
        ReviewChecks.AllEnabled.Disabled.Should().BeEmpty();
    }

    [Fact]
    public void No_inputs_leaves_everything_enabled()
    {
        var checks = ReviewChecks.Resolve(None, None, None);

        checks.Disabled.Should().BeEmpty();
        checks.Has("lint").Should().BeTrue();
    }

    [Fact]
    public void Skip_subtracts_named_checks()
    {
        var checks = ReviewChecks.Resolve(None, new[] { "duplication", "alt-text" }, None);

        checks.Has("duplication").Should().BeFalse();
        checks.Has("alt-text").Should().BeFalse();
        checks.Has("lint").Should().BeTrue();
        checks.Disabled.Should().Equal("duplication", "alt-text");
    }

    [Fact]
    public void Config_disabled_subtracts_named_checks()
    {
        var checks = ReviewChecks.Resolve(None, None, new[] { "paths" });

        checks.Has("paths").Should().BeFalse();
        checks.Disabled.Should().Equal("paths");
    }

    [Fact]
    public void Only_restricts_to_the_named_checks()
    {
        var checks = ReviewChecks.Resolve(new[] { "structure", "links" }, None, None);

        checks.Has("structure").Should().BeTrue();
        checks.Has("links").Should().BeTrue();
        checks.Has("lint").Should().BeFalse();
        // Disabled is reported in canonical order, so it is everything but the two kept.
        checks.Disabled.Should().NotContain("structure").And.NotContain("links");
        checks.Disabled.Should().Contain("lint");
    }

    [Fact]
    public void Only_then_skip_subtracts_from_the_allowlist()
    {
        var checks = ReviewChecks.Resolve(new[] { "structure", "links" }, new[] { "links" }, None);

        checks.Has("structure").Should().BeTrue();
        checks.Has("links").Should().BeFalse();
    }

    [Fact]
    public void Only_then_config_disabled_subtracts_from_the_allowlist()
    {
        var checks = ReviewChecks.Resolve(new[] { "structure", "links" }, None, new[] { "structure" });

        checks.Has("structure").Should().BeFalse();
        checks.Has("links").Should().BeTrue();
    }

    [Fact]
    public void Ids_are_normalized_for_case_and_whitespace()
    {
        var checks = ReviewChecks.Resolve(None, new[] { "  Duplication ", "ALT-TEXT" }, None);

        checks.Has("duplication").Should().BeFalse();
        checks.Has("alt-text").Should().BeFalse();
    }

    [Fact]
    public void Unknown_ids_are_ignored_in_resolution()
    {
        var checks = ReviewChecks.Resolve(None, new[] { "bogus", "duplication" }, None);

        checks.Has("duplication").Should().BeFalse();
        // A bogus id never narrows the universe, so everything else stays enabled.
        checks.Has("lint").Should().BeTrue();
    }

    [Fact]
    public void Only_with_no_known_ids_does_not_disable_everything()
    {
        // An all-typo --only must not silently turn the whole gate off; the universe is unchanged.
        var checks = ReviewChecks.Resolve(new[] { "bogus" }, None, None);

        checks.Disabled.Should().BeEmpty();
    }

    [Fact]
    public void Unknown_lists_only_unrecognized_ids()
    {
        ReviewChecks.Unknown(new[] { "lint", "bogus", "alt-text", "typo" })
            .Should().Equal("bogus", "typo");
        ReviewChecks.Unknown(new[] { "lint", "structure" }).Should().BeEmpty();
    }
}
