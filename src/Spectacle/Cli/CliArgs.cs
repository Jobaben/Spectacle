using System.Collections.Generic;

namespace Spectacle.Cli;

public abstract record CliCommand
{
    public sealed record Open(string Path) : CliCommand;
    public sealed record Register : CliCommand;
    public sealed record Unregister : CliCommand;
    public sealed record Help : CliCommand;
    public sealed record Version : CliCommand;
    public sealed record Stats(string Path) : CliCommand;
    public sealed record ExportHtml(string Path, string? OutputPath) : CliCommand;
    public sealed record RevisionPlan(string Path, string? OutputPath, bool Json, bool UnresolvedOnly) : CliCommand;
    public sealed record ReviewSummary(string Path, bool Json) : CliCommand;
    public sealed record Lint(string Path, bool Json) : CliCommand;
    public sealed record Outline(string Path, bool Json) : CliCommand;
    public sealed record Checklist(string Path, bool Json) : CliCommand;
    public sealed record CheckLinks(string Path, bool Json) : CliCommand;
    public sealed record Diff(string Path, string OtherPath, bool Json) : CliCommand;
    public sealed record CheckStructure(string Path, bool Json) : CliCommand;
    public sealed record CheckTables(string Path, bool Json) : CliCommand;
    public sealed record CheckFences(string Path, bool Json) : CliCommand;
    public sealed record CheckPaths(string Path, bool Json) : CliCommand;
    public sealed record CheckSections(string Path, string? Required, bool Json, string? ConfigPath = null) : CliCommand;
    public sealed record CheckDuplication(string Path, bool Json) : CliCommand;
    public sealed record CheckAltText(string Path, bool Json) : CliCommand;
    public sealed record CheckEmphasisHeading(string Path, bool Json) : CliCommand;
    public sealed record Review(string Path, bool Json, string? Baseline = null, bool Sarif = false) : CliCommand;
}

public static class CliArgs
{
    public static CliCommand Parse(string[] args)
    {
        if (args.Length == 0) return new CliCommand.Help();

        // Split into flags (start with '-') and positionals, preserving order so the
        // first positional is the source file and the second (export only) is the
        // optional output path. This keeps `<file> --stats` and `--stats <file>`
        // equivalent, matching how shells hand us arguments either way.
        string? path = null;
        string? secondPositional = null;
        var flags = new List<string>();
        foreach (var a in args)
        {
            if (a.StartsWith('-')) flags.Add(a);
            else if (path is null) path = a;
            else secondPositional ??= a;
        }

        // Flags that stand alone and never need a file take precedence.
        if (flags.Contains("-h") || flags.Contains("--help")) return new CliCommand.Help();
        if (flags.Contains("--version")) return new CliCommand.Version();
        if (flags.Contains("--register")) return new CliCommand.Register();
        if (flags.Contains("--unregister")) return new CliCommand.Unregister();

        if (flags.Contains("--stats"))
            return path is null ? new CliCommand.Help() : new CliCommand.Stats(path);

        if (flags.Contains("--export-html") || flags.Contains("--export"))
            return path is null ? new CliCommand.Help() : new CliCommand.ExportHtml(path, secondPositional);

        if (flags.Contains("--revision-plan") || flags.Contains("--revisions"))
            return path is null
                ? new CliCommand.Help()
                : new CliCommand.RevisionPlan(
                    path, secondPositional, flags.Contains("--json"), flags.Contains("--unresolved"));

        if (flags.Contains("--review-summary"))
            return path is null
                ? new CliCommand.Help()
                : new CliCommand.ReviewSummary(path, flags.Contains("--json"));

        if (flags.Contains("--lint"))
            return path is null ? new CliCommand.Help() : new CliCommand.Lint(path, flags.Contains("--json"));

        if (flags.Contains("--outline"))
            return path is null ? new CliCommand.Help() : new CliCommand.Outline(path, flags.Contains("--json"));

        if (flags.Contains("--checklist"))
            return path is null ? new CliCommand.Help() : new CliCommand.Checklist(path, flags.Contains("--json"));

        if (flags.Contains("--check-links"))
            return path is null ? new CliCommand.Help() : new CliCommand.CheckLinks(path, flags.Contains("--json"));

        // --diff needs both the source file and a second file to compare against.
        if (flags.Contains("--diff"))
            return path is null || secondPositional is null
                ? new CliCommand.Help()
                : new CliCommand.Diff(path, secondPositional, flags.Contains("--json"));

        if (flags.Contains("--check-structure"))
            return path is null ? new CliCommand.Help() : new CliCommand.CheckStructure(path, flags.Contains("--json"));

        if (flags.Contains("--check-tables"))
            return path is null ? new CliCommand.Help() : new CliCommand.CheckTables(path, flags.Contains("--json"));

        if (flags.Contains("--check-fences"))
            return path is null ? new CliCommand.Help() : new CliCommand.CheckFences(path, flags.Contains("--json"));

        if (flags.Contains("--check-paths"))
            return path is null ? new CliCommand.Help() : new CliCommand.CheckPaths(path, flags.Contains("--json"));

        // --check-sections takes the required sections as a second positional comma-separated
        // list (mirroring how --diff consumes that slot). The list is optional: when omitted,
        // the sections are resolved from a `.spectacle.json` config — an explicit
        // `--config=<path>` if given, else the nearest one discovered above the spec. An
        // explicit inline list always wins over config.
        if (flags.Contains("--check-sections"))
            return path is null
                ? new CliCommand.Help()
                : new CliCommand.CheckSections(
                    path, secondPositional, flags.Contains("--json"), FlagValue(flags, "--config="));

        if (flags.Contains("--check-duplication") || flags.Contains("--check-duplicates"))
            return path is null ? new CliCommand.Help() : new CliCommand.CheckDuplication(path, flags.Contains("--json"));

        if (flags.Contains("--check-alt-text") || flags.Contains("--check-alt"))
            return path is null ? new CliCommand.Help() : new CliCommand.CheckAltText(path, flags.Contains("--json"));

        if (flags.Contains("--check-emphasis-heading") || flags.Contains("--check-emphasis"))
            return path is null ? new CliCommand.Help() : new CliCommand.CheckEmphasisHeading(path, flags.Contains("--json"));

        // --review takes a single spec, a directory (batch review), and optionally a
        // baseline to diff against. --baseline names the older version via the second
        // positional, mirroring how --diff consumes it; the second positional is left as
        // the baseline only when --baseline is present (a directory has no second positional).
        if (flags.Contains("--review"))
        {
            if (path is null) return new CliCommand.Help();
            var baseline = flags.Contains("--baseline") ? secondPositional : null;
            // --baseline with no file to compare against is a misuse; show help.
            if (flags.Contains("--baseline") && baseline is null) return new CliCommand.Help();
            return new CliCommand.Review(
                path, flags.Contains("--json"), baseline, flags.Contains("--sarif"));
        }

        // No recognized flag: open the file if we have one, otherwise show help
        // (covers a lone unknown flag such as `--what`).
        return path is null ? new CliCommand.Help() : new CliCommand.Open(path);
    }

    // Returns the value of a `prefix=value` flag (e.g. `--config=foo.json`), or null if
    // absent. Keeping the value on the flag token avoids pairing logic in the positional
    // split, where a bare `--config foo.json` would otherwise eat the second positional.
    private static string? FlagValue(List<string> flags, string prefix)
    {
        var match = flags.FindLast(f => f.StartsWith(prefix, System.StringComparison.Ordinal));
        return match?[prefix.Length..];
    }
}
