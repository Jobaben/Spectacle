using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using Spectacle.Ai;
using Xunit;

namespace Spectacle.Tests;

/// <summary>
/// Finding the Claude CLI, and the prompt that makes it revise the open document *in place*.
///
/// The prompt's contract exists because of an observed failure: handed the fix brief alone, the
/// agent wrote the revised text to a brand-new Markdown file next to the original. Against a live
/// reader that is a silent no-op — the watcher never fires, the loop timeline never advances — so
/// the wrapper's in-place instructions are load-bearing and asserted here word by word.
/// </summary>
public class ClaudeCliTests
{
    // ---------- ClaudeCliLocator ----------

    private static Func<string, bool> Exists(params string[] paths)
    {
        var set = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        return set.Contains;
    }

    private static string Sep => Path.PathSeparator.ToString();

    [Fact]
    public void The_locator_finds_the_cli_on_PATH()
    {
        var tools = Path.Combine(Path.GetTempPath(), "tools");
        var path = string.Join(Sep, Path.Combine(Path.GetTempPath(), "empty"), tools);

        ClaudeCliLocator.Detect(null, path, Exists(Path.Combine(tools, "claude.exe")))
            .Should().Be(Path.Combine(tools, "claude.exe"));
    }

    [Fact]
    public void The_locator_prefers_the_native_binary_over_the_npm_shim_in_the_same_directory()
    {
        var tools = Path.Combine(Path.GetTempPath(), "tools");

        ClaudeCliLocator.Detect(null, tools,
                Exists(Path.Combine(tools, "claude.exe"), Path.Combine(tools, "claude.cmd")))
            .Should().Be(Path.Combine(tools, "claude.exe"));
    }

    [Fact]
    public void The_locator_finds_the_npm_shim_and_the_bare_shim_too()
    {
        var tools = Path.Combine(Path.GetTempPath(), "tools");

        ClaudeCliLocator.Detect(null, tools, Exists(Path.Combine(tools, "claude.cmd")))
            .Should().Be(Path.Combine(tools, "claude.cmd"));
        ClaudeCliLocator.Detect(null, tools, Exists(Path.Combine(tools, "claude")))
            .Should().Be(Path.Combine(tools, "claude"));
    }

    [Fact]
    public void No_install_means_null_not_a_guess()
    {
        ClaudeCliLocator.Detect(null, string.Join(Sep, "C:\\a", "C:\\b"), _ => false).Should().BeNull();
        ClaudeCliLocator.Detect(null, null, _ => true).Should().BeNull();
        ClaudeCliLocator.Detect(null, "", _ => true).Should().BeNull();
    }

    [Fact]
    public void Quoted_padded_and_empty_PATH_entries_are_survived()
    {
        var tools = Path.Combine(Path.GetTempPath(), "tools");
        var path = string.Join(Sep, "", $"  \"{tools}\"  ", "   ");

        ClaudeCliLocator.Detect(null, path, Exists(Path.Combine(tools, "claude.exe")))
            .Should().Be(Path.Combine(tools, "claude.exe"));
    }

    [Fact]
    public void The_override_variable_pins_a_binary_and_wins_over_PATH()
    {
        var pinned = Path.Combine(Path.GetTempPath(), "portable", "claude.exe");
        var onPath = Path.Combine(Path.GetTempPath(), "tools");

        ClaudeCliLocator.Detect($"\"{pinned}\"", onPath,
                Exists(pinned, Path.Combine(onPath, "claude.exe")))
            .Should().Be(pinned);
    }

    [Fact]
    public void A_pinned_binary_that_does_not_exist_means_not_installed_not_fall_back()
    {
        // An explicit pin that is wrong should be visible as "no Claude", not silently replaced by
        // whatever PATH happens to hold.
        var onPath = Path.Combine(Path.GetTempPath(), "tools");

        ClaudeCliLocator.Detect("C:\\nowhere\\claude.exe", onPath,
                Exists(Path.Combine(onPath, "claude.exe")))
            .Should().BeNull();
    }

    // ---------- ClaudeRevisionPrompt ----------

    private const string Brief = "# Revision brief — draft.md\n\n## Required fixes (1)\n";
    private static readonly string DocPath = Path.Combine(Path.GetTempPath(), "specs", "draft.md");

    [Fact]
    public void The_prompt_names_the_exact_file_and_demands_the_edit_in_place()
    {
        var prompt = ClaudeRevisionPrompt.Build(DocPath, Brief);

        prompt.Should().Contain($"Target file — revise it IN PLACE: {DocPath}");
        prompt.Should().Contain("save it to the exact path above");
    }

    [Fact]
    public void The_prompt_forbids_the_new_file_failure_mode_by_name()
    {
        var prompt = ClaudeRevisionPrompt.Build(DocPath, Brief);

        prompt.Should().Contain("Create no other file");
        prompt.Should().Contain("`draft.md.revised.md`");
        prompt.Should().Contain("Do not rename, move, or delete the target file");
    }

    [Fact]
    public void The_prompt_keeps_the_deliverable_in_the_file_not_in_chat()
    {
        var prompt = ClaudeRevisionPrompt.Build(DocPath, Brief);

        prompt.Should().Contain("The saved file is the entire deliverable");
        prompt.Should().Contain("change nothing else");
    }

    [Fact]
    public void The_prompt_carries_the_brief_verbatim_at_the_end()
    {
        // StringBuilder.AppendLine writes the platform newline; the brief's own newlines pass
        // through untouched.
        var nl = Environment.NewLine;
        ClaudeRevisionPrompt.Build(DocPath, Brief)
            .Should().EndWith("The revision brief:" + nl + nl + Brief.TrimEnd('\n'));
    }

    [Fact]
    public void The_prompt_encourages_incremental_saves_so_the_loop_stays_legible()
    {
        ClaudeRevisionPrompt.Build(DocPath, Brief)
            .Should().Contain("records each save as an iteration");
    }

    // ---------- ClaudeRevisionRunner.BuildStartInfo ----------

    [Fact]
    public void A_native_binary_is_launched_directly_in_print_mode_with_acceptEdits()
    {
        var psi = ClaudeRevisionRunner.BuildStartInfo("C:\\tools\\claude.exe", "C:\\specs");

        psi.FileName.Should().Be("C:\\tools\\claude.exe");
        psi.Arguments.Should().Be("-p --permission-mode acceptEdits");
        psi.WorkingDirectory.Should().Be("C:\\specs");
    }

    [Fact]
    public void The_npm_cmd_shim_is_launched_through_the_command_interpreter()
    {
        var psi = ClaudeRevisionRunner.BuildStartInfo("C:\\npm\\claude.CMD", "C:\\specs");

        psi.FileName.Should().Be("cmd.exe");
        psi.Arguments.Should().Contain("\"C:\\npm\\claude.CMD\" -p --permission-mode acceptEdits");
    }

    [Fact]
    public void The_process_is_headless_with_every_stream_redirected()
    {
        // The prompt travels on stdin (no command-line length or quoting limits), both output
        // streams must be drained to avoid pipe deadlock, and a console window flashing up under
        // the reader would be a bug.
        var psi = ClaudeRevisionRunner.BuildStartInfo("C:\\tools\\claude.exe", "C:\\specs");

        psi.UseShellExecute.Should().BeFalse();
        psi.CreateNoWindow.Should().BeTrue();
        psi.RedirectStandardInput.Should().BeTrue();
        psi.RedirectStandardOutput.Should().BeTrue();
        psi.RedirectStandardError.Should().BeTrue();
    }
}
