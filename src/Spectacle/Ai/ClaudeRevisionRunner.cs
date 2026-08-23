using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Spectacle.Ai;

/// <summary>
/// What a run has done so far, counted from its stream events: assistant turns taken and file
/// edits written. Raised mid-run so the reader watches the run work instead of watching a spinner.
/// </summary>
public sealed record ClaudeRunProgress(int Turns, int Edits);

/// <summary>
/// The measured shape of a finished run, read from the CLI's own <c>result</c> event and the
/// stream that preceded it — never inferred from timing or exit codes alone.
/// </summary>
public sealed record ClaudeRunStats(int Turns, int Edits, long DurationMs, double? CostUsd, string? SessionId);

/// <summary>
/// The outcome of one background revision run. <see cref="Detail"/> is the chip-sized failure
/// reason (empty on success); <see cref="Message"/> is the agent's own closing text from the
/// stream's <c>result</c> event — the run explaining what it did, or why it could not.
/// <see cref="Stats"/> is <c>null</c> only when the stream never produced a result event (a CLI
/// too old to speak stream-json, or a launch that failed outright).
/// </summary>
public sealed record ClaudeRunResult(
    bool Succeeded, int ExitCode, string Detail, string Message = "", ClaudeRunStats? Stats = null);

/// <summary>
/// Runs <c>claude -p</c> as a background process to revise the open document in place.
///
/// The runner deliberately knows nothing about *what* changed: the agent saves the file, the
/// document watcher fires, and the existing pipeline re-renders, re-grades, and advances the loop
/// timeline exactly as it does for any other writer. What this class owns is the process — started
/// with the prompt on stdin (no command-line length or quoting limits), in the document's
/// directory, under <c>--permission-mode acceptEdits</c> so file edits are auto-approved while
/// anything that would need an interactive permission prompt is refused (print mode has nobody to
/// ask) — and the run's *account of itself*: stdout is the CLI's stream-json event feed
/// (<c>--output-format stream-json --verbose</c>), decoded line by line into progress
/// (<see cref="Progress"/>) and a final result carrying the agent's own closing message. A run
/// that saved nothing, or failed, is no longer indistinguishable from one that worked — the
/// stream says which it was, deterministically.
///
/// One run at a time per runner: a second revision requested mid-run is rejected rather than
/// queued, because the brief it would carry was computed against a document the current run is
/// still rewriting.
/// </summary>
public sealed class ClaudeRevisionRunner
{
    private readonly string _executable;
    private int _running; // 0 or 1, via Interlocked — TryStart can race with itself across threads

    /// <summary>Raised when the process has actually started.</summary>
    public event EventHandler? Started;

    /// <summary>
    /// Raised on a worker thread as the run's stream reports work: once per change in the file-edit
    /// count (each edit is a save the reader is about to see land) and once for the first turn (the
    /// run is alive). Not raised per turn — a chatty run would re-render the preview for nothing.
    /// </summary>
    public event EventHandler<ClaudeRunProgress>? Progress;

    /// <summary>Raised when the run ends, however it ends. Raised on a worker thread.</summary>
    public event EventHandler<ClaudeRunResult>? Completed;

    public ClaudeRevisionRunner(string executable) => _executable = executable;

    public bool IsRunning => Volatile.Read(ref _running) == 1;

    /// <summary>
    /// Starts a revision run, or returns <c>false</c> when one is already in flight. A run that
    /// fails to launch still reports through <see cref="Completed"/>, so callers observe every
    /// accepted run ending exactly once.
    /// </summary>
    public bool TryStart(string workingDirectory, string prompt)
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) return false;

        Task.Run(async () =>
        {
            ClaudeRunResult result;
            try
            {
                result = await RunAsync(workingDirectory, prompt).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                result = new ClaudeRunResult(false, -1, Head(ex.Message));
            }
            // Cleared before Completed is raised, so a handler that reacts by starting the next
            // run finds the runner free.
            Volatile.Write(ref _running, 0);
            Completed?.Invoke(this, result);
        });
        return true;
    }

    private async Task<ClaudeRunResult> RunAsync(string workingDirectory, string prompt)
    {
        using var process = new Process { StartInfo = BuildStartInfo(_executable, workingDirectory) };

        try { process.Start(); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new ClaudeRunResult(false, -1, Head($"could not launch {_executable}: {ex.Message}"));
        }

        Started?.Invoke(this, EventArgs.Empty);

        // Both output streams must be drained while the process runs, or a chatty run deadlocks
        // against a full pipe. Stdout is the stream-json event feed, decoded as it arrives;
        // stderr's head is the part of a launch-level failure worth showing.
        var stdout = DrainStreamAsync(process.StandardOutput);
        var stderr = process.StandardError.ReadToEndAsync();

        try
        {
            await process.StandardInput.WriteAsync(prompt).ConfigureAwait(false);
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // The process exited before reading its prompt; the exit code below tells the story.
        }

        await process.WaitForExitAsync().ConfigureAwait(false);
        var run = await stdout.ConfigureAwait(false);
        await stderr.ConfigureAwait(false);

        // The exit code says how the process ended; the result event says how the *run* ended.
        // Both must be clean: `claude -p` can exit 0 while its result line reports an error, and
        // that run must not read as a success the timeline stays silent about.
        var ok = process.ExitCode == 0 && !(run.Result?.IsError ?? false);
        var detail = ok ? "" : Head(FirstNonEmpty(
            run.Result?.IsError == true ? run.Result.Message : "", stderr.Result, run.RawHead));
        var stats = run.Result is null && run.Turns == 0 && run.Edits == 0 && run.SessionId is null
            ? null
            : new ClaudeRunStats(
                Turns: run.Result?.NumTurns > 0 ? run.Result.NumTurns : run.Turns,
                Edits: run.Edits,
                DurationMs: run.Result?.DurationMs ?? 0,
                CostUsd: run.Result?.CostUsd,
                SessionId: run.SessionId);
        return new ClaudeRunResult(ok, process.ExitCode, detail, run.Result?.Message ?? "", stats);
    }

    private sealed record StreamTally(
        ClaudeStreamEvent.Result? Result, string? SessionId, int Turns, int Edits, string RawHead);

    /// <summary>
    /// Reads the stream-json feed line by line, raising <see cref="Progress"/> as edits land.
    /// Lines that are not stream events (an older CLI printing plain text) accumulate into
    /// <c>RawHead</c> so a failure still has something honest to show.
    /// </summary>
    private async Task<StreamTally> DrainStreamAsync(StreamReader reader)
    {
        ClaudeStreamEvent.Result? result = null;
        string? sessionId = null;
        var turns = 0;
        var edits = 0;
        var raw = new StringBuilder();

        string? line;
        while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) is not null)
        {
            var evt = ClaudeStreamEvent.ParseLine(line);
            switch (evt)
            {
                case ClaudeStreamEvent.Init init:
                    sessionId = init.SessionId;
                    break;
                case ClaudeStreamEvent.AssistantTurn turn:
                    turns++;
                    var before = edits;
                    foreach (var tool in turn.Tools)
                        if (tool.IsFileEdit) edits++;
                    if (turns == 1 || edits != before)
                        Progress?.Invoke(this, new ClaudeRunProgress(turns, edits));
                    break;
                case ClaudeStreamEvent.Result r:
                    result = r;
                    break;
                case null:
                    if (raw.Length < 4096) raw.AppendLine(line);
                    break;
            }
        }

        return new StreamTally(result, sessionId, turns, edits, raw.ToString());
    }

    /// <summary>
    /// The exact invocation, separated out so it can be asserted without spawning anything (the
    /// project keeps its test surface public rather than using InternalsVisibleTo).
    /// <c>--output-format stream-json</c> makes stdout a deterministic event feed instead of prose
    /// (<c>--verbose</c> is the CLI's required companion for that format in print mode).
    /// <c>.cmd</c>/<c>.bat</c> shims (the npm install) go through <c>cmd.exe</c> because
    /// CreateProcess with redirected streams wants a real executable.
    /// </summary>
    public static ProcessStartInfo BuildStartInfo(string executable, string workingDirectory)
    {
        const string args = "-p --output-format stream-json --verbose --permission-mode acceptEdits";
        var ext = Path.GetExtension(executable);
        var viaShell = ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase)
                    || ext.Equals(".bat", StringComparison.OrdinalIgnoreCase);

        return new ProcessStartInfo
        {
            FileName = viaShell ? "cmd.exe" : executable,
            Arguments = viaShell ? $"/d /s /c \"\"{executable}\" {args}\"" : args,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
    }

    private static string FirstNonEmpty(string a, string b, string c) =>
        !string.IsNullOrWhiteSpace(a) ? a : !string.IsNullOrWhiteSpace(b) ? b : c;

    /// <summary>The first line-ish stretch of a failure message — chip-sized, not log-sized.</summary>
    private static string Head(string text)
    {
        var t = (text ?? "").Trim();
        var nl = t.IndexOf('\n');
        if (nl >= 0) t = t[..nl].TrimEnd('\r');
        return t.Length <= 200 ? t : t[..200] + "…";
    }
}
