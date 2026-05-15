using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Spectacle.Render;

namespace Spectacle.Annotations;

public static class RevisionPlanExporter
{
    public static string Build(
        string sourcePath,
        string sourceSha256,
        DateTime generatedAt,
        IReadOnlyList<MatchedComment> matched)
    {
        var fileName = Path.GetFileName(sourcePath);
        var sb = new StringBuilder();

        sb.Append("# Revision plan for ").AppendLine(fileName);
        sb.AppendLine();
        sb.Append("Source file: ").Append(sourcePath)
          .Append(" (SHA-256: ").Append(sourceSha256).AppendLine(")");
        sb.Append("Generated: ").Append(generatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ")).AppendLine();
        sb.AppendLine();
        sb.AppendLine("Apply each revision below to the source file. Quote each \"Original\" block");
        sb.AppendLine("verbatim from the source before replacing it; leave all other content");
        sb.AppendLine("unchanged. If an \"Original\" no longer matches the source exactly, stop and");
        sb.AppendLine("report which revision could not be applied.");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();

        var i = 1;
        foreach (var m in matched)
        {
            sb.Append("## Revision ").Append(i).Append(" — ")
              .Append(m.Comment.BlockAnchor.Kind).Append(" at line ")
              .Append(m.CurrentBlock.Line).AppendLine();
            sb.AppendLine();
            sb.AppendLine("**Original (verbatim from source):**");
            sb.AppendLine();
            foreach (var line in m.Comment.OriginalText.Split('\n'))
                sb.Append("> ").AppendLine(line);
            sb.AppendLine();
            sb.AppendLine("**Instruction:**");
            sb.AppendLine();
            sb.AppendLine(m.Comment.Body);
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            i++;
        }

        return sb.ToString();
    }
}
