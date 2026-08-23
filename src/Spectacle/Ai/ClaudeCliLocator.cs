using System;
using System.IO;

namespace Spectacle.Ai;

/// <summary>
/// Finds a Claude Code CLI installation on the host machine.
///
/// When the CLI is present, the reader can close the write → gate → revise loop by itself: instead
/// of the reader copying a fix brief to the clipboard and the human ferrying it to an agent, the
/// app hands the brief to <c>claude -p</c> in a background process and the agent's saves land in
/// the open document — where the file watcher already turns each one into a loop iteration. When
/// the CLI is absent nothing changes: the clipboard path stays exactly as it was.
///
/// Detection is a PATH scan for the CLI's known executable names, done once per window. The
/// <see cref="OverrideVariable"/> environment variable pins a specific binary — for a portable
/// install the shell never put on PATH, or for tests — and when set it is authoritative: a pinned
/// path that does not exist means "not installed", not "fall back to whatever PATH finds".
/// </summary>
public static class ClaudeCliLocator
{
    public const string OverrideVariable = "SPECTACLE_CLAUDE_CLI";

    /// <summary>
    /// The executable names a Claude Code install answers to, in preference order: the native
    /// installer's binary, the npm global shim, and a bare extensionless shim (Git Bash-style
    /// installs put one of those on PATH too).
    /// </summary>
    public static readonly string[] CandidateNames = { "claude.exe", "claude.cmd", "claude" };

    /// <summary>The full path of the installed CLI, or <c>null</c> when none was found.</summary>
    public static string? Detect() => Detect(
        Environment.GetEnvironmentVariable(OverrideVariable),
        Environment.GetEnvironmentVariable("PATH"),
        File.Exists);

    /// <summary>
    /// The probe itself, with the environment and the filesystem passed in so it can be exercised
    /// without either.
    /// </summary>
    public static string? Detect(string? overridePath, string? pathValue, Func<string, bool> fileExists)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            var pinned = overridePath.Trim().Trim('"');
            return fileExists(pinned) ? pinned : null;
        }

        foreach (var entry in (pathValue ?? "").Split(Path.PathSeparator))
        {
            // Windows PATH entries are routinely quoted, padded, or plain broken; a bad entry is
            // skipped rather than allowed to abort the scan.
            var dir = entry.Trim().Trim('"');
            if (dir.Length == 0) continue;

            foreach (var name in CandidateNames)
            {
                string candidate;
                try { candidate = Path.Combine(dir, name); }
                catch (ArgumentException) { continue; }
                if (fileExists(candidate)) return candidate;
            }
        }

        return null;
    }
}
