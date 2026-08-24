using System;
using System.Collections.Generic;
using FluentAssertions;
using Spectacle.Ai;
using Xunit;

namespace Spectacle.Tests;

public class ClaudeProjectRootTests
{
    private const string Home = @"C:\Users\dev";

    // A fake filesystem: every path listed exists. Directories are listed without a trailing
    // separator; the probes compare case-insensitively, as Windows does.
    private static (Func<string, bool> Files, Func<string, bool> Dirs) Fs(
        IEnumerable<string> files, IEnumerable<string> dirs)
    {
        var f = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
        var d = new HashSet<string>(dirs, StringComparer.OrdinalIgnoreCase);
        return (p => f.Contains(p), p => d.Contains(p));
    }

    [Fact]
    public void The_repository_root_wins_over_a_nested_marker()
    {
        // A docs/.claude holding one narrow setting must not shadow the repository root: launching
        // there would silently drop the root's CLAUDE.md, settings, rules and hooks.
        var fs = Fs(
            files: new[] { @"C:\repo\CLAUDE.md" },
            dirs: new[] { @"C:\repo\.git", @"C:\repo\.claude", @"C:\repo\docs\.claude" });

        var result = ClaudeProjectRoot.Resolve(@"C:\repo\docs", Home, fs.Files, fs.Dirs);

        result.Path.Should().Be(@"C:\repo");
    }

    [Fact]
    public void A_nested_marker_is_the_root_when_the_repository_root_has_none()
    {
        // Outermost *marker-bearing* ancestor — the git root itself needs no marker of its own.
        var fs = Fs(
            files: new[] { @"C:\repo\projects\argus\CLAUDE.md" },
            dirs: new[] { @"C:\repo\.git" });

        var result = ClaudeProjectRoot.Resolve(@"C:\repo\projects\argus\docs", Home, fs.Files, fs.Dirs);

        result.Path.Should().Be(@"C:\repo\projects\argus");
    }

    [Fact]
    public void The_walk_stops_at_the_git_root()
    {
        // A parent checkout's .claude has nothing to do with this artifact.
        var fs = Fs(
            files: new[] { @"C:\work\CLAUDE.md", @"C:\work\inner\CLAUDE.md" },
            dirs: new[] { @"C:\work\inner\.git" });

        var result = ClaudeProjectRoot.Resolve(@"C:\work\inner\docs", Home, fs.Files, fs.Dirs);

        result.Path.Should().Be(@"C:\work\inner");
    }

    [Fact]
    public void The_walk_stops_at_a_linked_worktree_whose_git_is_a_file()
    {
        // In a linked worktree (and in a submodule) `.git` is a file holding a `gitdir:` pointer,
        // not a directory. Probing only for the directory would climb past the worktree root into
        // whatever encloses it — here, a sibling checkout's CLAUDE.md.
        var fs = Fs(
            files: new[] { @"C:\GIT\CLAUDE.md", @"C:\GIT\wt\.git", @"C:\GIT\wt\CLAUDE.md" },
            dirs: Array.Empty<string>());

        ClaudeProjectRoot.Resolve(@"C:\GIT\wt\docs", Home, fs.Files, fs.Dirs)
            .Path.Should().Be(@"C:\GIT\wt");
    }

    [Fact]
    public void The_home_directory_is_never_a_project_root()
    {
        // ~/.claude is user scope, present on nearly every machine. Selecting it would report a
        // resolved project root while loading no project configuration at all.
        var fs = Fs(files: Array.Empty<string>(), dirs: new[] { @"C:\Users\dev\.claude" });

        var result = ClaudeProjectRoot.Resolve(@"C:\Users\dev\notes", Home, fs.Files, fs.Dirs);

        result.Path.Should().BeNull();
        result.Reason.Should().Contain("no CLAUDE.md or .claude");
    }

    [Fact]
    public void A_marker_below_the_home_directory_still_resolves()
    {
        var fs = Fs(files: new[] { @"C:\Users\dev\argus\CLAUDE.md" }, dirs: new[] { @"C:\Users\dev\.claude" });

        ClaudeProjectRoot.Resolve(@"C:\Users\dev\argus\docs", Home, fs.Files, fs.Dirs)
            .Path.Should().Be(@"C:\Users\dev\argus");
    }

    [Fact]
    public void A_bare_claude_directory_is_marker_enough()
    {
        var fs = Fs(files: Array.Empty<string>(), dirs: new[] { @"C:\repo\.claude" });

        ClaudeProjectRoot.Resolve(@"C:\repo\docs\deep", Home, fs.Files, fs.Dirs)
            .Path.Should().Be(@"C:\repo");
    }

    [Fact]
    public void A_document_with_no_marker_anywhere_resolves_to_nothing_with_a_reason()
    {
        var fs = Fs(files: Array.Empty<string>(), dirs: Array.Empty<string>());

        var result = ClaudeProjectRoot.Resolve(@"C:\scratch\notes", Home, fs.Files, fs.Dirs);

        result.Path.Should().BeNull();
        result.Reason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void An_unusable_start_directory_resolves_to_nothing_rather_than_throwing()
    {
        // A background run must never die on a malformed path.
        var fs = Fs(files: Array.Empty<string>(), dirs: Array.Empty<string>());

        ClaudeProjectRoot.Resolve("   ", Home, fs.Files, fs.Dirs).Path.Should().BeNull();
    }

    [Fact]
    public void The_reason_names_the_marker_that_resolved_the_root()
    {
        var fs = Fs(files: new[] { @"C:\repo\CLAUDE.md" }, dirs: Array.Empty<string>());

        ClaudeProjectRoot.Resolve(@"C:\repo\docs", Home, fs.Files, fs.Dirs)
            .Reason.Should().Contain("CLAUDE.md");
    }
}
