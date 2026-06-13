using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Spectacle.Annotations;

/// <summary>
/// Formats a <see cref="ReviewSummary"/> for headless output — a readable text
/// block (the default, à la <c>--stats</c>) or structured JSON for an agent.
/// </summary>
public static class ReviewSummaryExporter
{
    private const string Iso8601Utc = "yyyy-MM-ddTHH:mm:ssZ";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string Build(
        ReviewSummary summary, string sourcePath, DateTime generatedAt, RevisionPlanFormat format) =>
        format == RevisionPlanFormat.Json
            ? Json(summary, sourcePath, generatedAt)
            : Text(summary, sourcePath, generatedAt);

    private static string Text(ReviewSummary s, string sourcePath, DateTime generatedAt)
    {
        var sb = new StringBuilder();
        sb.Append(Path.GetFileName(sourcePath)).AppendLine(" — review summary");
        sb.Append("  Comments:  ").Append(s.Total).AppendLine();
        sb.Append("  Open:      ").Append(s.Open).AppendLine();
        sb.Append("  Resolved:  ").Append(s.Resolved).AppendLine();
        sb.Append("  Anchored:  ").Append(s.Matched).AppendLine();
        sb.Append("  Orphaned:  ").Append(s.Orphaned)
          .AppendLine("   (block changed or removed since the comment was made)");
        sb.Append("  Generated: ").Append(generatedAt.ToUniversalTime().ToString(Iso8601Utc));
        return sb.ToString();
    }

    private static string Json(ReviewSummary s, string sourcePath, DateTime generatedAt)
    {
        var payload = new
        {
            source = sourcePath,
            generatedAt = generatedAt.ToUniversalTime().ToString(Iso8601Utc),
            total = s.Total,
            open = s.Open,
            resolved = s.Resolved,
            matched = s.Matched,
            orphaned = s.Orphaned,
        };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }
}
