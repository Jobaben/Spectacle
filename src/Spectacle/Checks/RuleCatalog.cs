using System;
using System.Collections.Generic;
using System.Linq;
using Spectacle.Render;

namespace Spectacle.Checks;

/// <summary>One rule: its stable id, the check that owns it, how much it matters, and how to fix it.</summary>
public sealed record RuleInfo(
    string Id,
    string CheckId,
    GateSeverity DefaultSeverity,
    string Description,
    string Remedy);

/// <summary>
/// The catalogue of every rule Spectacle can report, keyed by the stable <c>check/rule</c> id used
/// in SARIF, in the gate verdict, in CI annotations, and in the baseline delta — one id naming one
/// thing everywhere.
///
/// Each entry carries a <see cref="RuleInfo.Remedy"/> as well as a description, which is what
/// turns a verdict into something an AI tool can act on: the fix brief hands the authoring agent
/// the instruction for each rule it broke, so the loop closes without a human translating
/// "toc/stale-toc-entry" into "delete the entry or add the heading".
/// </summary>
public static class RuleCatalog
{
    private static readonly RuleInfo[] Rules =
    {
        new("lint/placeholder", "lint", GateSeverity.Error,
            "Leftover placeholder marker (TODO, TBD, FIXME, …) in spec prose.",
            "Replace the marker with the decision it stands in for, or delete the sentence if it is no longer needed."),
        new("lint/empty-section", "lint", GateSeverity.Error,
            "A heading with no content of its own and no subsection beneath it.",
            "Write the section's content, or remove the heading."),
        new("structure/multiple-h1", "structure", GateSeverity.Error,
            "More than one top-level (h1) heading.",
            "Keep one h1 as the document title and demote the others to h2."),
        new("structure/skipped-level", "structure", GateSeverity.Error,
            "A heading skips a level (e.g. h1 jumps to h3).",
            "Promote the heading to the next level down from its parent, or add the intermediate heading."),
        new("structure/duplicate-heading", "structure", GateSeverity.Error,
            "Duplicate heading text, which also yields ambiguous anchors.",
            "Rename one of the headings so each anchor is unique."),
        new("links", "links", GateSeverity.Error,
            "A broken internal link (unresolved anchor or empty target).",
            "Point the link at a heading that exists, or fix the anchor's slug."),
        new("tables", "tables", GateSeverity.Error,
            "A malformed GFM pipe table (row cell count differs from the header).",
            "Give the row the same number of cells as the header, padding with empty cells if needed."),
        new("fences/unclosed-fence", "fences", GateSeverity.Error,
            "A fenced code block opened but never closed.",
            "Close the fence with a matching ``` line; everything after an unclosed fence renders as code."),
        new("fences/no-language", "fences", GateSeverity.Warning,
            "A fenced code block with no language tag, so it is not syntax-highlighted.",
            "Tag the fence with its language (```json, ```bash, …), or ```text when it is not code."),
        new("paths", "paths", GateSeverity.Error,
            "A relative link/image target that does not exist on disk.",
            "Correct the path, or add the file it refers to."),
        new("duplication", "duplication", GateSeverity.Error,
            "A block (paragraph, list item, code, table) repeated verbatim elsewhere.",
            "Delete the repeat, or replace it with a link to the one authoritative copy."),
        new("alt-text", "alt-text", GateSeverity.Error,
            "An image with no alt text (empty description).",
            "Describe what the image shows in its alt text: ![what it shows](path)."),
        new("link-text/non-descriptive", "link-text", GateSeverity.Error,
            "A link whose visible text (e.g. 'click here', 'more') names no destination.",
            "Rewrite the link text to name the destination, so it makes sense read on its own."),
        new("link-text/empty", "link-text", GateSeverity.Error,
            "A link with empty or whitespace-only visible text.",
            "Give the link visible text describing where it goes."),
        new("emphasis-heading", "emphasis-heading", GateSeverity.Error,
            "An emphasized line used as a fake heading instead of a real heading.",
            "Convert the bold line into a real heading (## …) so it lands in the outline."),
        new("sections", "sections", GateSeverity.Error,
            "A required section (by the spec template) is missing from the document.",
            "Add the missing section heading and write its content."),
        new("toc/stale-toc-entry", "toc", GateSeverity.Error,
            "A table-of-contents entry pointing at a heading that does not exist.",
            "Delete the stale entry, or add the heading it points at."),
        new("toc/missing-from-toc", "toc", GateSeverity.Error,
            "A section heading (at a level the table of contents covers) with no entry.",
            "Add the heading to the table of contents in document order."),
        new("numbering/out-of-sequence", "numbering", GateSeverity.Error,
            "An ordered list whose item numbers are neither all the same nor strictly consecutive.",
            "Renumber the list consecutively from its first item (or use 1. throughout)."),
        new("bare-urls/bare-url", "bare-urls", GateSeverity.Error,
            "A bare (auto-linked) URL in prose that should be a descriptive Markdown link.",
            "Wrap the URL in a descriptive link, or put it in backticks when the literal address is the point."),
        new("heading-numbering/out-of-sequence", "heading-numbering", GateSeverity.Error,
            "Manually numbered headings whose section numbers are neither all the same nor strictly consecutive.",
            "Renumber the headings consecutively, or drop the manual numbers entirely."),
        new("link-refs/undefined-reference", "link-refs", GateSeverity.Error,
            "A reference-style link or image whose label has no matching definition (renders as broken literal text).",
            "Add the [label]: url definition, or convert the reference to an inline link."),
        new("footnotes/undefined-footnote", "footnotes", GateSeverity.Error,
            "A footnote reference whose label has no matching definition (renders as broken literal text).",
            "Add the [^label]: … definition, or remove the reference."),

        // The metadata header an AI workflow stamps onto its output.
        new("front-matter/missing-front-matter", "front-matter", GateSeverity.Error,
            "The project requires a front-matter header and the document has none.",
            "Add a --- delimited YAML header at the very top of the file with the required keys."),
        new("front-matter/unclosed-front-matter", "front-matter", GateSeverity.Error,
            "Front matter opened with --- and was never closed, so no parser reads it as metadata.",
            "Close the header with a --- line before the document's first heading."),
        new("front-matter/missing-key", "front-matter", GateSeverity.Error,
            "A key the project's metadata template requires is absent from the header.",
            "Add the key to the front matter with its real value."),
        new("front-matter/empty-value", "front-matter", GateSeverity.Error,
            "A required front-matter key is present but blank — a template that was never filled in.",
            "Fill in the key's value, or remove the key if it truly does not apply."),
        new("front-matter/duplicate-key", "front-matter", GateSeverity.Error,
            "The same front-matter key is declared twice; YAML keeps only the last value.",
            "Delete the duplicate so the value a reader sees is the value a parser returns."),
        new("front-matter/misplaced-front-matter", "front-matter", GateSeverity.Error,
            "A front-matter block appears below the top of the document, where it renders as prose.",
            "Move the keys into the header at the top of the file, or delete the stray block."),

        // The residue of generation, as distinct from a defect in the prose.
        new("ai-artifacts/unfilled-template", "ai-artifacts", GateSeverity.Error,
            "An unsubstituted template token ({{x}}, ${x}, <PLACEHOLDER>) reached the reader.",
            "Replace the token with its value, or delete the sentence if the value does not exist."),
        new("ai-artifacts/assistant-voice", "ai-artifacts", GateSeverity.Error,
            "Chat framing survived into the document: an acknowledgement, a sign-off, or a self-reference.",
            "Delete the framing sentence. The document addresses its reader, not whoever prompted it."),
        new("ai-artifacts/truncated-output", "ai-artifacts", GateSeverity.Error,
            "A truncation marker ('[…]', '(truncated)', 'rest of the file unchanged') stands where content should be.",
            "Write the omitted content, or cut the section and its marker."),
        new("ai-artifacts/placeholder-target", "ai-artifacts", GateSeverity.Error,
            "A link or image points at a stand-in target (path/to/file, your-org/your-repo, #).",
            "Point it at the real destination, or remove the link and keep the text."),

        // Diagrams. Spectacle draws them, so one that cannot be drawn is missing content.
        new("mermaid/empty-diagram", "mermaid", GateSeverity.Error,
            "A ```mermaid fence with no diagram in it, which renders as blank space.",
            "Write the diagram, or delete the empty fence."),
        new("mermaid/unknown-diagram-type", "mermaid", GateSeverity.Error,
            "A diagram opening with a keyword Mermaid does not recognize, so it cannot be drawn.",
            "Open the diagram with a supported type (flowchart, sequenceDiagram, classDiagram, …); check the spelling of the first word."),
        new("mermaid/missing-description", "mermaid", GateSeverity.Error,
            "A diagram with no accTitle or accDescr, so a screen reader announces an unnamed graphic.",
            "Add an accDescr line describing what the diagram shows (and an accTitle to name it), the way alt text describes an image."),

        // Advisory rules — surfaced everywhere, gating nowhere by default.
        new("prose/hedge", "prose", GateSeverity.Info,
            "Hedging language that signals an undecided spec ('should probably', 'may need to').",
            "Commit to the decision, or move the open question into an explicit 'Open questions' section."),
        new("prose/weasel", "prose", GateSeverity.Info,
            "A vague filler or open-ended quantifier with no concrete meaning ('etc.', 'various').",
            "Name the actual items, or state the rule that decides them."),
        new("prose/vague-directive", "prose", GateSeverity.Info,
            "An instruction that defers the decision ('as appropriate', 'to be determined').",
            "State the concrete behaviour, or record who decides it and when."),
    };

    private static readonly Dictionary<string, RuleInfo> ById =
        Rules.ToDictionary(r => r.Id, StringComparer.OrdinalIgnoreCase);

    /// <summary>Every rule, in report order.</summary>
    public static IReadOnlyList<RuleInfo> All => Rules;

    /// <summary>The rule with this id, or <c>null</c> when it is not catalogued.</summary>
    public static RuleInfo? Find(string ruleId) =>
        ById.TryGetValue(ruleId, out var info) ? info : null;

    /// <summary>
    /// The default severity for a rule id. An uncatalogued id defaults to
    /// <see cref="GateSeverity.Error"/> — a finding Spectacle emits but forgot to catalogue must
    /// still gate, so the failure mode is a missing description rather than a silently ignored
    /// defect.
    /// </summary>
    public static GateSeverity DefaultSeverityOf(string ruleId) =>
        Find(ruleId)?.DefaultSeverity ?? GateSeverity.Error;

    /// <summary>The rule's one-line description, or its id when it is not catalogued.</summary>
    public static string DescriptionOf(string ruleId) => Find(ruleId)?.Description ?? ruleId;

    /// <summary>The rule's fix instruction, or an empty string when it is not catalogued.</summary>
    public static string RemedyOf(string ruleId) => Find(ruleId)?.Remedy ?? string.Empty;
}
