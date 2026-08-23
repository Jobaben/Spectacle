using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using FluentAssertions;
using Spectacle.Ai;
using Xunit;

namespace Spectacle.Tests;

/// <summary>
/// The background process itself, run for real against stub CLIs. The suite runs on Windows (the
/// app is a WPF executable), so the stubs are <c>.cmd</c> files — which also exercises the
/// interpreter wrapping the npm shim needs.
/// </summary>
public class ClaudeRevisionRunnerTests : IDisposable
{
    private readonly string _root;

    public ClaudeRevisionRunnerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"spectacle-claude-run-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    private string Stub(string name, string script)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, script);
        return path;
    }

    private static ClaudeRunResult Run(ClaudeRevisionRunner runner, string workingDir, string prompt)
    {
        ClaudeRunResult? result = null;
        using var done = new ManualResetEventSlim();
        runner.Completed += (_, r) => { result = r; done.Set(); };

        runner.TryStart(workingDir, prompt).Should().BeTrue();
        done.Wait(TimeSpan.FromSeconds(30)).Should().BeTrue("the run must complete");
        return result!;
    }

    [Fact]
    public void The_prompt_reaches_the_process_on_stdin_and_a_clean_exit_reports_success()
    {
        // `more` drains stdin to a file; the runner must have delivered the whole prompt there.
        var stub = Stub("claude.cmd", "@echo off\r\nmore > \"%~dp0seen.txt\"\r\nexit /b 0\r\n");
        var runner = new ClaudeRevisionRunner(stub);

        var started = false;
        runner.Started += (_, _) => started = true;

        var result = Run(runner, _root, "Target file — revise it IN PLACE: draft.md\nApply the brief.");

        started.Should().BeTrue();
        result.Succeeded.Should().BeTrue();
        result.ExitCode.Should().Be(0);
        File.ReadAllText(Path.Combine(_root, "seen.txt"))
            .Should().Contain("revise it IN PLACE").And.Contain("Apply the brief.");
        runner.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void A_failing_run_reports_the_exit_code_and_the_first_line_of_stderr()
    {
        var stub = Stub("claude.cmd", "@echo off\r\necho credit balance too low 1>&2\r\nexit /b 3\r\n");
        var runner = new ClaudeRevisionRunner(stub);

        var result = Run(runner, _root, "prompt");

        result.Succeeded.Should().BeFalse();
        result.ExitCode.Should().Be(3);
        result.Detail.Should().Contain("credit balance too low");
    }

    [Fact]
    public void A_binary_that_cannot_launch_still_completes_as_a_failure()
    {
        // Every accepted run ends exactly once, even when the process never existed — otherwise the
        // preview's "running" chip would be stuck forever.
        var runner = new ClaudeRevisionRunner(Path.Combine(_root, "nope", "claude.exe"));

        var result = Run(runner, _root, "prompt");

        result.Succeeded.Should().BeFalse();
        result.Detail.Should().Contain("could not launch");
        runner.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void A_stream_json_run_reports_the_agents_message_and_measured_stats()
    {
        // A stub speaking the CLI's stream-json feed: one edit, one closing text, one result line.
        var stub = Stub("claude.cmd",
            "@echo off\r\n" +
            "more > nul\r\n" +
            "echo {\"type\":\"system\",\"subtype\":\"init\",\"session_id\":\"s-1\",\"model\":\"m\"}\r\n" +
            "echo {\"type\":\"assistant\",\"message\":{\"content\":[{\"type\":\"tool_use\",\"name\":\"Edit\",\"input\":{\"file_path\":\"draft.md\"}}]}}\r\n" +
            "echo {\"type\":\"assistant\",\"message\":{\"content\":[{\"type\":\"text\",\"text\":\"done\"}]}}\r\n" +
            "echo {\"type\":\"result\",\"subtype\":\"success\",\"is_error\":false,\"num_turns\":4,\"duration_ms\":1200,\"total_cost_usd\":0.05,\"result\":\"Rewrote the intro.\"}\r\n" +
            "exit /b 0\r\n");
        var runner = new ClaudeRevisionRunner(stub);

        var progress = new List<ClaudeRunProgress>();
        runner.Progress += (_, p) => progress.Add(p);

        var result = Run(runner, _root, "prompt");

        result.Succeeded.Should().BeTrue();
        result.Message.Should().Be("Rewrote the intro.");
        result.Stats.Should().NotBeNull();
        result.Stats!.Turns.Should().Be(4, "the result event's own count wins over ours");
        result.Stats.Edits.Should().Be(1);
        result.Stats.DurationMs.Should().Be(1200);
        result.Stats.CostUsd.Should().Be(0.05);
        result.Stats.SessionId.Should().Be("s-1");
        progress.Should().Contain(new ClaudeRunProgress(1, 1), "the first turn's edit must be reported live");
    }

    [Fact]
    public void A_result_event_reporting_an_error_fails_the_run_even_on_a_clean_exit()
    {
        // `claude -p` can exit 0 while its result line says the run errored; the exit code alone
        // must never make that read as a success.
        var stub = Stub("claude.cmd",
            "@echo off\r\n" +
            "more > nul\r\n" +
            "echo {\"type\":\"result\",\"subtype\":\"error_during_execution\",\"is_error\":true,\"num_turns\":2,\"duration_ms\":300,\"result\":\"API error: rate limited\"}\r\n" +
            "exit /b 0\r\n");
        var runner = new ClaudeRevisionRunner(stub);

        var result = Run(runner, _root, "prompt");

        result.Succeeded.Should().BeFalse();
        result.ExitCode.Should().Be(0);
        result.Detail.Should().Contain("rate limited");
        result.Message.Should().Contain("rate limited");
    }

    [Fact]
    public void A_second_revision_is_refused_while_one_is_in_flight()
    {
        // The brief a second run would carry was computed against a document the first run is
        // still rewriting — reject, don't queue.
        var stub = Stub("claude.cmd", "@echo off\r\nping -n 3 127.0.0.1 > nul\r\nexit /b 0\r\n");
        var runner = new ClaudeRevisionRunner(stub);

        using var done = new ManualResetEventSlim();
        runner.Completed += (_, _) => done.Set();

        runner.TryStart(_root, "first").Should().BeTrue();
        runner.IsRunning.Should().BeTrue();
        runner.TryStart(_root, "second").Should().BeFalse();

        done.Wait(TimeSpan.FromSeconds(30)).Should().BeTrue();
        runner.IsRunning.Should().BeFalse();
        runner.TryStart(_root, "third").Should().BeTrue("a finished runner accepts the next run");
    }
}
