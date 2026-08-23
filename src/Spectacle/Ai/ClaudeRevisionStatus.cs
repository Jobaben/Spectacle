namespace Spectacle.Ai;

/// <summary>
/// What the preview is told about the Claude CLI: whether one was found on this machine, and where
/// the current revision run stands. This is presentation state — the run itself lives in
/// <see cref="ClaudeRevisionRunner"/> — carried into the page through the gate payload so the
/// overlay can offer the hand-off only when it exists and show a run's progress across the
/// re-renders the run's own saves cause.
/// </summary>
public sealed record ClaudeRevisionStatus(bool Available, string State, string? Detail)
{
    /// <summary>No CLI on this machine: the preview offers the clipboard path only.</summary>
    public static readonly ClaudeRevisionStatus Unavailable = new(false, "idle", null);

    /// <summary>A CLI was found and no run is in flight.</summary>
    public static readonly ClaudeRevisionStatus Idle = new(true, "idle", null);

    /// <summary>A background revision run is writing to the open document right now.</summary>
    public static readonly ClaudeRevisionStatus Running = new(true, "running", null);

    /// <summary>The last run finished cleanly.</summary>
    public static readonly ClaudeRevisionStatus Done = new(true, "done", null);

    /// <summary>The last run failed; <paramref name="detail"/> is a one-line reason.</summary>
    public static ClaudeRevisionStatus Failed(string detail) => new(true, "failed", detail);
}
