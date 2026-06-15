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
    /// The documented starter config: a sensible required-section template and an empty
    /// <c>disabledChecks</c> list, with a <c>"//"</c> note (an unknown key the tolerant parser
    /// ignores) explaining each field and naming every valid check id.
    /// </summary>
    public static string Template()
    {
        var ids = string.Join(", ", ReviewChecks.All);
        return $$"""
            {
              "//": "Spectacle project config. 'requiredSections' lists headings every spec under this folder must contain (enforced by --check-sections and --review). 'disabledChecks' turns off gating checks for --review by id. Valid ids: {{ids}}.",
              "requiredSections": [
                "Overview",
                "Acceptance Criteria",
                "Non-Goals"
              ],
              "disabledChecks": []
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
