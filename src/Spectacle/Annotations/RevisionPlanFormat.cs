namespace Spectacle.Annotations;

/// <summary>Output format for a generated revision plan.</summary>
public enum RevisionPlanFormat
{
    /// <summary>Human- and agent-readable prose instructions (the default).</summary>
    Markdown,

    /// <summary>Structured JSON for programmatic consumption by an AI agent.</summary>
    Json,
}
