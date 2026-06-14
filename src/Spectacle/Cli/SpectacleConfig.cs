using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Spectacle.Cli;

/// <summary>
/// A project-level Spectacle configuration, read from a <c>.spectacle.json</c> file so a
/// team can declare its spec template once instead of retyping it on every invocation.
/// Today it carries the required-section list that <c>--check-sections</c> enforces; the
/// shape is deliberately tolerant so an unknown or future key never breaks an older build.
/// </summary>
public sealed record SpectacleConfig(IReadOnlyList<string> RequiredSections)
{
    public static readonly SpectacleConfig Empty = new(new List<string>());

    /// <summary>
    /// Parses config JSON. Tolerant by design: malformed JSON, a missing
    /// <c>requiredSections</c> key, or a non-array value all yield an empty config rather
    /// than throwing — a broken config must not crash a headless check. The
    /// <c>requiredSections</c> value, when present, must be an array of strings; non-string
    /// or blank entries are dropped.
    /// </summary>
    public static SpectacleConfig Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Empty;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return Empty;
            if (!doc.RootElement.TryGetProperty("requiredSections", out var sections)) return Empty;
            if (sections.ValueKind != JsonValueKind.Array) return Empty;

            var names = sections.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString() ?? string.Empty)
                .Where(s => s.Trim().Length != 0)
                .ToList();

            return new SpectacleConfig(names);
        }
        catch (JsonException)
        {
            return Empty;
        }
    }
}
