using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Spectacle.Cli;

/// <summary>
/// A project-level Spectacle configuration, read from a <c>.spectacle.json</c> file so a
/// team can declare its review settings once instead of retyping them on every invocation.
/// It carries the required-section list that <c>--check-sections</c> and <c>--review</c>
/// enforce (<see cref="RequiredSections"/>) and the gating checks the team has turned off for
/// <c>--review</c> (<see cref="DisabledChecks"/>). The shape is deliberately tolerant so an
/// unknown or future key never breaks an older build.
/// </summary>
public sealed record SpectacleConfig(
    IReadOnlyList<string> RequiredSections,
    IReadOnlyList<string> DisabledChecks)
{
    public static readonly SpectacleConfig Empty = new(new List<string>(), new List<string>());

    /// <summary>
    /// Parses config JSON. Tolerant by design: malformed JSON, a missing key, or a non-array
    /// value all yield empty values rather than throwing — a broken config must not crash a
    /// headless check. The <c>requiredSections</c> and <c>disabledChecks</c> values, when
    /// present, must be arrays of strings; non-string or blank entries are dropped.
    /// </summary>
    public static SpectacleConfig Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Empty;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return Empty;

            return new SpectacleConfig(
                StringArray(doc.RootElement, "requiredSections"),
                StringArray(doc.RootElement, "disabledChecks"));
        }
        catch (JsonException)
        {
            return Empty;
        }
    }

    private static IReadOnlyList<string> StringArray(JsonElement root, string key)
    {
        if (!root.TryGetProperty(key, out var array) || array.ValueKind != JsonValueKind.Array)
            return new List<string>();

        return array.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString() ?? string.Empty)
            .Where(s => s.Trim().Length != 0)
            .ToList();
    }
}
