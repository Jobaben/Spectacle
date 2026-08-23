using FluentAssertions;
using Spectacle.Ai;
using Xunit;

namespace Spectacle.Tests;

/// <summary>
/// Decoding the CLI's stream-json feed, line by line. This is the reader's only deterministic
/// account of a background run — the parser must read exactly what the stream says and skip,
/// never choke on, everything else: the stream belongs to an external process whose event
/// vocabulary grows with every release.
/// </summary>
public class ClaudeStreamTests
{
    [Fact]
    public void The_init_line_carries_the_session()
    {
        var evt = ClaudeStreamEvent.ParseLine(
            """{"type":"system","subtype":"init","session_id":"s-123","model":"claude-x","tools":["Edit"]}""");

        evt.Should().BeOfType<ClaudeStreamEvent.Init>()
            .Which.Should().Be(new ClaudeStreamEvent.Init("s-123", "claude-x"));
    }

    [Fact]
    public void An_assistant_turn_yields_its_text_and_its_tool_calls()
    {
        var evt = ClaudeStreamEvent.ParseLine(
            """{"type":"assistant","message":{"content":[{"type":"text","text":"Fixing the intro."},{"type":"tool_use","name":"Edit","input":{"file_path":"C:\\specs\\draft.md","old_string":"a"}},{"type":"tool_use","name":"Read","input":{"file_path":"C:\\specs\\draft.md"}}]}}""");

        var turn = evt.Should().BeOfType<ClaudeStreamEvent.AssistantTurn>().Which;
        turn.Text.Should().Be("Fixing the intro.");
        turn.Tools.Should().HaveCount(2);
        turn.Tools[0].Should().Be(new ClaudeToolCall("Edit", "C:\\specs\\draft.md"));
        turn.Tools[0].IsFileEdit.Should().BeTrue();
        turn.Tools[1].IsFileEdit.Should().BeFalse("Read does not write the document");
    }

    [Theory]
    [InlineData("Edit", true)]
    [InlineData("Write", true)]
    [InlineData("MultiEdit", true)]
    [InlineData("NotebookEdit", true)]
    [InlineData("Read", false)]
    [InlineData("Bash", false)]
    [InlineData("Grep", false)]
    public void Only_file_writing_tools_count_as_edits(string tool, bool isEdit) =>
        new ClaudeToolCall(tool, null).IsFileEdit.Should().Be(isEdit);

    [Fact]
    public void The_result_line_is_the_runs_own_account_of_how_it_ended()
    {
        var evt = ClaudeStreamEvent.ParseLine(
            """{"type":"result","subtype":"success","is_error":false,"duration_ms":41250,"num_turns":6,"result":"Rewrote the Ten minutes section as asked.","session_id":"s-123","total_cost_usd":0.0712}""");

        var result = evt.Should().BeOfType<ClaudeStreamEvent.Result>().Which;
        result.IsError.Should().BeFalse();
        result.Message.Should().Be("Rewrote the Ten minutes section as asked.");
        result.NumTurns.Should().Be(6);
        result.DurationMs.Should().Be(41250);
        result.CostUsd.Should().Be(0.0712);
    }

    [Fact]
    public void An_error_result_without_text_still_says_something_human()
    {
        var evt = ClaudeStreamEvent.ParseLine(
            """{"type":"result","subtype":"error_max_turns","is_error":true,"duration_ms":90000,"num_turns":50}""");

        var result = evt.Should().BeOfType<ClaudeStreamEvent.Result>().Which;
        result.IsError.Should().BeTrue();
        result.Message.Should().Be("error max turns", "the subtype is the only account there is");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("plain prose from an old CLI")]
    [InlineData("{not json")]
    [InlineData("""{"no":"type"}""")]
    [InlineData("""{"type":"user","message":{}}""")]
    [InlineData("""{"type":"system","subtype":"status"}""")]
    [InlineData("[1,2,3]")]
    public void Everything_else_is_skipped_not_thrown(string line) =>
        ClaudeStreamEvent.ParseLine(line).Should().BeNull();
}
