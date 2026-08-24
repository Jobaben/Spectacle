using System;
using System.IO;

namespace Spectacle.Ai;

/// <summary>
/// Where a viewer-started Claude process resolved to, and why. <see cref="Path"/> is
/// <c>null</c> when no project scope could be established; <see cref="Reason"/> is a one-line
/// account either way, shown on the run chip so a fallback is never silent.
/// </summary>
public sealed record ClaudeProjectRootResult(string? Path, string Reason);

/// <summary>
/// Finds the directory a viewer-started <c>claude -p</c> must run in, so the artifact's own
/// project instructions, settings, rules and hooks load.
///
/// Supplying an absolute filename does not pull in that file's project configuration — the
/// working directory does. A run launched from the viewer's own folder, or from a document folder
/// beneath the project, is an ungoverned session that looks governed.
///
/// The rule is the <em>outermost</em> marker-bearing ancestor, not the nearest one.
/// <see cref="Spectacle.Cli.ConfigLocator"/> uses nearest-wins for <c>.spectacle.json</c>, which is
/// right for a config meant to be overridden per directory; Claude Code configuration is not that
/// shape. A <c>docs/.claude/</c> holding one narrow setting would shadow the repository root and
/// silently drop everything above it. Subdirectory instructions still apply, because Claude Code
/// reads nested <c>CLAUDE.md</c> files when it reads files beneath them.
///
/// Two ceilings bound the walk. The enclosing git repository, so it cannot escape into a parent
/// checkout. And the user profile directory, because nearly every machine has a <c>~/.claude/</c>
/// holding user-level settings — an unbounded walk would name the home directory as the project
/// root for any document under it, reporting a resolved scope while loading no project
/// configuration at all.
/// </summary>
public static class ClaudeProjectRoot
{
    /// <summary>Files whose presence marks a directory as a Claude Code project root.</summary>
    public static readonly string[] MarkerFiles = { "CLAUDE.md" };

    /// <summary>Directories whose presence marks a directory as a Claude Code project root.</summary>
    public static readonly string[] MarkerDirectories = { ".claude" };

    /// <summary>Resolves against the real filesystem and the real user profile.</summary>
    public static ClaudeProjectRootResult Resolve(string startDirectory) => Resolve(
        startDirectory,
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        File.Exists,
        Directory.Exists);

    /// <summary>
    /// The walk itself, with the home directory and the filesystem passed in so it can be
    /// exercised without either.
    /// </summary>
    public static ClaudeProjectRootResult Resolve(
        string startDirectory, string? userProfile, Func<string, bool> fileExists, Func<string, bool> directoryExists)
    {
        DirectoryInfo? dir;
        try { dir = new DirectoryInfo(Path.GetFullPath(startDirectory)); }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException or IOException)
        {
            return new ClaudeProjectRootResult(null, $"'{startDirectory}' is not a usable directory path");
        }

        var home = Normalize(userProfile);

        // Climb once, recording every marker-bearing ancestor and where the git root sits. The
        // last marker recorded before a ceiling is the outermost one.
        string? outermost = null;
        string? marker = null;
        string? gitRoot = null;

        for (; dir is not null; dir = dir.Parent)
        {
            var path = dir.FullName;

            // At or above the home directory the markers belong to the user scope, not to any
            // project — stop before considering them.
            if (home is not null && IsAtOrAbove(path, home)) break;

            foreach (var name in MarkerFiles)
                if (fileExists(Path.Combine(path, name))) { outermost = path; marker = name; }
            foreach (var name in MarkerDirectories)
                if (directoryExists(Path.Combine(path, name))) { outermost = path; marker ??= name; }

            // `.git` is a directory in a normal checkout and a *file* holding a `gitdir:` pointer
            // in a linked worktree or a submodule. Probing only for the directory would let the
            // walk climb straight past a worktree root into whatever encloses it.
            var git = Path.Combine(path, ".git");
            if (gitRoot is null && (directoryExists(git) || fileExists(git))) gitRoot = path;
            if (gitRoot is not null && string.Equals(gitRoot, path, StringComparison.OrdinalIgnoreCase)) break;
        }

        return outermost is null
            ? new ClaudeProjectRootResult(null, $"no CLAUDE.md or .claude directory above '{startDirectory}'")
            : new ClaudeProjectRootResult(outermost, $"{marker} in '{outermost}'");
    }

    private static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)); }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException or IOException)
        {
            return null;
        }
    }

    /// <summary>Whether <paramref name="path"/> is <paramref name="ceiling"/> or an ancestor of it.</summary>
    private static bool IsAtOrAbove(string path, string ceiling)
    {
        var p = Path.TrimEndingDirectorySeparator(path);
        if (string.Equals(p, ceiling, StringComparison.OrdinalIgnoreCase)) return true;
        // A drive root keeps its trailing separator, so appending another would never match.
        var prefix = p.EndsWith(Path.DirectorySeparatorChar) ? p : p + Path.DirectorySeparatorChar;
        return ceiling.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
