using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Spectacle.Cli;

/// <summary>
/// A project-level Spectacle configuration, read from a <c>.spectacle.json</c> file so a
/// team can declare its review settings once instead of retyping them on every invocation.
/// It carries the required-section list that <c>--check-sections</c> and <c>--review</c>
/// enforce (<see cref="RequiredSections"/>), the gating checks the team has turned off for
/// <c>--review</c> (<see cref="DisabledChecks"/>), the front-matter keys every generated document
/// must declare (<see cref="RequiredFrontMatter"/>), and the gate's grading policy
/// (<see cref="Severity"/>, <see cref="FailOn"/>). The shape is deliberately tolerant so an
/// unknown or future key never breaks an older build.
/// </summary>
public sealed record SpectacleConfig(
    IReadOnlyList<string> RequiredSections,
    IReadOnlyList<string> DisabledChecks,
    IReadOnlyList<string>? RequiredFrontMatterKeys = null,
    IReadOnlyDictionary<string, string>? SeverityOverrides = null,
    string? FailOn = null)
{
    public static readonly SpectacleConfig Empty = new(new List<string>(), new List<string>());

    /// <summary>
    /// Front-matter keys every document under this project must declare, enforced by
    /// <c>--check-front-matter</c> and the <c>front-matter</c> gate check. Empty means the project
    /// does not use a metadata template, and the check then reports only genuine malformations.
    /// </summary>
    public IReadOnlyList<string> RequiredFrontMatter => RequiredFrontMatterKeys ?? Array.Empty<string>();

    /// <summary>
    /// Per-rule or per-check severity grades (<c>{"bare-urls": "warning"}</c>), applied by the
    /// gate. Empty means every rule keeps its catalogued default.
    /// </summary>
    public IReadOnlyDictionary<string, string> Severity =>
        SeverityOverrides ?? new Dictionary<string, string>();

    /// <summary>
    /// Parses config JSON. Tolerant by design: malformed JSON, a missing key, or a value of the
    /// wrong kind all yield empty values rather than throwing — a broken config must not crash a
    /// headless check. Array values must be arrays of strings; non-string or blank entries are
    /// dropped. <c>severity</c> must be an object of string values; other entries are dropped.
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
                StringArray(doc.RootElement, "disabledChecks"),
                StringArray(doc.RootElement, "requiredFrontMatter"),
                StringMap(doc.RootElement, "severity"),
                StringValue(doc.RootElement, "failOn"));
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

    private static IReadOnlyDictionary<string, string> StringMap(JsonElement root, string key)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty(key, out var obj) || obj.ValueKind != JsonValueKind.Object) return map;

        foreach (var property in obj.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String) continue;
            var value = property.Value.GetString();
            if (property.Name.Trim().Length == 0 || string.IsNullOrWhiteSpace(value)) continue;
            map[property.Name.Trim()] = value.Trim();
        }
        return map;
    }

    private static string? StringValue(JsonElement root, string key) =>
        root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
