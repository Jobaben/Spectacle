namespace Spectacle.Render;

/// <summary>
/// One heading in the document outline. <see cref="Id"/> is the Markdig
/// auto-generated slug rendered into the heading's <c>id</c> attribute, so the
/// preview can scroll to it with <c>document.getElementById</c>. <see cref="Line"/>
/// is 1-based and matches the <c>data-line</c> tagged onto the heading block,
/// giving the preview a fallback jump target when no id is present.
/// </summary>
public sealed record OutlineEntry(int Level, string Text, string Id, int Line);
