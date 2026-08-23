using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Spectacle.Annotations;

/// <summary>
/// Writes the reviewer's unresolved comments back out as a revision brief — instructions addressed
/// to whichever tool or agent authored the document, in the same voice as the gate's fix brief.
///
/// This is the human half of the revision loop. The fix brief carries what the *checks* found; a
/// reviewer reading the document leaves comments on blocks, and those are revision instructions
/// too — usually the more important ones. Handing an agent the raw revision plan works, but the
/// brief form is what the loop's other half already speaks: an explicit scope ("change only what
/// is asked"), bottom-up ordering so applying one revision never moves the next one's block, and
/// each instruction paired with the exact text it applies to, quoted verbatim so the agent edits
/// the right block instead of a paraphrase of it.
///
/// Only unresolved comments participate: a resolved comment is work already done, and re-issuing
/// it would send the agent revising blocks the reviewer has signed off on. Orphaned comments are
/// excluded for the same reason the revision plan drops them — they no longer point at any block
/// in the document, so there is nothing to quote and nothing for the agent to find.
/// </summary>
public static class CommentBriefExporter
{
    /// <summary>
    /// Builds the brief for <paramref name="unresolved"/> — the matched, unresolved comments, in
    /// any order (the brief sorts them bottom-up itself).
    /// </summary>
    public static string Build(string sourcePath, IReadOnlyList<MatchedComment> unresolved)
    {
        var name = Path.GetFileName(sourcePath);
        // Bottom-up, like the fix brief: an edit at line 12 can shift every line after it, so
        // working from the end of the document keeps each quoted block where the brief says it is.
        var revisions = unresolved.OrderByDescending(m => m.CurrentBlock.Line).ToList();

        var sb = new StringBuilder();
        sb.Append("# Revision brief — ").Append(name).AppendLine(" (reviewer comments)").AppendLine();

        sb.Append("A reviewer left ").Append(revisions.Count)
          .Append(revisions.Count == 1 ? " unresolved comment" : " unresolved comments")
          .Append(" on `").Append(name)
          .AppendLine("`. Each one is a revision instruction for the block it quotes. Apply them,")
          .AppendLine("then leave the comments themselves to the reviewer to resolve.");
        sb.AppendLine();

        sb.AppendLine("## How to apply this brief").AppendLine();
        sb.AppendLine("1. Change only the blocks quoted below. Leave every other line exactly as it is.");
        sb.AppendLine("2. Work top to bottom through this list — it is ordered from the end of the document backwards, so each block is still where the brief says it is when you reach it.");
        sb.AppendLine("3. Each revision quotes its block verbatim from the source. Locate that exact text before editing; if it no longer matches the document, skip that revision and report it instead of guessing at a different block.");
        sb.AppendLine("4. Do not add a changelog, a summary of your edits, or a note about this brief. The revised document is the whole deliverable.");

        sb.AppendLine().Append("## Revisions (").Append(revisions.Count).AppendLine(")");

        var n = 0;
        foreach (var m in revisions)
        {
            n++;
            sb.AppendLine();
            sb.Append("### ").Append(n).Append(". Line ").Append(m.CurrentBlock.Line)
              .Append(" — ").AppendLine(m.Comment.BlockAnchor.Kind).AppendLine();

            sb.AppendLine("The block, verbatim from the source:").AppendLine();
            foreach (var line in m.Comment.OriginalText.Split(new[] { "\r\n", "\n" }, System.StringSplitOptions.None))
                sb.Append("> ").AppendLine(line);
            sb.AppendLine();
            sb.AppendLine("**Do this:**").AppendLine();
            sb.AppendLine(m.Comment.Body.TrimEnd('\n'));
        }

        if (revisions.Count == 0)
            sb.AppendLine().AppendLine("No unresolved comments. Leave the document unchanged.");

        return sb.ToString().TrimEnd('\n');
    }
}
