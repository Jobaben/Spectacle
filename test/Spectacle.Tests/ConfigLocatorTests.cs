using System;
using System.IO;
using FluentAssertions;
using Spectacle.Cli;
using Xunit;

namespace Spectacle.Tests;

public class ConfigLocatorTests : IDisposable
{
    private readonly string _tmp =
        Path.Combine(Path.GetTempPath(), "spectacle-cfg-" + Guid.NewGuid().ToString("N"));

    public ConfigLocatorTests() => Directory.CreateDirectory(_tmp);

    public void Dispose()
    {
        try { Directory.Delete(_tmp, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    [Fact]
    public void Find_locates_config_in_the_start_directory()
    {
        File.WriteAllText(Path.Combine(_tmp, ConfigLocator.FileName), "{}");

        ConfigLocator.Find(_tmp).Should().Be(Path.Combine(_tmp, ConfigLocator.FileName));
    }

    [Fact]
    public void Find_walks_up_to_a_parent_directory()
    {
        File.WriteAllText(Path.Combine(_tmp, ConfigLocator.FileName), "{}");
        var nested = Path.Combine(_tmp, "a", "b");
        Directory.CreateDirectory(nested);

        ConfigLocator.Find(nested).Should().Be(Path.Combine(_tmp, ConfigLocator.FileName));
    }

    [Fact]
    public void Find_returns_null_when_no_config_exists()
    {
        var nested = Path.Combine(_tmp, "deep", "nesting");
        Directory.CreateDirectory(nested);

        ConfigLocator.Find(nested).Should().BeNull();
    }

    [Fact]
    public void Resolve_prefers_an_explicit_config_path_over_discovery()
    {
        // A discoverable config sits next to the spec...
        File.WriteAllText(Path.Combine(_tmp, ConfigLocator.FileName),
            """{ "requiredSections": ["Discovered"] }""");
        var spec = Path.Combine(_tmp, "spec.md");
        File.WriteAllText(spec, "# Spec\n");

        // ...but an explicit --config=<path> wins.
        var explicitCfg = Path.Combine(_tmp, "custom.json");
        File.WriteAllText(explicitCfg, """{ "requiredSections": ["Explicit"] }""");

        ConfigLocator.Resolve(spec, explicitCfg).RequiredSections.Should().Equal("Explicit");
    }

    [Fact]
    public void Resolve_discovers_the_nearest_config_when_no_explicit_path()
    {
        File.WriteAllText(Path.Combine(_tmp, ConfigLocator.FileName),
            """{ "requiredSections": ["Overview", "Goals"] }""");
        var sub = Path.Combine(_tmp, "specs");
        Directory.CreateDirectory(sub);
        var spec = Path.Combine(sub, "spec.md");
        File.WriteAllText(spec, "# Spec\n");

        ConfigLocator.Resolve(spec, explicitConfigPath: null)
            .RequiredSections.Should().Equal("Overview", "Goals");
    }

    [Fact]
    public void Resolve_yields_empty_config_when_nothing_is_found()
    {
        var spec = Path.Combine(_tmp, "spec.md");
        File.WriteAllText(spec, "# Spec\n");

        ConfigLocator.Resolve(spec, explicitConfigPath: null).RequiredSections.Should().BeEmpty();
    }
}
