using System.IO;
using System.Text;

namespace Spectacle.Ai;

/// <summary>
/// Wraps the gate's fix brief in the prompt the app hands to <c>claude -p</c>.
///
/// The brief alone is not enough. It says *what to change* but not *where the result must live*,
/// and an agent handed a document plus revision instructions will often — quite reasonably —
/// write the revised text to a new file next to the original. In a one-shot pipeline that is
/// fine; against a live reader it is fatal: the open document never changes, the file watcher
/// never fires, and the loop timeline records nothing while a <c>*.revised.md</c> quietly
/// accumulates on disk. So the prompt's first and firmest instruction is the in-place contract:
/// edit exactly this file, at exactly this path, and create nothing else.
/// </summary>
public static class ClaudeRevisionPrompt
{
    /// <summary>
    /// Builds the prompt for one revision pass over <paramref name="documentPath"/> (the open
    /// document, as an absolute path) carrying <paramref name="fixBrief"/> (the triaged brief the
    /// reader would otherwise copy).
    /// </summary>
    public static string Build(string documentPath, string fixBrief) =>
        Build(documentPath, fixBrief, ArtifactContextView.None);

    /// <summary>
    /// The same prompt, plus the cross-session handoff contract for a document whose durable state
    /// lives in its <c>artifact_context</c> front-matter namespace.
    ///
    /// Every viewer-started run is a brand-new process with a brand-new session and no memory of
    /// any previous one, so the capsule in the file is the only history there is. Left unsaid, a
    /// small request flattens it: an agent asked to change a retry interval replaces three
    /// sessions of accumulated reasoning with a one-line note about the retry interval. This
    /// section is what makes the merge the expected behavior rather than a lucky one.
    /// </summary>
    public static string Build(string documentPath, string fixBrief, ArtifactContextView context)
    {
        var name = Path.GetFileName(documentPath);

        var sb = new StringBuilder();
        sb.AppendLine("You are the revision step of a live write → gate → revise loop. The Markdown");
        sb.AppendLine("document below is open right now in a reader that re-renders and re-grades it on");
        sb.AppendLine("every save, so your edits are watched as they land.");
        sb.AppendLine();
        sb.Append("Target file — revise it IN PLACE: ").AppendLine(documentPath);
        sb.AppendLine();
        sb.AppendLine("Non-negotiable rules:");
        sb.AppendLine();
        sb.Append("1. Edit the target file directly with your file-editing tools and save it to the exact path above. ")
          .AppendLine("The revised document must end up in that file — the same file that is open in the reader.");
        sb.Append("2. Create no other file. No `").Append(name).Append(".revised.md`, no copy, no backup, ")
          .AppendLine("no draft alongside, no scratch file. Do not rename, move, or delete the target file.");
        sb.AppendLine("3. Apply the revision brief below and change nothing else. Leave every line the brief does not name exactly as it is.");
        sb.AppendLine("4. Do not print the revised document or a diff as chat output, and do not append either to the file. The saved file is the deliverable; your final message is its receipt: end with one or two sentences saying what you changed — the reader shows that message on the run's timeline entry. If you could not apply an ask, say which one and why there, instead of saving an unrelated edit.");
        sb.AppendLine("5. Save in a few coherent passes rather than one monolithic rewrite when the brief allows it — the reader records each save as an iteration, and smaller saves make that history legible.");
        sb.AppendLine("6. When a revision asks for changes to a quoted block, the saved text of that exact block must change: the reader marks a reviewer's ask as addressed only when the block it anchors to changes, so editing only the text around it leaves the ask open no matter how good the edit is.");
        AppendHandoff(sb, context);

        sb.AppendLine();
        sb.AppendLine("The revision brief:");
        sb.AppendLine();
        sb.Append(fixBrief.TrimEnd('\n'));
        return sb.ToString();
    }

    /// <summary>
    /// The cross-session handoff contract, varying by what the document's capsule looks like
    /// right now: inherited and authoritative when it is well formed, repaired when it is broken,
    /// seeded when there is none.
    /// </summary>
    private static void AppendHandoff(StringBuilder sb, ArtifactContextView context)
    {
        sb.AppendLine();
        sb.AppendLine("Cross-session handoff — the artifact carries its own memory:");
        sb.AppendLine();
        sb.Append("This is an independent session. No previous conversation is available to you, and none ")
          .AppendLine("will be available to whoever revises this document next.");
        sb.AppendLine();

        switch (context.State)
        {
            case ArtifactContextState.Present:
                sb.Append("The document's `artifact_context` front-matter namespace is durable context inherited ")
                  .Append("from previous independent sessions — the compressed history, decisions, constraints, ")
                  .AppendLine("evidence and open questions behind the body as it stands. It is authoritative, not");
                sb.AppendLine("documentation about how the file was made.");
                if (context.Sections.Count != 0)
                    sb.Append("It currently carries: ").Append(string.Join(", ", context.Sections)).AppendLine(".");
                break;

            case ArtifactContextState.Malformed:
                sb.Append("The document's `artifact_context` namespace is inherited context from previous ")
                  .AppendLine("independent sessions, and it is currently malformed:");
                foreach (var issue in context.Issues) sb.Append("  - ").AppendLine(issue);
                sb.Append("Do not discard it and start over. Preserve every readable line of meaning it holds and ")
                  .AppendLine("repair the structure conservatively as part of this revision.");
                break;

            default:
                sb.Append("The document does not carry an `artifact_context` namespace yet. Create it in the front ")
                  .Append("matter as part of this revision, seeded with the materially relevant state of the work ")
                  .AppendLine("as it stands after your edit.");
                break;
        }

        sb.AppendLine();
        sb.AppendLine("Before you materially revise the document:");
        sb.AppendLine();
        sb.AppendLine("a. Read the complete file before changing it, front matter included.");
        sb.AppendLine("b. Treat `artifact_context` as the inherited history and the Markdown body as the current state.");
        sb.AppendLine();
        sb.AppendLine("After you have applied the revision, update the capsule:");
        sb.AppendLine();
        sb.Append("c. Collect what this session materially introduced: intent, discoveries, evidence, decisions, ")
          .AppendLine("changed decisions, constraints, assumptions, rejected alternatives, resolved and new questions.");
        sb.Append("d. Semantically merge that into the existing `artifact_context` and recompress the result for ")
          .AppendLine("information density. Merge — do not replace, and do not simply append.");
        sb.Append("e. The capsule is the current semantic state plus its material causal history, not an ")
          .AppendLine("append-only event log. A superseded decision becomes one current decision carrying its");
        sb.Append("   reason — never two contradictory current decisions — and the transition itself goes into ")
          .AppendLine("`history` only when the change is materially causal.");
        sb.Append("f. An open question this session answered no longer belongs under `unresolved`: move the ")
          .AppendLine("outcome into the decision or constraint that answers it.");
        sb.Append("g. Record the revision request's material intent and reason, not the conversational wording ")
          .AppendLine("it arrived in — unless the exact wording is itself materially significant.");
        sb.Append("h. Order the capsule for reconstruction rather than chronology: current purpose, current ")
          .Append("decisions, current constraints, current unresolved state, important evidence, then material ")
          .AppendLine("causal history. A future session must reach the current state without replaying every event.");
        sb.AppendLine("i. Do not overwrite or delete unrelated front matter. Only `artifact_context` is yours to rewrite.");
        sb.Append("j. Validate the result: the front matter must still parse and the document must still render. ")
          .AppendLine("Do not finish while the artifact is structurally invalid.");
        sb.AppendLine();
        sb.Append("The test of your work: a future session handed only this file, the repository and a new request ")
          .AppendLine("must be able to continue correctly, without any access to this conversation.");
    }
}
