namespace Spectacle.Cli;

public abstract record CliCommand
{
    public sealed record Open(string Path) : CliCommand;
    public sealed record Register : CliCommand;
    public sealed record Unregister : CliCommand;
    public sealed record Help : CliCommand;
    public sealed record Version : CliCommand;
}

public static class CliArgs
{
    public static CliCommand Parse(string[] args)
    {
        if (args.Length == 0) return new CliCommand.Help();

        var first = args[0];
        return first switch
        {
            "-h" or "--help" => new CliCommand.Help(),
            "--version" => new CliCommand.Version(),
            "--register" => new CliCommand.Register(),
            "--unregister" => new CliCommand.Unregister(),
            _ when first.StartsWith('-') => new CliCommand.Help(),
            _ => new CliCommand.Open(first),
        };
    }
}
