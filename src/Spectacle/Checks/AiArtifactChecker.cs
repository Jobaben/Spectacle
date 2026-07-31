using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Spectacle.Checks;

/// <summary>A generation artifact left in the document, with its rule, 1-based line and the matched text.</summary>
public sealed record AiArtifact(string Rule, int Line, string Excerpt, string Message);

/// <summary>
/// Reports the ways a generated Markdown document fails to be a *document* — the residue of the
/// process that produced it rather than a defect in the prose.
///
/// Every other check in Spectacle would pass a file that opens with "Certainly! Here's the updated
/// specification:" and ends with "…rest of the document unchanged". Those are the failures that
/// actually matter when a model, not a person, writes the file, and they are exactly what a human
/// reviewer catches in two seconds and a Markdown linter never catches at all. This check closes
/// that gap, which is what lets an unattended workflow gate its own output.
///
/// Four rules:
/// <list type="bullet">
///   <item><c>unfilled-template</c> — a substitution token that was never substituted:
///     <c>{{title}}</c>, <c>${VERSION}</c>, <c>&lt;PROJECT_NAME&gt;</c>, <c>[INSERT SUMMARY]</c>,
///     <c>%SCOPE%</c>, or a fill-in-the-blank rule of underscores. The template reached the reader
///     instead of the value.</item>
///   <item><c>assistant-voice</c> — chat framing that survived into the artifact: an
///     acknowledgement opener ("Certainly!"), a sign-off ("Let me know if you'd like…"), a
///     self-reference ("As an AI language model", "I've updated the section"). The document is
///     addressed to whoever prompted it rather than to whoever will read it.</item>
///   <item><c>truncated-output</c> — a marker where content should be: "[…]", "(truncated)",
///     "rest of the file unchanged", "content continues". The generation stopped and said so,
///     and the file was published anyway.</item>
///   <item><c>placeholder-target</c> — a link or image pointing at a stand-in rather than a real
///     destination: <c>path/to/file</c>, <c>your-org/your-repo</c>, an unsubstituted
///     <c>{{url}}</c>, or a bare <c>#</c>. These are what a model writes when it needs a URL and
///     has none, so they read as citations while referring to nothing. The reserved
///     <c>example.com</c> domain is deliberately <em>not</em> flagged — it exists so that
///     documentation can show a URL that points nowhere on purpose.</item>
/// </list>
///
/// Scanning skips fenced code and inline code spans, so a template engine's own syntax shown as
/// an example — <c>`{{name}}`</c> in a docs page about templating — is never flagged. At most one
/// finding per rule per line keeps a line of dense residue from burying the rest of the verdict.
/// </summary>
public static class AiArtifactChecker
{
    public const string UnfilledTemplateRule = "unfilled-template";
    public const string AssistantVoiceRule = "assistant-voice";
    public const string TruncatedOutputRule = "truncated-output";
    public const string PlaceholderTargetRule = "placeholder-target";

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseYamlFrontMatter()
        .UsePreciseSourceLocation()
        .Build();

    // Unsubstituted template tokens. Deliberately shaped so ordinary prose can't match: a
    // mustache/shell token needs its braces, an angle token must be SCREAMING_CASE (so `<div>`
    // and `<https://…>` are safe), and a bracket token must open with an imperative
    // placeholder word.
    private static readonly Regex[] TemplateTokens =
    {
        new(@"\{\{[^{}\n]*\}\}", RegexOptions.Compiled),
        new(@"\$\{[^{}\n]*\}", RegexOptions.Compiled),
        new(@"<[A-Z][A-Z0-9_]{2,}>", RegexOptions.Compiled),
        new(@"%[A-Z][A-Z0-9_]{2,}%", RegexOptions.Compiled),
        new(@"\[(?:INSERT|PLACEHOLDER|YOUR|ADD|DESCRIBE|EXPLAIN|FILL)\b[^\]\n]*\]",
            RegexOptions.Compiled | RegexOptions.IgnoreCase),
        // A rule of four or more underscores used as a blank to be filled in. Bounded by
        // non-word characters so a snake_case identifier never matches.
        new(@"(?<![\w])_{4,}(?![\w])", RegexOptions.Compiled),
    };

    // Chat framing. Openers are anchored to the start of the line — mid-sentence "of course" is
    // ordinary English — while self-reference and sign-off phrases are specific enough to match
    // anywhere.
    private static readonly Regex[] AssistantVoice =
    {
        new(@"^\s*(?:Sure|Certainly|Of course|Absolutely|Great question|No problem)\b[!,.]",
            RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"\bas an AI(?:\s+(?:language\s+)?model)?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"\bI(?:'m| am) (?:an AI|a language model)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"\bI hope (?:this|that) helps\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"\blet me know if you\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"\bfeel free to (?:ask|reach out|let me know)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"\bif you(?:'d| would) like me to\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"\bhere(?:'s| is) (?:the|a|an|your) (?:complete|completed|updated|revised|full|final|rewritten|corrected)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"\bI(?:'ve| have) (?:created|generated|updated|written|added|revised|drafted) (?:the|a|an|your|this)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"\bas (?:requested|per your request)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"\bmy (?:training data|knowledge cutoff)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
    };

    // Markers standing in for content the generation did not produce.
    private static readonly Regex[] Truncation =
    {
        new(@"\[\s*(?:\.\.\.|…)\s*\]", RegexOptions.Compiled),
        new(@"\(\s*(?:\.\.\.|…)\s*\)", RegexOptions.Compiled),
        new(@"[\[(]\s*truncated[^\])\n]*[\])]", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"\btruncated for brevity\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"\b(?:omitted|abbreviated|shortened) for brevity\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"\b(?:the )?(?:rest|remainder) of (?:the )?(?:file|document|content|section|list|spec)s?\s+(?:is|are|remains?|stays?)?\s*(?:unchanged|the same|omitted|as before)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"\b(?:content|output|list|section|document) continues\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"\b(?:and )?so on(?: and so forth)?\s*(?:\.\.\.|…)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"^\s*(?:\.\.\.|…)\s*$", RegexOptions.Compiled),
    };

    // Stand-in destinations, restricted to targets that can only ever be placeholders. Notably
    // absent: the IANA-reserved `example.com` — that domain exists precisely so documentation can
    // show a URL without pointing anywhere, so flagging it would punish correct writing.
    private static readonly Regex PlaceholderTarget = new(
        @"^\.{0,2}/?path/to/|\byour[-_](?:org|repo|company|project|domain|name|team|username)\b|^(?:#|url|link|todo|tbd|insert[-_ ]?url)$|\{\{|\$\{|<[A-Z][A-Z0-9_]{2,}>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Reports every generation artifact in <paramref name="markdown"/>, ordered by line.
    /// </summary>
    public static IReadOnlyList<AiArtifact> Check(string? markdown)
    {
        var findings = new List<AiArtifact>();
        // (rule, line) — one finding per rule per line, so a dense line reports once.
        var seen = new HashSet<(string, int)>();

        void Add(string rule, int line, string excerpt, string message)
        {
            if (seen.Add((rule, line)))
                findings.Add(new AiArtifact(rule, line, Excerpt(excerpt), message));
        }

        foreach (var (line, text) in MarkdownTextScanner.ProseLines(markdown))
        {
            if (text.Trim().Length == 0) continue;

            // A thematic break written with underscores (`____`) is punctuation, not a
            // fill-in-the-blank, so the token scan skips a line made only of rule characters.
            if (!IsThematicBreak(text) && FirstMatch(TemplateTokens, text) is { } token)
                Add(UnfilledTemplateRule, line, token,
                    $"unsubstituted template token '{Excerpt(token)}' — the template reached the reader instead of the value");

            if (FirstMatch(AssistantVoice, text) is { } voice)
                Add(AssistantVoiceRule, line, voice,
                    $"assistant framing '{Excerpt(voice)}' — the text addresses whoever prompted it, not whoever reads it");

            if (FirstMatch(Truncation, text) is { } cut)
                Add(TruncatedOutputRule, line, cut,
                    $"truncation marker '{Excerpt(cut)}' — content is missing where the marker stands");
        }

        foreach (var (line, target, kind) in PlaceholderTargets(markdown))
            Add(PlaceholderTargetRule, line, target, $"{kind} points at the placeholder target '{Excerpt(target)}'");

        return findings.OrderBy(f => f.Line).ThenBy(f => f.Rule, StringComparer.Ordinal).ToList();
    }

    private static IEnumerable<(int Line, string Target, string Kind)> PlaceholderTargets(string? markdown)
    {
        var document = Markdown.Parse(markdown ?? string.Empty, Pipeline);

        foreach (var link in document.Descendants<LinkInline>())
        {
            var url = (link.Url ?? string.Empty).Trim();
            // An empty target is a broken link, which LinkChecker already owns; a bare '#' is a
            // deliberate nowhere-link and belongs here.
            if (url.Length == 0) continue;
            if (!PlaceholderTarget.IsMatch(url)) continue;

            yield return (link.Line + 1, url, link.IsImage ? "image" : "link");
        }
    }

    private static bool IsThematicBreak(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length >= 3 && trimmed.All(c => c is '_' or '-' or '*' or ' ');
    }

    private static string? FirstMatch(Regex[] patterns, string text)
    {
        foreach (var pattern in patterns)
        {
            var match = pattern.Match(text);
            if (match.Success) return match.Value.Trim();
        }
        return null;
    }

    // Findings are read in a terminal and in CI annotations, so an excerpt stays short and
    // single-line.
    private static string Excerpt(string text)
    {
        var flat = text.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return flat.Length <= 60 ? flat : flat[..57] + "…";
    }
}
