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
    public static string Build(string documentPath, string fixBrief)
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
        sb.AppendLine("4. Do not print the revised document, a diff, or a summary of your edits as chat output, and do not append any of those to the file. The saved file is the entire deliverable.");
        sb.AppendLine("5. Save in a few coherent passes rather than one monolithic rewrite when the brief allows it — the reader records each save as an iteration, and smaller saves make that history legible.");
        sb.AppendLine();
        sb.AppendLine("The revision brief:");
        sb.AppendLine();
        sb.Append(fixBrief.TrimEnd('\n'));
        return sb.ToString();
    }
}
