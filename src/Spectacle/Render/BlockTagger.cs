using System;
using System.Collections.Generic;
using System.Linq;
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
            var kind = KindOf(block);
            if (kind is null) continue;

            var raw = SliceSource(source, block);
            var normalized = NormalizeText(raw);
            var hash = Sha256Hex(normalized);
            var key = (kind, hash);
            var occurrence = counts.TryGetValue(key, out var n) ? n : 0;
            counts[key] = occurrence + 1;

            var blockId = $"b{result.Count}";
            var line = block.Line + 1;
            var leading = LeadingText(normalized);

            var attrs = block.GetAttributes();
            attrs.AddClass("md-block");
            attrs.AddPropertyIfNotExist("data-block-id", blockId);
            attrs.AddPropertyIfNotExist("data-kind", kind);
            attrs.AddPropertyIfNotExist("data-line", line.ToString());
            attrs.AddPropertyIfNotExist("data-text-hash", hash);
            attrs.AddPropertyIfNotExist("data-occurrence-index", occurrence.ToString());
            attrs.AddPropertyIfNotExist("tabindex", "0");

            // For list blocks, descend and tag each list-item; the list itself stays untagged.
            if (block is ListBlock list)
            {
                attrs.Classes?.Remove("md-block");
                attrs.Properties?.RemoveAll(p =>
                    p.Key is "data-block-id" or "data-kind" or "data-line"
                            or "data-text-hash" or "data-occurrence-index" or "tabindex");
                TagListItems(list, source, result, counts);
                continue;
            }

            result.Add(new TaggedBlock(blockId, kind, line, hash, occurrence, normalized));
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

            var raw = SliceSource(source, item);
            var normalized = NormalizeText(raw);
            var hash = Sha256Hex(normalized);
            var key = ("list-item", hash);
            var occurrence = counts.TryGetValue(key, out var n) ? n : 0;
            counts[key] = occurrence + 1;

            var blockId = $"b{result.Count}";
            var line = item.Line + 1;

            var attrs = item.GetAttributes();
            attrs.AddClass("md-block");
            attrs.AddPropertyIfNotExist("data-block-id", blockId);
            attrs.AddPropertyIfNotExist("data-kind", "list-item");
            attrs.AddPropertyIfNotExist("data-line", line.ToString());
            attrs.AddPropertyIfNotExist("data-text-hash", hash);
            attrs.AddPropertyIfNotExist("data-occurrence-index", occurrence.ToString());
            attrs.AddPropertyIfNotExist("tabindex", "0");

            result.Add(new TaggedBlock(blockId, "list-item", line, hash, occurrence, normalized));
        }
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
        HtmlBlock => "html",
        ListBlock => "list",
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

    private static string LeadingText(string normalized)
    {
        var firstLine = normalized.Split('\n')[0];
        return firstLine.Length <= 80 ? firstLine : firstLine.Substring(0, 80);
    }

    private static string Sha256Hex(string s)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(s));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
