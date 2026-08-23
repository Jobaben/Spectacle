using System;

namespace Spectacle.Ai;

/// <summary>
/// One finished background revision run, as the loop timeline remembers it. Iterations say what
/// each *save* did; this says what each *run* did — including the runs the timeline used to be
/// silent about: a run that failed, or ended without saving anything. <see cref="AfterIteration"/>
/// is the loop iteration current when the run started and <see cref="Iterations"/> how many the
/// run's saves produced, so the panel can attribute exactly which bars were the agent's work
/// (iterations <c>AfterIteration + 1</c> through <c>AfterIteration + Iterations</c>).
/// <see cref="Message"/> is the agent's own closing text — the run explaining itself to the
/// reader, taken verbatim from the CLI's stream-json result event.
/// </summary>
public sealed record ClaudeRunRecord(
    int Number,
    DateTime StartedAt,
    DateTime EndedAt,
    int AfterIteration,
    int Iterations,
    bool Succeeded,
    string Message,
    int Turns,
    int Edits,
    long DurationMs,
    double? CostUsd);
