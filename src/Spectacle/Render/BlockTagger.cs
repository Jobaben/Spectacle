using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Markdig.Renderers.Html;
using Markdig.Syntax;

namespace Spectacle.Render;

internal static class BlockTagger
{
    public static IReadOnlyList<TaggedBlock> TagDocument(MarkdownDocument document, string source)
    {
        var result = new List<TaggedBlock>();
        var counts = new Dictionary<(string, string), int>();

        foreach (var block in document)
        {
            if (block is ListBlock list)
            {
                TagListItems(list, source, result, counts);
                continue;
            }

            var kind = KindOf(block);
            if (kind is null) continue;

            AttachTagAttributes(block, kind, source, result, counts);
        }

        return result;
    }

    private static void TagListItems(
        ListBlock list, string source,
        List<TaggedBlock> result,
        Dictionary<(string, string), int> counts)
    {
        foreach (var child in list)
        {
            if (child is not ListItemBlock item) continue;
            AttachTagAttributes(item, "list-item", source, result, counts);
        }
    }

    private static void AttachTagAttributes(
        Block block, string kind, string source,
        List<TaggedBlock> result,
        Dictionary<(string, string), int> counts)
    {
        var raw = SliceSource(source, block);
        var normalized = NormalizeText(raw);
        var hash = Sha256Hex(normalized);
        var key = (kind, hash);
        var occurrence = counts.TryGetValue(key, out var n) ? n : 0;
        counts[key] = occurrence + 1;

        var blockId = $"b{result.Count}";
        var line = block.Line + 1;

        var attrs = block.GetAttributes();
        attrs.AddClass("md-block");
        attrs.AddPropertyIfNotExist("data-block-id", blockId);
        attrs.AddPropertyIfNotExist("data-kind", kind);
        attrs.AddPropertyIfNotExist("data-line", line.ToString());
        attrs.AddPropertyIfNotExist("data-text-hash", hash);
        attrs.AddPropertyIfNotExist("data-occurrence-index", occurrence.ToString());
        attrs.AddPropertyIfNotExist("tabindex", "0");

        result.Add(new TaggedBlock(blockId, kind, line, hash, occurrence, normalized));
    }

    private static string? KindOf(Block block) => block switch
    {
        HeadingBlock => "heading",
        ParagraphBlock => "paragraph",
        FencedCodeBlock => "code",
        CodeBlock => "code",
        QuoteBlock => "blockquote",
        Markdig.Extensions.Tables.Table => "table",
        ThematicBreakBlock => "hr",
        _ => null
    };

    private static string SliceSource(string source, Block block)
    {
        if (block.Span.Start < 0 || block.Span.End < block.Span.Start) return string.Empty;
        var start = Math.Min(block.Span.Start, source.Length);
        var endInclusive = Math.Min(block.Span.End, source.Length - 1);
        if (endInclusive < start) return string.Empty;
        return source.Substring(start, endInclusive - start + 1);
    }

    internal static string NormalizeText(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;
        var lines = raw.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var i = 0; i < lines.Length; i++)
            lines[i] = lines[i].TrimEnd();
        var joined = string.Join("\n", lines);
        return joined.TrimEnd('\n');
    }

    private static string Sha256Hex(string s)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(s));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
