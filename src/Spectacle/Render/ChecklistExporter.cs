using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Spectacle.Render;

/// <summary>
/// Formats <see cref="ChecklistAnalyzer"/> items for headless output — a text
/// completion report (default) listing open items, or structured JSON.
/// </summary>
public static class ChecklistExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Build(IReadOnlyList<ChecklistItem> items, string sourcePath, bool json) =>
        json ? Json(items, sourcePath) : Text(items, sourcePath);

    private static string Text(IReadOnlyList<ChecklistItem> items, string sourcePath)
    {
        var done = items.Count(i => i.Checked);
        var sb = new StringBuilder();
        sb.Append(Path.GetFileName(sourcePath)).Append(" — checklist: ")
          .Append(done).Append('/').Append(items.Count).AppendLine(" complete");
        foreach (var item in items.Where(i => !i.Checked))
            sb.Append("  [ ] line ").Append(item.Line).Append("  ").AppendLine(item.Text);
        return sb.ToString().TrimEnd('\n');
    }

    private static string Json(IReadOnlyList<ChecklistItem> items, string sourcePath)
    {
        var payload = new
        {
            source = sourcePath,
            total = items.Count,
            done = items.Count(i => i.Checked),
            open = items.Count(i => !i.Checked),
            items = items.Select(i => new { @checked = i.Checked, text = i.Text, line = i.Line }),
        };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }
}
