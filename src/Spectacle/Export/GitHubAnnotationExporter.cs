using System.Collections.Generic;
using System.Linq;
using System.Text;
using Spectacle.Gate;

namespace Spectacle.Export;

/// <summary>
/// Emits findings as GitHub Actions workflow commands, so a gate run annotates the diff itself
/// rather than burying its verdict in a log nobody opens.
///
/// SARIF gets the same findings into GitHub's code-scanning tab, but only for repositories with
/// code scanning enabled, and only after an upload step. A workflow command needs neither: any
/// runner that can echo a line gets inline annotations on the pull request, which makes this the
/// zero-configuration path for a project that just added Spectacle to its pipeline.
///
/// Advisory findings are emitted as <c>::notice</c>, so guidance appears without ever looking like
/// a failure.
/// </summary>
public static class GitHubAnnotationExporter
{
    public static string Build(IReadOnlyList<BatchReviewEntry> entries) =>
        Build(entries, GatePolicy.Default);

    public static string Build(IReadOnlyList<BatchReviewEntry> entries, GatePolicy policy)
    {
        var sb = new StringBuilder();
        foreach (var entry in entries)
        {
            foreach (var f in policy.Apply(FindingStream.All(entry.Report)))
                sb.AppendLine(Command(entry.Path, f));
        }
        return sb.ToString().TrimEnd('\n');
    }

    private static string Command(string path, GateFinding f)
    {
        // The command's own vocabulary: "notice" is its lowest level, and the file path uses
        // forward slashes so a Windows runner's backslashes still match the repository path.
        var level = f.Severity switch
        {
            GateSeverity.Error => "error",
            GateSeverity.Warning => "warning",
            _ => "notice",
        };

        return $"::{level} file={Property(path.Replace('\\', '/'))},line={f.Line}," +
               $"title={Property("Spectacle " + f.RuleId)}::{Data(f.Message)}";
    }

    // GitHub parses these lines, so the delimiters have to be escaped or a message containing a
    // comma silently truncates the annotation. Property values escape more than message data:
    // ',' and ':' would otherwise end the property list.
    private static string Property(string value) =>
        Data(value).Replace(",", "%2C").Replace(":", "%3A");

    private static string Data(string value) =>
        value.Replace("%", "%25").Replace("\r", "%0D").Replace("\n", "%0A");
}
