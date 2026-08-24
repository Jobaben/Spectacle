using System;
using System.IO;

namespace Spectacle.Ai;

/// <summary>How a requested revision was disposed of.</summary>
public enum ArtifactRevisionStatus
{
    /// <summary>The run was launched.</summary>
    Started,

    /// <summary>Another run is already rewriting this document.</summary>
    Busy,
}

/// <summary>
/// What one revision request produced: whether it ran, the project root it resolved (<c>null</c>
/// when it fell back), the directory the process actually started in, and a one-line account for
/// the run chip. Every path returns a reason — a fallback that says nothing is the ungoverned
/// session this design exists to prevent.
/// </summary>
public sealed record ArtifactRevisionOutcome(
    ArtifactRevisionStatus Status, string? ProjectRoot, string WorkingDirectory, string Detail);

/// <summary>
/// The one path from the viewer to <c>claude -p</c>.
///
/// Nothing else in the application assembles an invocation: the sequence that has to hold for
/// cross-session continuity — resolve the project scope, read the artifact's inherited context,
/// decide whether the run may proceed at all, build the prompt that carries the handoff contract,
/// launch in the resolved root — lives here rather than at each call site, where it would be
/// tribal knowledge one caller at a time.
///
/// The invariant it exists to enforce: a run starts in the artifact's own Claude project root, so
/// the project's instructions, settings, rules and hooks load. Supplying an absolute filename does
/// not do that — the working directory does.
///
/// When no project root resolves, the run still goes ahead from the document's own folder, and the
/// reason travels with it to the chip. That is a deliberate choice over refusing: Claude Code loads
/// the user-scope <c>~/.claude</c> configuration whatever the working directory is, so a fallback
/// run is not ungoverned — it is governed by less. Refusing would have blocked revision of every
/// managed artifact that happens to live outside a configured project, which is a worse failure
/// than a run that says which scope it got.
/// </summary>
public sealed class ClaudeArtifactRevisionService
{
    private readonly Func<string, string, bool> _startRun;
    private readonly Func<string, string?> _readFile;
    private readonly Func<string, ClaudeProjectRootResult> _resolveRoot;

    /// <summary>Production wiring: a real runner, the real filesystem, the real walk.</summary>
    public ClaudeArtifactRevisionService(ClaudeRevisionRunner runner)
        : this(runner.TryStart, ReadOrNull, ClaudeProjectRoot.Resolve) { }

    /// <summary>
    /// The seam, with the launch, the read and the root walk passed in — so the decision logic is
    /// exercised without spawning a process or touching disk.
    /// </summary>
    public ClaudeArtifactRevisionService(
        Func<string, string, bool> startRun,
        Func<string, string?> readFile,
        Func<string, ClaudeProjectRootResult> resolveRoot)
    {
        _startRun = startRun;
        _readFile = readFile;
        _resolveRoot = resolveRoot;
    }

    /// <summary>
    /// What the chip says about a run whose scope fell back — short enough for a chip, and named
    /// so the reader can tell a fully-scoped run from a user-scope one at a glance.
    /// </summary>
    public static string FallbackNote(ArtifactContextView context) =>
        context.IsManaged
            ? "user scope only — no project root for this artifact"
            : "user scope only — no project root";

    /// <summary>
    /// Starts one revision of <paramref name="documentPath"/> carrying <paramref name="brief"/>.
    /// <paramref name="documentDirectory"/> is the document's own folder — the working directory
    /// used when no project root resolves.
    ///
    /// <paramref name="onLaunching"/> is handed the chip note for a fallback run (and
    /// <c>null</c> for a fully-scoped one) immediately *before* the process starts, so a caller
    /// can put it on screen without racing the runner's own <c>Started</c> event.
    /// </summary>
    public ArtifactRevisionOutcome Revise(
        string documentPath, string documentDirectory, string brief, Action<string?>? onLaunching = null)
    {
        var text = _readFile(documentPath);
        var context = text is null ? ArtifactContextView.None : ArtifactContext.Read(text);
        var root = _resolveRoot(documentDirectory);

        var workingDirectory = root.Path ?? documentDirectory;
        var detail = root.Path is not null
            ? $"project root: {root.Reason}"
            : $"no project root ({root.Reason}); running in the document's folder";
        if (text is null) detail += "; the document could not be read, so no inherited context was found";

        onLaunching?.Invoke(root.Path is null ? FallbackNote(context) : null);

        var started = _startRun(workingDirectory, ClaudeRevisionPrompt.Build(documentPath, brief, context));
        return new ArtifactRevisionOutcome(
            started ? ArtifactRevisionStatus.Started : ArtifactRevisionStatus.Busy,
            root.Path,
            workingDirectory,
            started ? detail : "a revision run is already in flight");
    }

    /// <summary>The document's text, or <c>null</c> when it cannot be read — never an exception.</summary>
    private static string? ReadOrNull(string path)
    {
        try { return File.ReadAllText(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}
