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
    public sealed record Review(string Path, bool Json) : CliCommand;
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

        if (flags.Contains("--review"))
            return path is null ? new CliCommand.Help() : new CliCommand.Review(path, flags.Contains("--json"));

        // No recognized flag: open the file if we have one, otherwise show help
        // (covers a lone unknown flag such as `--what`).
        return path is null ? new CliCommand.Help() : new CliCommand.Open(path);
    }
}
