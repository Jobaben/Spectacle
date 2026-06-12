using System;
using System.Text;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Spectacle.Render;

/// <summary>
/// Readership-oriented counts for a Markdown document: prose word count and the
/// estimated reading time derived from it, plus structural tallies (headings,
/// code blocks, links, images) pulled from the parsed tree. Word count is taken
/// from rendered prose only — Markdown punctuation (<c>#</c>, <c>*</c>, fences)
/// and the contents of code blocks are excluded so the number matches what a
/// reader actually reads.
/// </summary>
public sealed record DocumentStats(
    int Words,
    int Characters,
    int Lines,
    int Headings,
    int CodeBlocks,
    int Links,
    int Images,
    int ReadingTimeMinutes)
{
    /// <summary>Average adult silent-reading speed, in words per minute.</summary>
    public const int WordsPerMinute = 200;

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    public static DocumentStats Compute(string? markdown)
    {
        var source = markdown ?? string.Empty;
        var document = Markdown.Parse(source, Pipeline);

        var headings = 0;
        var codeBlocks = 0;
        var links = 0;
        var images = 0;
        var prose = new StringBuilder();

        foreach (var node in document.Descendants())
        {
            switch (node)
            {
                case HeadingBlock:
                    headings++;
                    break;
                case CodeBlock:
                    // FencedCodeBlock derives from CodeBlock, so this covers both.
                    codeBlocks++;
                    break;
                case LinkInline link when link.IsImage:
                    images++;
                    break;
                case LinkInline:
                    links++;
                    break;
                // Prose contributors. Code-block bodies are deliberately omitted —
                // their text never appears as inline literals, so they add to the
                // code-block tally but not to the word count.
                case LiteralInline literal:
                    prose.Append(literal.Content.ToString()).Append(' ');
                    break;
                case CodeInline code:
                    prose.Append(code.Content).Append(' ');
                    break;
            }
        }

        var words = CountWords(prose.ToString());
        var readingTime = words == 0 ? 0 : Math.Max(1, (int)Math.Ceiling(words / (double)WordsPerMinute));

        return new DocumentStats(
            Words: words,
            Characters: source.Length,
            Lines: source.Length == 0 ? 0 : source.Split('\n').Length,
            Headings: headings,
            CodeBlocks: codeBlocks,
            Links: links,
            Images: images,
            ReadingTimeMinutes: readingTime);
    }

    private static int CountWords(string text)
    {
        var count = 0;
        var inWord = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                inWord = false;
            }
            else if (!inWord)
            {
                inWord = true;
                count++;
            }
        }
        return count;
    }
}
