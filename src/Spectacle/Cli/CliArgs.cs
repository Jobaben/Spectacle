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
    public sealed record RevisionPlan(string Path, string? OutputPath, bool Json) : CliCommand;
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
                : new CliCommand.RevisionPlan(path, secondPositional, flags.Contains("--json"));

        // No recognized flag: open the file if we have one, otherwise show help
        // (covers a lone unknown flag such as `--what`).
        return path is null ? new CliCommand.Help() : new CliCommand.Open(path);
    }
}
