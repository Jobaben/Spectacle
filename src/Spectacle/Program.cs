using System.IO;
using System.Windows;
using Spectacle.Cli;
using Spectacle.Files;
using Spectacle.Install;

namespace Spectacle;

public static class Program
{
    private const string UsageText = """
        Spectacle — Markdown viewer

        Usage:
          Spectacle.exe <file.md|file.markdown>   Open and render a Markdown file
          Spectacle.exe --register                Register as default handler for .md/.markdown (per-user)
          Spectacle.exe --unregister              Remove the file association
          Spectacle.exe --help, -h                Show this help
          Spectacle.exe --version                 Show version
        """;

    [STAThread]
    public static int Main(string[] args)
    {
        var command = CliArgs.Parse(args);
        return command switch
        {
            CliCommand.Help => Print(UsageText, 0),
            CliCommand.Version => Print(GetVersion(), 0),
            CliCommand.Register => DoRegister(),
            CliCommand.Unregister => DoUnregister(),
            CliCommand.Open open => DoOpen(open.Path),
            _ => Print(UsageText, 0),
        };
    }

    private static int DoOpen(string path)
    {
        if (!FileGuard.IsAllowed(path))
        {
            Console.Error.WriteLine($"Spectacle only opens .md and .markdown files. Refusing: {path}");
            return 2;
        }
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"File not found: {path}");
            return 2;
        }

        var app = new App();
        var window = new MainWindow(path);
        return app.Run(window);
    }

    private static int DoRegister()
    {
        var exe = Environment.ProcessPath
            ?? throw new InvalidOperationException("Could not resolve own executable path.");
        new FileAssocInstaller(exe).Register();
        Console.WriteLine("Registered .md and .markdown to Spectacle for the current user.");
        return 0;
    }

    private static int DoUnregister()
    {
        var exe = Environment.ProcessPath ?? "";
        new FileAssocInstaller(exe).Unregister();
        Console.WriteLine("Removed Spectacle file associations for the current user.");
        return 0;
    }

    private static int Print(string text, int code) { Console.WriteLine(text); return code; }

    private static string GetVersion() =>
        typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0";
}
