using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using Spectacle.Export;
using Spectacle.Gate;
using Xunit;

namespace Spectacle.Tests;

/// <summary>
/// The CI-facing output formats: GitHub Actions workflow commands and JUnit XML. Both read the same
/// <see cref="FindingStream"/> the gate verdict does, so these tests are about the wire format —
/// levels, escaping, and counts a CI system will act on.
/// </summary>
public class WorkflowFormatExporterTests
{
    private const string Dirty =
        "# Title\n\nTODO: decide the token lifetime.\n\nWe should probably cache it.\n";

    private static BatchReviewEntry Entry(string path = "spec.md", string content = Dirty) =>
        new(path, ReviewReport.Compute(content));

    // ---------- GitHub Actions ----------

    [Fact]
    public void Github_emits_one_workflow_command_per_finding()
    {
        var lines = GitHubAnnotationExporter.Build(new[] { Entry() }).Split('\n');

        lines.Should().HaveCountGreaterThan(1);
        lines.Should().AllSatisfy(l => l.Should().StartWith("::"));
        lines.Should().Contain(l => l.Contains("file=spec.md") && l.Contains("lint/placeholder"));
    }

    [Fact]
    public void Github_maps_severities_to_its_own_levels()
    {
        var text = GitHubAnnotationExporter.Build(new[] { Entry() });

        text.Should().Contain("::error ");
        // "notice" is the workflow command vocabulary for the lowest level, so advice never looks
        // like a failure in the annotation list.
        text.Should().Contain("::notice ");
    }

    [Fact]
    public void Github_carries_the_line_number()
    {
        GitHubAnnotationExporter.Build(new[] { Entry() }).Should().Contain(",line=3,");
    }

    [Fact]
    public void Github_normalizes_backslash_paths()
    {
        GitHubAnnotationExporter.Build(new[] { Entry(@"specs\sub\spec.md") })
            .Should().Contain("file=specs/sub/spec.md");
    }

    [Fact]
    public void Github_escapes_the_delimiters_it_would_otherwise_break_on()
    {
        // A comma in a property value would end the property list; a newline would end the command.
        var report = ReviewReport.Compute("# T\n\n| a | b | c |\n| --- | --- | --- |\n| 1 |\n");
        var text = GitHubAnnotationExporter.Build(new[] { new BatchReviewEntry("spec.md", report) });

        foreach (var line in text.Split('\n').Where(l => l.Length != 0))
        {
            var properties = line[2..line.IndexOf("::", 2, System.StringComparison.Ordinal)];
            // Exactly the three properties the exporter writes: file, line, title.
            properties.Split(',').Should().HaveCount(3);
        }
    }

    [Fact]
    public void Github_respects_a_grading_policy()
    {
        var policy = GatePolicy.Create(new Dictionary<string, string> { ["lint"] = "warning" }, null);
        var text = GitHubAnnotationExporter.Build(new[] { Entry() }, policy);

        text.Should().Contain("::warning ").And.NotContain("::error ");
    }

    [Fact]
    public void Github_emits_nothing_for_a_clean_document()
    {
        GitHubAnnotationExporter.Build(new[] { Entry(content: "# Title\n\nA signed token is issued.\n") })
            .Should().BeEmpty();
    }

    // ---------- JUnit ----------

    private static XElement Junit(params BatchReviewEntry[] entries) =>
        XDocument.Parse(JUnitExporter.Build(entries)).Root!;

    [Fact]
    public void Junit_is_well_formed_xml_with_a_testsuites_root()
    {
        var root = Junit(Entry());

        root.Name.LocalName.Should().Be("testsuites");
        root.Attribute("name")!.Value.Should().Be("Spectacle");
    }

    [Fact]
    public void Junit_gives_each_document_a_suite_and_each_finding_a_case()
    {
        var root = Junit(Entry("a.md"), Entry("b.md"));

        root.Elements("testsuite").Should().HaveCount(2);
        root.Elements("testsuite").Select(s => s.Attribute("name")!.Value)
            .Should().Equal("a.md", "b.md");
        root.Descendants("testcase").Should().HaveCountGreaterThan(2);
    }

    [Fact]
    public void Junit_names_a_case_after_the_rule_and_its_line()
    {
        var names = Junit(Entry()).Descendants("testcase").Select(c => c.Attribute("name")!.Value);

        names.Should().Contain(n => n.Contains("lint/placeholder") && n.Contains("line 3"));
    }

    [Fact]
    public void Junit_records_a_gating_finding_as_a_failure_carrying_the_fix()
    {
        var failure = Junit(Entry()).Descendants("failure")
            .First(f => f.Attribute("type")!.Value == "lint/placeholder");

        failure.Attribute("message")!.Value.Should().NotBeNullOrWhiteSpace();
        failure.Value.Should().Contain("spec.md:3").And.Contain("Fix:");
    }

    [Fact]
    public void Junit_records_an_advisory_as_skipped_so_it_is_visible_but_never_red()
    {
        var root = Junit(Entry());

        root.Descendants("skipped").Should().HaveCount(1);
        root.Attribute("skipped")!.Value.Should().Be("1");
    }

    [Fact]
    public void Junit_counts_add_up()
    {
        var root = Junit(Entry());
        var suite = root.Element("testsuite")!;

        suite.Attribute("tests")!.Value.Should().Be(suite.Descendants("testcase").Count().ToString());
        suite.Attribute("failures")!.Value.Should().Be(suite.Descendants("failure").Count().ToString());
    }

    [Fact]
    public void Junit_gives_a_clean_document_one_passing_case()
    {
        // An empty suite reads as "nothing ran", which is the opposite of the fact being reported.
        var suite = Junit(Entry(content: "# Title\n\nA signed token is issued.\n")).Element("testsuite")!;

        suite.Attribute("tests")!.Value.Should().Be("1");
        suite.Attribute("failures")!.Value.Should().Be("0");
        suite.Descendants("testcase").Should().HaveCount(1);
        suite.Descendants("failure").Should().BeEmpty();
    }

    [Fact]
    public void Junit_escapes_markup_in_a_finding_message()
    {
        var report = ReviewReport.Compute("# T\n\nTODO: handle <script> & \"quotes\".\n");
        var xml = JUnitExporter.Build(new[] { new BatchReviewEntry("spec.md", report) });

        // Parsing is the assertion: unescaped markup would throw here.
        var act = () => XDocument.Parse(xml);
        act.Should().NotThrow();
        xml.Should().NotContain("<script>");
    }

    [Fact]
    public void Junit_respects_a_grading_policy()
    {
        var policy = GatePolicy.Create(new Dictionary<string, string> { ["lint"] = "info" }, null);
        var root = XDocument.Parse(JUnitExporter.Build(new[] { Entry() }, policy)).Root!;

        root.Descendants("failure").Should().BeEmpty();
        root.Descendants("skipped").Should().HaveCountGreaterThan(1);
    }
}
