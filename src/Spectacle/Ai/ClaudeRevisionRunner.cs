using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Spectacle.Ai;

/// <summary>The outcome of one background revision run.</summary>
public sealed record ClaudeRunResult(bool Succeeded, int ExitCode, string Detail);

/// <summary>
/// Runs <c>claude -p</c> as a background process to revise the open document in place.
///
/// The runner deliberately knows nothing about *what* changed: the agent saves the file, the
/// document watcher fires, and the existing pipeline re-renders, re-grades, and advances the loop
/// timeline exactly as it does for any other writer. All this class owns is the process — started
/// with the prompt on stdin (no command-line length or quoting limits), in the document's
/// directory, under <c>--permission-mode acceptEdits</c> so file edits are auto-approved while
/// anything that would need an interactive permission prompt is refused (print mode has nobody to
/// ask).
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
        // against a full pipe. Stdout is progress noise; stderr's head is the part of a failure
        // worth showing.
        var stdout = process.StandardOutput.ReadToEndAsync();
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
        await Task.WhenAll(stdout, stderr).ConfigureAwait(false);

        var ok = process.ExitCode == 0;
        var detail = ok ? "" : Head(FirstNonEmpty(stderr.Result, stdout.Result));
        return new ClaudeRunResult(ok, process.ExitCode, detail);
    }

    /// <summary>
    /// The exact invocation, separated out so it can be asserted without spawning anything (the
    /// project keeps its test surface public rather than using InternalsVisibleTo).
    /// <c>.cmd</c>/<c>.bat</c> shims (the npm install) go through <c>cmd.exe</c> because
    /// CreateProcess with redirected streams wants a real executable.
    /// </summary>
    public static ProcessStartInfo BuildStartInfo(string executable, string workingDirectory)
    {
        const string args = "-p --permission-mode acceptEdits";
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

    private static string FirstNonEmpty(string a, string b) =>
        !string.IsNullOrWhiteSpace(a) ? a : b;

    /// <summary>The first line-ish stretch of a failure message — chip-sized, not log-sized.</summary>
    private static string Head(string text)
    {
        var t = (text ?? "").Trim();
        var nl = t.IndexOf('\n');
        if (nl >= 0) t = t[..nl].TrimEnd('\r');
        return t.Length <= 200 ? t : t[..200] + "…";
    }
}
