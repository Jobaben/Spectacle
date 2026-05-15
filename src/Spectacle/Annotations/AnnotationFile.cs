using System.Collections.Generic;
using System.Linq;

namespace Spectacle.Annotations;

public sealed record AnnotationFile(
    int FileVersion,
    string SourcePath,
    string SourceHashAtWrite,
    IReadOnlyList<Comment> Comments)
{
    public bool Equals(AnnotationFile? other) =>
        other is not null
        && FileVersion == other.FileVersion
        && SourcePath == other.SourcePath
        && SourceHashAtWrite == other.SourceHashAtWrite
        && Comments.SequenceEqual(other.Comments);

    public override int GetHashCode() =>
        System.HashCode.Combine(FileVersion, SourcePath, SourceHashAtWrite, Comments.Count);
}
