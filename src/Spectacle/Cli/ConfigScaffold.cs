using System;
using System.IO;
using Spectacle.Render;

namespace Spectacle.Cli;

/// <summary>
/// Produces the starting <c>.spectacle.json</c> a team edits to adopt project-level review
/// settings. The config the rest of Spectacle reads (<see cref="SpectacleConfig"/>,
/// <see cref="ConfigLocator"/>) is otherwise something you have to author by hand and keep in
/// sync with the available check ids; this scaffolds a documented, valid file so
/// <c>--check-sections</c> and the <c>--review</c> gate can be turned on in one step.
///
/// The template's inline note lists the live check ids straight from
/// <see cref="ReviewChecks.All"/>, so the scaffold can never advertise a stale set.
/// </summary>
public static class ConfigScaffold
{
    /// <summary>The conventional config filename Spectacle discovers above a spec.</summary>
    public const string FileName = ".spectacle.json";

    /// <summary>
    /// The documented starter config: a sensible required-section template, an empty
    /// <c>disabledChecks</c> list, an empty front-matter template, and the gate's grading policy
    /// at its defaults — with a <c>"//"</c> note per field (an unknown key the tolerant parser
    /// ignores) explaining what it does and naming every valid check id.
    /// </summary>
    public static string Template()
    {
        var ids = string.Join(", ", ReviewChecks.All);
        return $$"""
            {
              "//": "Spectacle project config. Every key is optional; an unknown key is ignored.",
              "//requiredSections": "Headings every document under this folder must contain (enforced by --check-sections and --review).",
              "requiredSections": [
                "Overview",
                "Acceptance Criteria",
                "Non-Goals"
              ],
              "//requiredFrontMatter": "YAML front-matter keys every document must declare — how a workflow makes its own output traceable (enforced by --check-front-matter and --gate). A dotted key reads a nested field, e.g. 'run.model'. Leave empty if this project does not use front matter.",
              "requiredFrontMatter": [],
              "//disabledChecks": "Gating checks to turn off, by id. Valid ids: {{ids}}.",
              "disabledChecks": [],
              "//severity": "Regrade a check or a single rule for --gate: 'error' (blocks), 'warning' (reported, blocks only at --fail-on=warning), 'info' (advice, never blocks). A rule id wins over its check id. Prefer this over disabledChecks: a downgraded rule keeps appearing in every report, a disabled one disappears.",
              "severity": {},
              "//failOn": "The lowest severity that fails --gate: 'error' (default) or 'warning'.",
              "failOn": "error"
            }
            """;
    }

    /// <summary>
    /// Resolves where the scaffold should be written from the optional path argument: nothing
    /// means <see cref="FileName"/> in the current directory; an existing directory means the
    /// file inside it; any other value is taken verbatim (so a custom filename is honoured).
    /// <paramref name="isDirectory"/> is injected so the resolution is testable without disk.
    /// </summary>
    public static string ResolveTargetPath(string? pathArg, Func<string, bool> isDirectory)
    {
        if (string.IsNullOrWhiteSpace(pathArg)) return FileName;
        return isDirectory(pathArg) ? Path.Combine(pathArg, FileName) : pathArg;
    }
}
