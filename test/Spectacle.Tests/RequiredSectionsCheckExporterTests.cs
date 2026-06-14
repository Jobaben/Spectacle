using System;
using System.Text.Json;
using FluentAssertions;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class RequiredSectionsCheckExporterTests
{
    private static readonly MissingSection[] Missing =
    {
        new("Acceptance Criteria"),
        new("Non-Goals"),
    };

    [Fact]
    public void Text_lists_missing_with_counts()
    {
        var text = RequiredSectionsCheckExporter.Build(Missing, requiredCount: 3, @"C:\path\spec.md", json: false);

        text.Should().Contain("spec.md");
        text.Should().Contain("1/3");
        text.Should().Contain("Acceptance Criteria");
        text.Should().Contain("Non-Goals");
    }

    [Fact]
    public void Text_reports_clean_when_none()
    {
        RequiredSectionsCheckExporter.Build(Array.Empty<MissingSection>(), requiredCount: 2, @"C:\spec.md", json: false)
            .Should().Contain("2/2");
    }

    [Fact]
    public void Json_emits_structured_counts_and_missing()
    {
        var json = RequiredSectionsCheckExporter.Build(Missing, requiredCount: 3, @"C:\path\spec.md", json: true);

        var root = JsonDocument.Parse(json).RootElement;
        root.GetProperty("source").GetString().Should().Be(@"C:\path\spec.md");
        root.GetProperty("requiredCount").GetInt32().Should().Be(3);
        root.GetProperty("presentCount").GetInt32().Should().Be(1);
        root.GetProperty("missingCount").GetInt32().Should().Be(2);
        root.GetProperty("missing")[0].GetProperty("required").GetString().Should().Be("Acceptance Criteria");
    }
}
