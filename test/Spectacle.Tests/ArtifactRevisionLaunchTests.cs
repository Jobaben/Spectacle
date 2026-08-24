using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Spectacle.Ai;
using Xunit;

namespace Spectacle.Tests;

/// <summary>
/// The viewer's launch path, executed rather than asserted: a real process, started by the real
/// service through the real runner, with a stub standing in for the CLI so the run is
/// deterministic, free and needs no credentials.
///
/// What it proves is the half of continuity that is Spectacle's own: the working directory is the
/// resolved project root, the prompt reaches stdin carrying the handoff contract, the edit lands
/// in the open document, and the stream decodes into a result. That a real model merges a capsule
/// well is the other half, and <see cref="ArtifactContinuityTests"/> asserts it as a property of
/// the artifact instead.
/// </summary>
public class ArtifactRevisionLaunchTests : IDisposable
{
    private readonly string _temp;
    private readonly string _root;
    private readonly string _docs;
    private readonly string _artifact;
    private readonly string _stub;

    private const string Managed = """
---
title: Poller architecture
artifact_context:
  decisions:
    - decision: Consume changes through a projection reader.
      reason: Only option that can replay after an outage.
  unresolved:
    - Determine the retry interval from telemetry.
---

# Poller architecture

Retries after 10 seconds.
""";

    private const string Revised = """
---
title: Poller architecture
artifact_context:
  decisions:
    - decision: Consume changes through a projection reader.
      reason: Only option that can replay after an outage.
    - decision: Retry after 30 seconds.
      reason: Telemetry showed the original interval was too aggressive.
---

# Poller architecture

Retries after 30 seconds.
""";

    public ArtifactRevisionLaunchTests()
    {
        _temp = Path.Combine(Path.GetTempPath(), "spectacle-launch-" + Guid.NewGuid().ToString("n"));
        _root = Path.Combine(_temp, "repo");
        _docs = Path.Combine(_root, "docs");
        Directory.CreateDirectory(Path.Combine(_root, ".claude"));
        Directory.CreateDirectory(Path.Combine(_docs, ".claude")); // must NOT win over the root
        Directory.CreateDirectory(Path.Combine(_root, ".git"));
        File.WriteAllText(Path.Combine(_root, "CLAUDE.md"), "# Project instructions\n");

        _artifact = Path.Combine(_docs, "architecture.md");
        File.WriteAllText(_artifact, Managed);
        File.WriteAllText(Path.Combine(_temp, "revised.md"), Revised);

        _stub = WriteStub();
    }

    /// <summary>
    /// A stand-in for the CLI: it records the working directory it was launched in and the prompt
    /// it received on stdin, rewrites the target file, and emits genuine stream-json.
    /// </summary>
    private string WriteStub()
    {
        var ps1 = Path.Combine(_temp, "stub.ps1");
        File.WriteAllText(ps1, """
$ErrorActionPreference = 'Stop'
$out = $env:SPECTACLE_STUB_OUT
[System.IO.File]::WriteAllText((Join-Path $out 'cwd.txt'), (Get-Location).Path)
[System.IO.File]::WriteAllText((Join-Path $out 'prompt.txt'), [Console]::In.ReadToEnd())
$target = $env:SPECTACLE_STUB_TARGET
[System.IO.File]::WriteAllText($target, [System.IO.File]::ReadAllText((Join-Path $out 'revised.md')))
Write-Output '{"type":"system","subtype":"init","session_id":"stub-1","model":"stub"}'
Write-Output ('{"type":"assistant","message":{"content":[{"type":"tool_use","name":"Edit","input":{"file_path":' + (ConvertTo-Json $target) + '}}]}}')
Write-Output '{"type":"result","subtype":"success","is_error":false,"result":"merged the capsule and applied the ask","num_turns":1,"duration_ms":7,"total_cost_usd":0}'
""");

        // A .cmd shim, which also drives the cmd.exe branch of BuildStartInfo that the npm
        // install of the real CLI goes through.
        var cmd = Path.Combine(_temp, "claude.cmd");
        File.WriteAllText(cmd,
            "@echo off\r\npowershell -NoProfile -ExecutionPolicy Bypass -File \"%~dp0stub.ps1\"\r\n");
        return cmd;
    }

    private ClaudeRunResult Run()
    {
        Environment.SetEnvironmentVariable("SPECTACLE_STUB_OUT", _temp);
        Environment.SetEnvironmentVariable("SPECTACLE_STUB_TARGET", _artifact);

        var runner = new ClaudeRevisionRunner(_stub);
        var finished = new TaskCompletionSource<ClaudeRunResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        runner.Completed += (_, result) => finished.TrySetResult(result);

        var outcome = new ClaudeArtifactRevisionService(runner)
            .Revise(_artifact, _docs, "1. Retry after 30 seconds.");
        outcome.Status.Should().Be(ArtifactRevisionStatus.Started);
        outcome.WorkingDirectory.Should().Be(_root);

        finished.Task.Wait(TimeSpan.FromSeconds(90)).Should().BeTrue("the stub run should finish");
        return finished.Task.Result;
    }

    [Fact]
    public void The_process_runs_in_the_resolved_project_root_not_the_document_folder()
    {
        // docs\.claude exists and must not win: launching there would drop the root's CLAUDE.md,
        // settings, rules and hooks.
        Run();

        var cwd = File.ReadAllText(Path.Combine(_temp, "cwd.txt")).Trim();

        Path.GetFullPath(cwd).Should().BeEquivalentTo(Path.GetFullPath(_root));
    }

    [Fact]
    public void The_prompt_reaches_stdin_carrying_the_handoff_contract_and_the_brief()
    {
        Run();

        var prompt = File.ReadAllText(Path.Combine(_temp, "prompt.txt"));
        prompt.Should().Contain("revise it IN PLACE: " + _artifact);
        prompt.Should().Contain("independent session");
        prompt.Should().Contain("It currently carries: decisions, unresolved.");
        prompt.Should().Contain("no longer belongs under `unresolved`");
        prompt.TrimEnd().Should().EndWith("1. Retry after 30 seconds.");
    }

    [Fact]
    public void The_edit_lands_in_the_open_document_and_the_stream_reports_the_run()
    {
        var result = Run();

        File.ReadAllText(_artifact).Should().Contain("Retries after 30 seconds.");
        result.Succeeded.Should().BeTrue(result.Detail);
        result.Message.Should().Be("merged the capsule and applied the ask");
        result.Stats!.Edits.Should().Be(1);
        result.Stats.SessionId.Should().Be("stub-1");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("SPECTACLE_STUB_OUT", null);
        Environment.SetEnvironmentVariable("SPECTACLE_STUB_TARGET", null);
        try { Directory.Delete(_temp, recursive: true); } catch (IOException) { }
    }
}
