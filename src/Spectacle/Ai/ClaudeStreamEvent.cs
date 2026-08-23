using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Spectacle.Ai;

/// <summary>One tool invocation inside an assistant turn — the name and, for file-editing tools,
/// the file it touched.</summary>
public sealed record ClaudeToolCall(string Name, string? FilePath)
{
    /// <summary>Whether this call writes a file — the signal the reader cares about, because a
    /// write is what lands as a save and becomes a loop iteration.</summary>
    public bool IsFileEdit =>
        Name is "Edit" or "Write" or "MultiEdit" or "NotebookEdit";
}

/// <summary>
/// One line of <c>claude -p --output-format stream-json</c>, decoded. The stream is NDJSON — one
/// JSON object per line — and it is the only deterministic account of a background run the reader
/// ever gets: which turns happened, which tools wrote the document, and how the run ended (the
/// final <c>result</c> line, with the agent's own closing message). Everything the loop HUD says
/// about a run is read from these events, never guessed from timing.
/// </summary>
public abstract record ClaudeStreamEvent
{
    /// <summary>The stream's opening line: the run exists and has a session.</summary>
    public sealed record Init(string? SessionId, string? Model) : ClaudeStreamEvent;

    /// <summary>One assistant turn: its text (if any) and the tools it called.</summary>
    public sealed record AssistantTurn(string? Text, IReadOnlyList<ClaudeToolCall> Tools) : ClaudeStreamEvent;

    /// <summary>
    /// The stream's final line: how the run ended, in the CLI's own words. <see cref="Message"/>
    /// is the agent's closing text on success and the CLI's error text on failure — either way it
    /// is the run explaining itself, which is exactly what a timeline entry needs.
    /// </summary>
    public sealed record Result(
        bool IsError, string Subtype, string Message,
        int NumTurns, long DurationMs, double? CostUsd) : ClaudeStreamEvent;

    /// <summary>
    /// Decodes one stream line, or returns <c>null</c> for anything unrecognized — blank lines,
    /// event types this reader has no use for (<c>user</c> tool results, partial deltas), and
    /// malformed JSON alike. The stream comes from an external process whose format can grow new
    /// event types any release; skipping the unknown is the contract, not an error path.
    /// </summary>
    public static ClaudeStreamEvent? ParseLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        var trimmed = line.Trim();
        if (!trimmed.StartsWith('{')) return null;

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!root.TryGetProperty("type", out var typeEl) || typeEl.ValueKind != JsonValueKind.String)
                return null;

            return typeEl.GetString() switch
            {
                "system" => ParseSystem(root),
                "assistant" => ParseAssistant(root),
                "result" => ParseResult(root),
                _ => null,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ClaudeStreamEvent? ParseSystem(JsonElement root)
    {
        // Only the opening handshake matters; other system subtypes are progress noise.
        if (Str(root, "subtype") != "init") return null;
        return new Init(Str(root, "session_id"), Str(root, "model"));
    }

    private static ClaudeStreamEvent? ParseAssistant(JsonElement root)
    {
        if (!root.TryGetProperty("message", out var message) ||
            !message.TryGetProperty("content", out var content) ||
            content.ValueKind != JsonValueKind.Array)
            return null;

        string? text = null;
        var tools = new List<ClaudeToolCall>();
        foreach (var item in content.EnumerateArray())
        {
            switch (Str(item, "type"))
            {
                case "text":
                    var t = Str(item, "text");
                    if (!string.IsNullOrWhiteSpace(t)) text = t;
                    break;
                case "tool_use":
                    var name = Str(item, "name");
                    if (name is null) break;
                    string? filePath = null;
                    if (item.TryGetProperty("input", out var input) &&
                        input.ValueKind == JsonValueKind.Object)
                        filePath = Str(input, "file_path") ?? Str(input, "notebook_path");
                    tools.Add(new ClaudeToolCall(name, filePath));
                    break;
            }
        }
        return new AssistantTurn(text, tools);
    }

    private static ClaudeStreamEvent ParseResult(JsonElement root)
    {
        var subtype = Str(root, "subtype") ?? "unknown";
        var isError = root.TryGetProperty("is_error", out var err) && err.ValueKind == JsonValueKind.True;
        // The agent's closing words when present; the subtype ("error_max_turns") is still a
        // human-readable account when they are not.
        var message = Str(root, "result");
        if (string.IsNullOrWhiteSpace(message)) message = subtype.Replace('_', ' ');

        return new Result(
            IsError: isError,
            Subtype: subtype,
            Message: message!,
            NumTurns: Int(root, "num_turns"),
            DurationMs: Long(root, "duration_ms"),
            CostUsd: Dbl(root, "total_cost_usd"));
    }

    private static string? Str(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int Int(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : 0;

    private static long Long(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var i) ? i : 0L;

    private static double? Dbl(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
}
