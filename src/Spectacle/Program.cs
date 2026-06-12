using System.IO;
using System.Windows;
using Spectacle.Cli;
using Spectacle.Files;
using Spectacle.Install;
using Spectacle.Render;

namespace Spectacle;

public static class Program
{
    private const string UsageText = """
        Spectacle — Markdown viewer

        Usage:
          Spectacle.exe <file.md|file.markdown>   Open and render a Markdown file
          Spectacle.exe <file> --stats            Print document statistics and exit
          Spectacle.exe <file> --export-html [out] Export rendered HTML and exit
          Spectacle.exe <file> --export-html --light  Export using the light theme
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
            CliCommand.Stats stats => DoStats(stats.Path),
            CliCommand.ExportHtml export => DoExportHtml(export.Path, export.OutputPath, export.Light),
            CliCommand.Open open => DoOpen(open.Path),
            _ => Print(UsageText, 0),
        };
    }

    private static int DoOpen(string path)
    {
        if (!ValidateSource(path)) return 2;

        var app = new App();
        var window = new MainWindow(path);
        return app.Run(window);
    }

    private static int DoStats(string path)
    {
        if (!ValidateSource(path)) return 2;

        var stats = DocumentStats.Compute(File.ReadAllText(path));
        Console.WriteLine($"""
            {Path.GetFileName(path)}
              Words:        {stats.Words:N0}
              Reading time: ~{stats.ReadingTimeMinutes} min
              Characters:   {stats.Characters:N0}
              Lines:        {stats.Lines:N0}
              Headings:     {stats.Headings:N0}
              Code blocks:  {stats.CodeBlocks:N0}
              Links:        {stats.Links:N0}
              Images:       {stats.Images:N0}
            """);
        return 0;
    }

    private static int DoExportHtml(string path, string? outputPath, bool light)
    {
        if (!ValidateSource(path)) return 2;

        var theme = light ? PreviewTheme.Light : PreviewTheme.Dark;
        var title = Path.GetFileNameWithoutExtension(path) ?? "document";
        var html = HtmlExporter.FromMarkdown(File.ReadAllText(path), theme, title);
        var target = outputPath ?? Path.ChangeExtension(path, ".html");
        File.WriteAllText(target, html);
        Console.WriteLine($"Exported {Path.GetFullPath(target)}");
        return 0;
    }

    private static bool ValidateSource(string path)
    {
        if (!FileGuard.IsAllowed(path))
        {
            Console.Error.WriteLine($"Spectacle only opens .md and .markdown files. Refusing: {path}");
            return false;
        }
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"File not found: {path}");
            return false;
        }
        return true;
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
