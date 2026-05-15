# Preview Annotations Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the user click any block in Spectacle's preview, write a revision instruction against it, and export the collected instructions as an unambiguous LLM-targeted "revision plan." The source `.md` is never modified by Spectacle.

**Architecture:** Block-level identity is established at render time by attaching `data-*` attributes to each top-level block element via a Markdig AST walk. A per-file sidecar JSON in `%LOCALAPPDATA%` stores comments anchored by `(kind, textHash, occurrenceIndex)`. Strict matching on reload; comments that don't re-bind become orphans (no fuzzy matching). The composer and saved comments render inline-after-block inside the WebView2; the host handles save/delete/resolve via `WebMessageReceived`.

**Tech Stack:** .NET 8 / WPF / Markdig 0.37 (already referenced) / WebView2 / xUnit + FluentAssertions / System.Text.Json (built-in) / System.Security.Cryptography (built-in).

**Spec:** `docs/superpowers/specs/2026-05-15-md-preview-annotations-design.md`.

---

## File Map

**Create:**
- `src/Spectacle/Annotations/BlockAnchor.cs` — immutable record `{ Kind, Line, TextHash, OccurrenceIndex, LeadingText }`.
- `src/Spectacle/Annotations/Comment.cs` — immutable record `{ Id, BlockAnchor, OriginalText, Body, CreatedAt, ResolvedAt? }`.
- `src/Spectacle/Annotations/AnnotationFile.cs` — top-level sidecar record `{ FileVersion, SourcePath, SourceHashAtWrite, Comments }`.
- `src/Spectacle/Annotations/AnnotationStore.cs` — load/save/sidecar path resolution.
- `src/Spectacle/Annotations/AnnotationMatcher.cs` — strict matching → `MatchResult { Matched, Orphaned }`.
- `src/Spectacle/Annotations/MatchedComment.cs` — record `{ Comment, CurrentBlock }`.
- `src/Spectacle/Annotations/RevisionPlanExporter.cs` — produces the LLM revision-plan markdown.
- `src/Spectacle/Render/BlockTagger.cs` — Markdig pipeline extension that attaches `data-*` attributes to top-level blocks and records `TaggedBlock` metadata.
- `src/Spectacle/Render/TaggedBlock.cs` — record `{ BlockId, Kind, Line, TextHash, OccurrenceIndex, OriginalText }`.
- `src/Spectacle/Render/RenderResult.cs` — record `{ Html, Blocks }`.
- `src/Spectacle/Render/Assets/preview-annotations.css` — annotation UI styles (dark + high-contrast).
- `src/Spectacle/Render/Assets/preview-annotations.js` — DOM/composer logic + postMessage.
- `test/Spectacle.Tests/BlockAnchorTests.cs`, `AnnotationStoreTests.cs`, `AnnotationMatcherTests.cs`, `RevisionPlanExporterTests.cs`, `BlockTaggerTests.cs`.
- `test/Spectacle.Tests/Fixtures/revision-plan-3-comments.md` — golden output for exporter.

**Modify:**
- `src/Spectacle/Render/MdRenderer.cs` — add `Render(string markdown) → RenderResult`; keep `ToHtml(string)` as a thin wrapper.
- `src/Spectacle/Render/PreviewHtml.cs` — overload `Build(...)` to accept a `MatchResult` and inject annotation CSS, JS, and a frozen `window.__spectacleAnnotations__` JSON payload.
- `src/Spectacle/Render/PreviewPipeline.cs` — load `AnnotationStore` on construct, match every render, expose `HandleHostMessage(...)`, refresh on mutations.
- `src/Spectacle/Web/WebViewHost.xaml.cs` — wire `WebMessageReceived` to a `HostMessageReceived` event/callback on the control.
- `src/Spectacle/MainWindow.xaml` — add a top bar with **Copy revision plan**, **Export revision plan…**, and count status; collapse when empty.
- `src/Spectacle/MainWindow.xaml.cs` — wire top-bar commands + bridge `WebViewHost.HostMessageReceived` → `PreviewPipeline.HandleHostMessage`.
- `src/Spectacle/Spectacle.csproj` — embed the two new asset files.

---

## Test Conventions

- Project uses xUnit 2.9 + FluentAssertions 6.12. Match naming style of existing tests (`Verb_phrase_with_underscores`).
- Build everything: `dotnet build C:\GIT\Spectacle\Spectacle.slnx`.
- Run a single test: `dotnet test C:\GIT\Spectacle\Spectacle.slnx --filter "FullyQualifiedName~ClassName.TestName"`.
- Run all tests: `dotnet test C:\GIT\Spectacle\Spectacle.slnx`.
- `dotnet run` does not work in this repo per global instructions — never use it.

---

### Task 1: BlockAnchor and Comment records

**Files:**
- Create: `src/Spectacle/Annotations/BlockAnchor.cs`
- Create: `src/Spectacle/Annotations/Comment.cs`
- Create: `src/Spectacle/Annotations/AnnotationFile.cs`
- Test: `test/Spectacle.Tests/BlockAnchorTests.cs`

- [ ] **Step 1: Write the failing test**

Create `test/Spectacle.Tests/BlockAnchorTests.cs`:

```csharp
using System;
using System.Text.Json;
using FluentAssertions;
using Spectacle.Annotations;
using Xunit;

namespace Spectacle.Tests;

public class BlockAnchorTests
{
    [Fact]
    public void BlockAnchor_round_trips_through_json()
    {
        var anchor = new BlockAnchor(
            Kind: "paragraph",
            Line: 42,
            TextHash: "abc123",
            OccurrenceIndex: 0,
            LeadingText: "Spectacle is");

        var json = JsonSerializer.Serialize(anchor);
        var clone = JsonSerializer.Deserialize<BlockAnchor>(json);

        clone.Should().Be(anchor);
    }

    [Fact]
    public void Comment_round_trips_through_json()
    {
        var anchor = new BlockAnchor("paragraph", 1, "h", 0, "lead");
        var comment = new Comment(
            Id: "id-1",
            BlockAnchor: anchor,
            OriginalText: "Hello.",
            Body: "Reword",
            CreatedAt: new DateTime(2026, 5, 15, 10, 0, 0, DateTimeKind.Utc),
            ResolvedAt: null);

        var json = JsonSerializer.Serialize(comment);
        var clone = JsonSerializer.Deserialize<Comment>(json);

        clone.Should().Be(comment);
    }

    [Fact]
    public void AnnotationFile_round_trips_through_json()
    {
        var file = new AnnotationFile(
            FileVersion: 1,
            SourcePath: @"C:\path\README.md",
            SourceHashAtWrite: "abc",
            Comments: new[]
            {
                new Comment("c1",
                    new BlockAnchor("heading", 1, "h", 0, "Hi"),
                    "# Hi", "rename",
                    new DateTime(2026, 5, 15, 0, 0, 0, DateTimeKind.Utc),
                    null)
            });

        var json = JsonSerializer.Serialize(file);
        var clone = JsonSerializer.Deserialize<AnnotationFile>(json);

        clone.Should().Be(file);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test C:\GIT\Spectacle\Spectacle.slnx --filter "FullyQualifiedName~BlockAnchorTests"`

Expected: build failure (`BlockAnchor`, `Comment`, `AnnotationFile` not defined).

- [ ] **Step 3: Create `BlockAnchor`**

Create `src/Spectacle/Annotations/BlockAnchor.cs`:

```csharp
namespace Spectacle.Annotations;

public sealed record BlockAnchor(
    string Kind,
    int Line,
    string TextHash,
    int OccurrenceIndex,
    string LeadingText);
```

- [ ] **Step 4: Create `Comment`**

Create `src/Spectacle/Annotations/Comment.cs`:

```csharp
using System;

namespace Spectacle.Annotations;

public sealed record Comment(
    string Id,
    BlockAnchor BlockAnchor,
    string OriginalText,
    string Body,
    DateTime CreatedAt,
    DateTime? ResolvedAt);
```

- [ ] **Step 5: Create `AnnotationFile`**

Create `src/Spectacle/Annotations/AnnotationFile.cs`:

```csharp
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
```

(Custom equality because the default record equality compares `IReadOnlyList<Comment>` by reference, not by content.)

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test C:\GIT\Spectacle\Spectacle.slnx --filter "FullyQualifiedName~BlockAnchorTests"`
Expected: 3 passing.

- [ ] **Step 7: Commit**

```bash
git add src/Spectacle/Annotations/ test/Spectacle.Tests/BlockAnchorTests.cs
git commit -m "feat(annotations): BlockAnchor, Comment, AnnotationFile records"
```

---

### Task 2: AnnotationStore — sidecar JSON load and save

**Files:**
- Create: `src/Spectacle/Annotations/AnnotationStore.cs`
- Test: `test/Spectacle.Tests/AnnotationStoreTests.cs`

- [ ] **Step 1: Write the failing test**

Create `test/Spectacle.Tests/AnnotationStoreTests.cs`:

```csharp
using System;
using System.IO;
using System.Text.Json;
using FluentAssertions;
using Spectacle.Annotations;
using Xunit;

namespace Spectacle.Tests;

public class AnnotationStoreTests : IDisposable
{
    private readonly string _root;
    private readonly string _sourceFile;

    public AnnotationStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"spectacle-ann-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _sourceFile = Path.Combine(_root, "README.md");
        File.WriteAllText(_sourceFile, "# Hi");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Returns_empty_file_when_sidecar_does_not_exist()
    {
        var store = new AnnotationStore(_sourceFile, sidecarRoot: _root);
        var file = store.Load();

        file.Comments.Should().BeEmpty();
        file.FileVersion.Should().Be(1);
        file.SourcePath.Should().Be(_sourceFile);
    }

    [Fact]
    public void Save_then_Load_round_trips()
    {
        var store = new AnnotationStore(_sourceFile, sidecarRoot: _root);
        var anchor = new BlockAnchor("paragraph", 1, "h", 0, "lead");
        var comment = new Comment("c1", anchor, "Hi.", "rename",
            new DateTime(2026, 5, 15, 10, 0, 0, DateTimeKind.Utc), null);

        store.Save(new AnnotationFile(1, _sourceFile, "src-hash", new[] { comment }));
        var loaded = store.Load();

        loaded.Comments.Should().ContainSingle().Which.Should().Be(comment);
    }

    [Fact]
    public void Save_writes_atomically_no_tmp_remains()
    {
        var store = new AnnotationStore(_sourceFile, sidecarRoot: _root);
        store.Save(new AnnotationFile(1, _sourceFile, "h", System.Array.Empty<Comment>()));

        Directory.EnumerateFiles(store.SidecarDirectory)
            .Where(p => p.EndsWith(".tmp"))
            .Should().BeEmpty();
    }

    [Fact]
    public void Load_renames_corrupt_file_and_returns_empty()
    {
        var store = new AnnotationStore(_sourceFile, sidecarRoot: _root);
        Directory.CreateDirectory(store.SidecarDirectory);
        File.WriteAllText(store.SidecarPath, "{ not valid json");

        var loaded = store.Load();

        loaded.Comments.Should().BeEmpty();
        File.Exists(store.SidecarPath).Should().BeFalse();
        Directory.EnumerateFiles(store.SidecarDirectory)
            .Should().Contain(p => p.Contains(".corrupt-"));
    }

    [Fact]
    public void Two_different_source_paths_produce_different_sidecar_paths()
    {
        var other = Path.Combine(_root, "OTHER.md");
        File.WriteAllText(other, "x");

        var a = new AnnotationStore(_sourceFile, sidecarRoot: _root);
        var b = new AnnotationStore(other, sidecarRoot: _root);

        a.SidecarPath.Should().NotBe(b.SidecarPath);
    }

    [Fact]
    public void Sidecar_path_is_case_insensitive_for_source_path()
    {
        var upper = _sourceFile.ToUpperInvariant();
        var lower = _sourceFile.ToLowerInvariant();

        var a = new AnnotationStore(upper, sidecarRoot: _root);
        var b = new AnnotationStore(lower, sidecarRoot: _root);

        a.SidecarPath.Should().Be(b.SidecarPath);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test C:\GIT\Spectacle\Spectacle.slnx --filter "FullyQualifiedName~AnnotationStoreTests"`
Expected: build failure (`AnnotationStore` not defined).

- [ ] **Step 3: Implement `AnnotationStore`**

Create `src/Spectacle/Annotations/AnnotationStore.cs`:

```csharp
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Spectacle.Annotations;

public sealed class AnnotationStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _sourcePath;

    public AnnotationStore(string sourcePath) : this(sourcePath, DefaultSidecarRoot()) { }

    public AnnotationStore(string sourcePath, string sidecarRoot)
    {
        _sourcePath = Path.GetFullPath(sourcePath);
        SidecarDirectory = sidecarRoot;
        SidecarPath = Path.Combine(sidecarRoot, HashPath(_sourcePath) + ".json");
    }

    public string SidecarDirectory { get; }
    public string SidecarPath { get; }

    public AnnotationFile Load()
    {
        if (!File.Exists(SidecarPath))
            return Empty();

        string json;
        try { json = File.ReadAllText(SidecarPath); }
        catch (IOException) { return Empty(); }

        try
        {
            var file = JsonSerializer.Deserialize<AnnotationFile>(json, JsonOpts);
            return file ?? Empty();
        }
        catch (JsonException)
        {
            var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var dest = SidecarPath + $".corrupt-{stamp}";
            try { File.Move(SidecarPath, dest, overwrite: true); }
            catch (IOException) { /* best-effort */ }
            return Empty();
        }
    }

    public void Save(AnnotationFile file)
    {
        Directory.CreateDirectory(SidecarDirectory);
        var tmp = SidecarPath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(file, JsonOpts));
        File.Move(tmp, SidecarPath, overwrite: true);
    }

    private AnnotationFile Empty() =>
        new(FileVersion: 1, SourcePath: _sourcePath, SourceHashAtWrite: string.Empty,
            Comments: Array.Empty<Comment>());

    private static string DefaultSidecarRoot() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Spectacle", "annotations");

    private static string HashPath(string path)
    {
        var key = path.ToLowerInvariant();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test C:\GIT\Spectacle\Spectacle.slnx --filter "FullyQualifiedName~AnnotationStoreTests"`
Expected: 6 passing.

- [ ] **Step 5: Commit**

```bash
git add src/Spectacle/Annotations/AnnotationStore.cs test/Spectacle.Tests/AnnotationStoreTests.cs
git commit -m "feat(annotations): AnnotationStore with atomic write + corrupt-file recovery"
```

---

### Task 3: BlockTagger — attach `data-*` attributes to top-level blocks

**Files:**
- Create: `src/Spectacle/Render/TaggedBlock.cs`
- Create: `src/Spectacle/Render/RenderResult.cs`
- Create: `src/Spectacle/Render/BlockTagger.cs`
- Modify: `src/Spectacle/Render/MdRenderer.cs`
- Test: `test/Spectacle.Tests/BlockTaggerTests.cs`

- [ ] **Step 1: Write the failing test**

Create `test/Spectacle.Tests/BlockTaggerTests.cs`:

```csharp
using System.Linq;
using FluentAssertions;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class BlockTaggerTests
{
    private static RenderResult Render(string md) => new MdRenderer().Render(md);

    [Fact]
    public void Heading_gets_md_block_attributes()
    {
        var r = Render("# Hello\n");

        r.Html.Should().Contain("class=\"md-block\"");
        r.Html.Should().Contain("data-kind=\"heading\"");
        r.Html.Should().Contain("data-block-id=\"b0\"");
        r.Html.Should().Contain("data-line=\"1\"");
        r.Html.Should().Contain("data-occurrence-index=\"0\"");
        r.Html.Should().Contain("tabindex=\"0\"");
        r.Blocks.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new
            {
                Kind = "heading",
                Line = 1,
                OccurrenceIndex = 0,
                BlockId = "b0"
            });
    }

    [Fact]
    public void Paragraph_and_heading_each_get_one_id()
    {
        var r = Render("# Hi\n\nA paragraph.\n");

        r.Blocks.Select(b => b.Kind).Should().Equal("heading", "paragraph");
        r.Blocks.Select(b => b.BlockId).Should().Equal("b0", "b1");
    }

    [Fact]
    public void Two_identical_paragraphs_get_separate_occurrence_indexes()
    {
        var r = Render("Same.\n\nSame.\n");

        var paras = r.Blocks.Where(b => b.Kind == "paragraph").ToList();
        paras.Should().HaveCount(2);
        paras[0].TextHash.Should().Be(paras[1].TextHash);
        paras[0].OccurrenceIndex.Should().Be(0);
        paras[1].OccurrenceIndex.Should().Be(1);
    }

    [Fact]
    public void Text_hash_is_stable_for_unchanged_input()
    {
        var a = Render("# Hello\n").Blocks[0].TextHash;
        var b = Render("# Hello\n").Blocks[0].TextHash;
        a.Should().Be(b);
    }

    [Fact]
    public void Text_hash_normalizes_line_endings()
    {
        var lf = Render("# Hello\n").Blocks[0].TextHash;
        var crlf = Render("# Hello\r\n").Blocks[0].TextHash;
        crlf.Should().Be(lf);
    }

    [Fact]
    public void Code_block_is_tagged_as_code()
    {
        var r = Render("```cs\nvar x = 1;\n```\n");

        r.Blocks.Should().ContainSingle().Which.Kind.Should().Be("code");
        r.Html.Should().Contain("data-kind=\"code\"");
    }

    [Fact]
    public void Blockquote_is_tagged_once_inner_paragraphs_are_not()
    {
        var r = Render("> a quote\n");

        r.Blocks.Should().ContainSingle().Which.Kind.Should().Be("blockquote");
    }

    [Fact]
    public void List_items_are_tagged_the_list_itself_is_not()
    {
        var r = Render("- one\n- two\n- three\n");

        r.Blocks.Select(b => b.Kind).Should().Equal("list-item", "list-item", "list-item");
        r.Blocks.Select(b => b.OccurrenceIndex).Should().Equal(0, 0, 0);
        r.Blocks.Select(b => b.TextHash).Distinct().Should().HaveCount(3);
    }

    [Fact]
    public void Thematic_break_is_tagged_as_hr()
    {
        var r = Render("---\n");

        r.Blocks.Should().ContainSingle().Which.Kind.Should().Be("hr");
    }

    [Fact]
    public void Original_text_round_trips_normalized()
    {
        var r = Render("Hello world.\n");

        r.Blocks[0].OriginalText.Should().Be("Hello world.");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test C:\GIT\Spectacle\Spectacle.slnx --filter "FullyQualifiedName~BlockTaggerTests"`
Expected: build failure (`RenderResult`, `TaggedBlock`, `MdRenderer.Render` not defined).

- [ ] **Step 3: Create `TaggedBlock`**

Create `src/Spectacle/Render/TaggedBlock.cs`:

```csharp
namespace Spectacle.Render;

public sealed record TaggedBlock(
    string BlockId,
    string Kind,
    int Line,
    string TextHash,
    int OccurrenceIndex,
    string OriginalText);
```

- [ ] **Step 4: Create `RenderResult`**

Create `src/Spectacle/Render/RenderResult.cs`:

```csharp
using System.Collections.Generic;

namespace Spectacle.Render;

public sealed record RenderResult(string Html, IReadOnlyList<TaggedBlock> Blocks);
```

- [ ] **Step 5: Implement `BlockTagger`**

Create `src/Spectacle/Render/BlockTagger.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Markdig.Renderers.Html;
using Markdig.Syntax;

namespace Spectacle.Render;

internal static class BlockTagger
{
    public static IReadOnlyList<TaggedBlock> TagDocument(MarkdownDocument document, string source)
    {
        var result = new List<TaggedBlock>();
        var counts = new Dictionary<(string, string), int>();

        foreach (var block in document)
        {
            var kind = KindOf(block);
            if (kind is null) continue;

            var raw = SliceSource(source, block);
            var normalized = NormalizeText(raw);
            var hash = Sha256Hex(normalized);
            var key = (kind, hash);
            var occurrence = counts.TryGetValue(key, out var n) ? n : 0;
            counts[key] = occurrence + 1;

            var blockId = $"b{result.Count}";
            var line = block.Line + 1;
            var leading = LeadingText(normalized);

            var attrs = block.GetAttributes();
            attrs.AddClass("md-block");
            attrs.AddPropertyIfNotExist("data-block-id", blockId);
            attrs.AddPropertyIfNotExist("data-kind", kind);
            attrs.AddPropertyIfNotExist("data-line", line.ToString());
            attrs.AddPropertyIfNotExist("data-text-hash", hash);
            attrs.AddPropertyIfNotExist("data-occurrence-index", occurrence.ToString());
            attrs.AddPropertyIfNotExist("tabindex", "0");

            // For list blocks, descend and tag each list-item; the list itself stays untagged.
            if (block is ListBlock list)
            {
                attrs.Classes?.Remove("md-block");
                attrs.Properties?.RemoveAll(p =>
                    p.Key is "data-block-id" or "data-kind" or "data-line"
                            or "data-text-hash" or "data-occurrence-index" or "tabindex");
                TagListItems(list, source, result, counts);
                continue;
            }

            result.Add(new TaggedBlock(blockId, kind, line, hash, occurrence, normalized));
        }

        return result;
    }

    private static void TagListItems(
        ListBlock list, string source,
        List<TaggedBlock> result,
        Dictionary<(string, string), int> counts)
    {
        foreach (var child in list)
        {
            if (child is not ListItemBlock item) continue;

            var raw = SliceSource(source, item);
            var normalized = NormalizeText(raw);
            var hash = Sha256Hex(normalized);
            var key = ("list-item", hash);
            var occurrence = counts.TryGetValue(key, out var n) ? n : 0;
            counts[key] = occurrence + 1;

            var blockId = $"b{result.Count}";
            var line = item.Line + 1;

            var attrs = item.GetAttributes();
            attrs.AddClass("md-block");
            attrs.AddPropertyIfNotExist("data-block-id", blockId);
            attrs.AddPropertyIfNotExist("data-kind", "list-item");
            attrs.AddPropertyIfNotExist("data-line", line.ToString());
            attrs.AddPropertyIfNotExist("data-text-hash", hash);
            attrs.AddPropertyIfNotExist("data-occurrence-index", occurrence.ToString());
            attrs.AddPropertyIfNotExist("tabindex", "0");

            result.Add(new TaggedBlock(blockId, "list-item", line, hash, occurrence, normalized));
        }
    }

    private static string? KindOf(Block block) => block switch
    {
        HeadingBlock => "heading",
        ParagraphBlock => "paragraph",
        FencedCodeBlock => "code",
        CodeBlock => "code",
        QuoteBlock => "blockquote",
        Markdig.Extensions.Tables.Table => "table",
        ThematicBreakBlock => "hr",
        HtmlBlock => "html",
        ListBlock => "list",
        _ => null
    };

    private static string SliceSource(string source, Block block)
    {
        if (block.Span.Start < 0 || block.Span.End < block.Span.Start) return string.Empty;
        var start = Math.Min(block.Span.Start, source.Length);
        var endInclusive = Math.Min(block.Span.End, source.Length - 1);
        if (endInclusive < start) return string.Empty;
        return source.Substring(start, endInclusive - start + 1);
    }

    internal static string NormalizeText(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;
        var lines = raw.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var i = 0; i < lines.Length; i++)
            lines[i] = lines[i].TrimEnd();
        var joined = string.Join("\n", lines);
        return joined.TrimEnd('\n');
    }

    private static string LeadingText(string normalized)
    {
        var firstLine = normalized.Split('\n')[0];
        return firstLine.Length <= 80 ? firstLine : firstLine.Substring(0, 80);
    }

    private static string Sha256Hex(string s)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(s));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
```

- [ ] **Step 6: Modify `MdRenderer` to expose `Render`**

Replace contents of `src/Spectacle/Render/MdRenderer.cs`:

```csharp
using Markdig;
using Markdig.Syntax;

namespace Spectacle.Render;

public sealed class MdRenderer
{
    private readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseEmojiAndSmiley()
        .UseAutoIdentifiers()
        .UseGenericAttributes()
        .Build();

    public RenderResult Render(string markdown)
    {
        var source = markdown ?? string.Empty;
        var document = Markdown.Parse(source, _pipeline);
        var blocks = BlockTagger.TagDocument(document, source);
        var html = document.ToHtml(_pipeline);
        return new RenderResult(html, blocks);
    }

    public string ToHtml(string markdown) => Render(markdown).Html;
}
```

`UseGenericAttributes()` is what makes Markdig emit the `HtmlAttributes` that `GetAttributes()/AddProperty` populate.

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test C:\GIT\Spectacle\Spectacle.slnx --filter "FullyQualifiedName~BlockTaggerTests"`
Expected: 10 passing.

- [ ] **Step 8: Re-run MdRendererTests to confirm no regression**

Run: `dotnet test C:\GIT\Spectacle\Spectacle.slnx --filter "FullyQualifiedName~MdRendererTests"`
Expected: 6 passing (the existing tests still call `ToHtml`).

Note: existing fixture snapshots will likely fail because output now contains `class="md-block"` and `data-*` attributes. Update the fixtures inline:

```bash
# For each failing fixture: regenerate, eyeball-diff, accept.
# Manual step — use the test output to overwrite expected .html files,
# then re-run to confirm green.
```

- [ ] **Step 9: Commit**

```bash
git add src/Spectacle/Render/ test/Spectacle.Tests/BlockTaggerTests.cs test/Spectacle.Tests/Fixtures/
git commit -m "feat(render): BlockTagger attaches md-block data-* attributes to top-level blocks"
```

---

### Task 4: AnnotationMatcher — strict `(Kind, TextHash, OccurrenceIndex)` matching

**Files:**
- Create: `src/Spectacle/Annotations/MatchedComment.cs`
- Create: `src/Spectacle/Annotations/AnnotationMatcher.cs`
- Test: `test/Spectacle.Tests/AnnotationMatcherTests.cs`

- [ ] **Step 1: Write the failing test**

Create `test/Spectacle.Tests/AnnotationMatcherTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using FluentAssertions;
using Spectacle.Annotations;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class AnnotationMatcherTests
{
    private static Comment MakeComment(string kind, string textHash, int occ, string body = "x") =>
        new(
            Id: Guid.NewGuid().ToString(),
            BlockAnchor: new BlockAnchor(kind, Line: 1, TextHash: textHash,
                OccurrenceIndex: occ, LeadingText: "lead"),
            OriginalText: "orig",
            Body: body,
            CreatedAt: new DateTime(2026, 5, 15, 0, 0, 0, DateTimeKind.Utc),
            ResolvedAt: null);

    private static TaggedBlock MakeBlock(string id, string kind, int line, string hash, int occ) =>
        new(id, kind, line, hash, occ, OriginalText: "orig");

    [Fact]
    public void Matches_when_kind_hash_and_occurrence_align()
    {
        var c = MakeComment("paragraph", "h1", 0);
        var b = MakeBlock("b0", "paragraph", 5, "h1", 0);

        var result = AnnotationMatcher.Match(new[] { b }, new[] { c });

        result.Matched.Should().ContainSingle();
        result.Matched[0].Comment.Should().Be(c);
        result.Matched[0].CurrentBlock.Should().Be(b);
        result.Orphaned.Should().BeEmpty();
    }

    [Fact]
    public void Orphans_when_text_hash_changes()
    {
        var c = MakeComment("paragraph", "old-hash", 0);
        var b = MakeBlock("b0", "paragraph", 1, "new-hash", 0);

        var result = AnnotationMatcher.Match(new[] { b }, new[] { c });

        result.Matched.Should().BeEmpty();
        result.Orphaned.Should().ContainSingle().Which.Should().Be(c);
    }

    [Fact]
    public void Orphans_when_kind_changes()
    {
        var c = MakeComment("paragraph", "h", 0);
        var b = MakeBlock("b0", "heading", 1, "h", 0);

        var result = AnnotationMatcher.Match(new[] { b }, new[] { c });

        result.Orphaned.Should().ContainSingle();
    }

    [Fact]
    public void Orphans_when_occurrence_index_no_longer_exists()
    {
        var c = MakeComment("paragraph", "h", 1); // wanted the 2nd; only 1 exists
        var b = MakeBlock("b0", "paragraph", 1, "h", 0);

        var result = AnnotationMatcher.Match(new[] { b }, new[] { c });

        result.Orphaned.Should().ContainSingle();
    }

    [Fact]
    public void Survives_line_shift_when_kind_hash_occurrence_unchanged()
    {
        var c = MakeComment("paragraph", "h", 0);
        var b = MakeBlock("b0", "paragraph", 99, "h", 0);

        var result = AnnotationMatcher.Match(new[] { b }, new[] { c });

        result.Matched.Should().ContainSingle();
    }

    [Fact]
    public void Two_identical_blocks_two_comments_each_one_matches_its_own()
    {
        var c1 = MakeComment("paragraph", "h", 0, body: "first");
        var c2 = MakeComment("paragraph", "h", 1, body: "second");
        var b1 = MakeBlock("b0", "paragraph", 1, "h", 0);
        var b2 = MakeBlock("b1", "paragraph", 5, "h", 1);

        var result = AnnotationMatcher.Match(new[] { b1, b2 }, new[] { c1, c2 });

        result.Matched.Should().HaveCount(2);
        result.Orphaned.Should().BeEmpty();
        result.Matched.Should().Contain(m => m.Comment.Body == "first" && m.CurrentBlock.BlockId == "b0");
        result.Matched.Should().Contain(m => m.Comment.Body == "second" && m.CurrentBlock.BlockId == "b1");
    }

    [Fact]
    public void Block_deleted_orphans_its_comment()
    {
        var c = MakeComment("paragraph", "h", 0);

        var result = AnnotationMatcher.Match(Array.Empty<TaggedBlock>(), new[] { c });

        result.Orphaned.Should().ContainSingle();
    }

    [Fact]
    public void Block_inserted_above_does_not_disturb_existing_match()
    {
        var c = MakeComment("paragraph", "target", 0);
        var inserted = MakeBlock("b0", "paragraph", 1, "new", 0);
        var target = MakeBlock("b1", "paragraph", 3, "target", 0);

        var result = AnnotationMatcher.Match(new[] { inserted, target }, new[] { c });

        result.Matched.Should().ContainSingle()
            .Which.CurrentBlock.Should().Be(target);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test C:\GIT\Spectacle\Spectacle.slnx --filter "FullyQualifiedName~AnnotationMatcherTests"`
Expected: build failure.

- [ ] **Step 3: Create `MatchedComment` and `MatchResult`**

Create `src/Spectacle/Annotations/MatchedComment.cs`:

```csharp
using System.Collections.Generic;
using Spectacle.Render;

namespace Spectacle.Annotations;

public sealed record MatchedComment(Comment Comment, TaggedBlock CurrentBlock);

public sealed record MatchResult(
    IReadOnlyList<MatchedComment> Matched,
    IReadOnlyList<Comment> Orphaned);
```

- [ ] **Step 4: Implement `AnnotationMatcher`**

Create `src/Spectacle/Annotations/AnnotationMatcher.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using Spectacle.Render;

namespace Spectacle.Annotations;

public static class AnnotationMatcher
{
    public static MatchResult Match(
        IReadOnlyList<TaggedBlock> currentBlocks,
        IReadOnlyList<Comment> savedComments)
    {
        var byKey = currentBlocks.ToDictionary(
            b => (b.Kind, b.TextHash, b.OccurrenceIndex),
            b => b);

        var matched = new List<MatchedComment>();
        var orphaned = new List<Comment>();

        foreach (var comment in savedComments)
        {
            var key = (
                comment.BlockAnchor.Kind,
                comment.BlockAnchor.TextHash,
                comment.BlockAnchor.OccurrenceIndex);

            if (byKey.TryGetValue(key, out var block))
                matched.Add(new MatchedComment(comment, block));
            else
                orphaned.Add(comment);
        }

        return new MatchResult(matched, orphaned);
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test C:\GIT\Spectacle\Spectacle.slnx --filter "FullyQualifiedName~AnnotationMatcherTests"`
Expected: 8 passing.

- [ ] **Step 6: Commit**

```bash
git add src/Spectacle/Annotations/MatchedComment.cs src/Spectacle/Annotations/AnnotationMatcher.cs test/Spectacle.Tests/AnnotationMatcherTests.cs
git commit -m "feat(annotations): AnnotationMatcher with strict kind+hash+occurrence matching"
```

---

### Task 5: RevisionPlanExporter — produce LLM revision-plan markdown

**Files:**
- Create: `src/Spectacle/Annotations/RevisionPlanExporter.cs`
- Create: `test/Spectacle.Tests/RevisionPlanExporterTests.cs`

- [ ] **Step 1: Write the failing test**

Create `test/Spectacle.Tests/RevisionPlanExporterTests.cs`:

```csharp
using System;
using FluentAssertions;
using Spectacle.Annotations;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class RevisionPlanExporterTests
{
    private static readonly DateTime FixedNow = new(2026, 5, 15, 10, 0, 0, DateTimeKind.Utc);

    private static MatchedComment MatchOf(string kind, int line, string body, string original)
    {
        var anchor = new BlockAnchor(kind, line, "h", 0, original);
        var c = new Comment("c", anchor, original, body, FixedNow, null);
        var b = new TaggedBlock("b0", kind, line, "h", 0, original);
        return new MatchedComment(c, b);
    }

    [Fact]
    public void Header_contains_source_path_and_sha()
    {
        var plan = RevisionPlanExporter.Build(
            sourcePath: @"C:\path\README.md",
            sourceSha256: "abc123",
            generatedAt: FixedNow,
            matched: System.Array.Empty<MatchedComment>());

        plan.Should().Contain(@"C:\path\README.md");
        plan.Should().Contain("SHA-256: abc123");
        plan.Should().Contain("2026-05-15T10:00:00");
    }

    [Fact]
    public void Single_revision_quotes_original_and_includes_instruction()
    {
        var match = MatchOf("paragraph", 42, "Reword for clarity.", "Spectacle is a viewer.");

        var plan = RevisionPlanExporter.Build(
            @"C:\R.md", "h", FixedNow, new[] { match });

        plan.Should().Contain("## Revision 1 — paragraph at line 42");
        plan.Should().Contain("> Spectacle is a viewer.");
        plan.Should().Contain("Reword for clarity.");
    }

    [Fact]
    public void Multi_line_original_is_quoted_with_per_line_prefix()
    {
        var match = MatchOf("code", 5, "explain this", "line one\nline two\nline three");

        var plan = RevisionPlanExporter.Build(@"C:\R.md", "h", FixedNow, new[] { match });

        plan.Should().Contain("> line one");
        plan.Should().Contain("> line two");
        plan.Should().Contain("> line three");
    }

    [Fact]
    public void Revisions_are_numbered_in_input_order()
    {
        var a = MatchOf("paragraph", 1, "first", "A");
        var b = MatchOf("paragraph", 2, "second", "B");
        var c = MatchOf("paragraph", 3, "third", "C");

        var plan = RevisionPlanExporter.Build(@"C:\R.md", "h", FixedNow, new[] { a, b, c });

        var idx1 = plan.IndexOf("## Revision 1");
        var idx2 = plan.IndexOf("## Revision 2");
        var idx3 = plan.IndexOf("## Revision 3");
        idx1.Should().BeLessThan(idx2);
        idx2.Should().BeLessThan(idx3);
    }

    [Fact]
    public void Empty_matched_produces_header_with_no_revisions()
    {
        var plan = RevisionPlanExporter.Build(@"C:\R.md", "h", FixedNow, System.Array.Empty<MatchedComment>());

        plan.Should().Contain("# Revision plan");
        plan.Should().NotContain("## Revision");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test C:\GIT\Spectacle\Spectacle.slnx --filter "FullyQualifiedName~RevisionPlanExporterTests"`
Expected: build failure.

- [ ] **Step 3: Implement `RevisionPlanExporter`**

Create `src/Spectacle/Annotations/RevisionPlanExporter.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test C:\GIT\Spectacle\Spectacle.slnx --filter "FullyQualifiedName~RevisionPlanExporterTests"`
Expected: 5 passing.

- [ ] **Step 5: Commit**

```bash
git add src/Spectacle/Annotations/RevisionPlanExporter.cs test/Spectacle.Tests/RevisionPlanExporterTests.cs
git commit -m "feat(annotations): RevisionPlanExporter produces LLM-targeted markdown"
```

---

### Task 6: preview-annotations.css — visual layer for hover, composer, comment cards

**Files:**
- Create: `src/Spectacle/Render/Assets/preview-annotations.css`
- Modify: `src/Spectacle/Spectacle.csproj` (embed the asset)

- [ ] **Step 1: Create the CSS asset**

Create `src/Spectacle/Render/Assets/preview-annotations.css`:

```css
.md-block {
  position: relative;
  border-left: 3px solid transparent;
  padding-left: 0.5rem;
  margin-left: -0.5rem;
  outline: none;
}
.md-block:hover { border-left-color: var(--accent, #4ea1ff); cursor: pointer; }
.md-block:focus-visible {
  border-left-color: var(--focus, #7cb7ff);
  outline: 2px solid var(--focus, #7cb7ff);
  outline-offset: 2px;
  border-radius: 2px;
}
.md-block[data-has-comments="true"]::after {
  content: "💬 " attr(data-comment-count);
  position: absolute;
  top: 0.1em;
  right: 0;
  font-size: 0.75em;
  color: var(--muted, #9aa0a6);
}

.sp-card {
  border-left: 3px solid var(--accent, #4ea1ff);
  background: var(--card-bg, #252526);
  border-radius: 4px;
  padding: 12px 14px;
  margin: 6px 0 14px 0;
  font-size: 0.95em;
}
.sp-card .sp-header {
  font-weight: 600;
  margin-bottom: 6px;
  color: var(--accent, #4ea1ff);
}
.sp-card .sp-meta {
  font-size: 0.8em;
  color: var(--muted, #9aa0a6);
  margin-bottom: 6px;
}
.sp-card .sp-body { white-space: pre-wrap; }
.sp-card.sp-resolved { opacity: 0.55; }

.sp-composer textarea {
  width: 100%;
  min-height: 5em;
  font-family: inherit;
  font-size: inherit;
  background: var(--code-bg, #1c1c1c);
  color: inherit;
  border: 1px solid var(--rule, #3c3c3c);
  border-radius: 4px;
  padding: 6px 8px;
  box-sizing: border-box;
  resize: vertical;
}
.sp-composer .sp-actions { margin-top: 8px; display: flex; gap: 8px; }
.sp-composer button {
  background: var(--code-bg, #1c1c1c);
  color: inherit;
  border: 1px solid var(--rule, #3c3c3c);
  border-radius: 4px;
  padding: 4px 12px;
  cursor: pointer;
  font: inherit;
}
.sp-composer button:focus-visible { outline: 2px solid var(--focus, #7cb7ff); outline-offset: 2px; }
.sp-composer button.sp-primary { background: var(--accent, #4ea1ff); color: #0a0a0a; border-color: transparent; }
.sp-composer button.sp-primary:disabled { opacity: 0.5; cursor: not-allowed; }

.sp-orphans {
  background: var(--card-bg, #252526);
  border: 1px solid var(--rule, #3c3c3c);
  border-radius: 4px;
  margin: 0 0 18px 0;
  padding: 10px 14px;
}
.sp-orphans-header { font-weight: 600; cursor: pointer; }
.sp-orphans ul { margin: 6px 0 0 0; padding-left: 1.2em; }
.sp-orphans li { margin-bottom: 4px; }

@media (forced-colors: active) {
  .md-block:hover, .md-block:focus-visible { border-left-color: Highlight; }
  .sp-card { border-left-color: Highlight; background: Canvas; }
  .sp-composer textarea, .sp-composer button { background: Canvas; color: CanvasText; border-color: CanvasText; }
  .sp-composer button.sp-primary { background: Highlight; color: HighlightText; }
}
```

- [ ] **Step 2: Embed the asset in the csproj**

Modify `src/Spectacle/Spectacle.csproj`. Inside the existing `<ItemGroup>` that lists `EmbeddedResource` entries, add:

```xml
<EmbeddedResource Include="Render\Assets\preview-annotations.css" />
```

The resulting block should look like:

```xml
<ItemGroup>
  <EmbeddedResource Include="Render\Assets\preview.css" />
  <EmbeddedResource Include="Render\Assets\dark.css" />
  <EmbeddedResource Include="Render\Assets\hc.css" />
  <EmbeddedResource Include="Render\Assets\prism.min.js" />
  <EmbeddedResource Include="Render\Assets\prism.css" />
  <EmbeddedResource Include="Render\Assets\preview-annotations.css" />
</ItemGroup>
```

- [ ] **Step 3: Verify build**

Run: `dotnet build C:\GIT\Spectacle\Spectacle.slnx`
Expected: success.

- [ ] **Step 4: Commit**

```bash
git add src/Spectacle/Render/Assets/preview-annotations.css src/Spectacle/Spectacle.csproj
git commit -m "feat(render): preview-annotations.css for hover, composer, and comment cards"
```

---

### Task 7: preview-annotations.js — composer, comment rendering, postMessage

**Files:**
- Create: `src/Spectacle/Render/Assets/preview-annotations.js`
- Modify: `src/Spectacle/Spectacle.csproj` (embed the asset)

- [ ] **Step 1: Create the JS asset**

Create `src/Spectacle/Render/Assets/preview-annotations.js`:

```javascript
(function () {
  "use strict";
  var data = window.__spectacleAnnotations__ || { comments: [], orphaned: [] };

  function post(type, payload) {
    if (!window.chrome || !window.chrome.webview) return;
    window.chrome.webview.postMessage(JSON.stringify(Object.assign({ type: type }, payload)));
  }

  function uuid() {
    return ([1e7]+-1e3+-4e3+-8e3+-1e11).replace(/[018]/g, function (c) {
      return (c ^ crypto.getRandomValues(new Uint8Array(1))[0] & 15 >> c / 4).toString(16);
    });
  }

  function escapeHtml(s) {
    return s.replace(/[&<>"']/g, function (ch) {
      return { "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[ch];
    });
  }

  function formatTimestamp(iso) {
    try {
      var d = new Date(iso);
      return d.toLocaleString();
    } catch (e) { return iso; }
  }

  function buildCard(comment, index) {
    var card = document.createElement("article");
    card.className = "sp-card";
    if (comment.resolvedAt) card.className += " sp-resolved";
    card.setAttribute("role", "comment");
    card.setAttribute("data-comment-id", comment.id);
    card.setAttribute("aria-label",
      "Revision request " + index + " on " +
      comment.blockAnchor.kind + " at line " + comment.blockAnchor.line);

    var header = document.createElement("div");
    header.className = "sp-header";
    header.textContent = "Revision request #" + index;
    card.appendChild(header);

    var meta = document.createElement("div");
    meta.className = "sp-meta";
    meta.textContent = formatTimestamp(comment.createdAt) +
      (comment.resolvedAt ? " · Resolved" : "");
    card.appendChild(meta);

    var body = document.createElement("div");
    body.className = "sp-body";
    body.textContent = comment.body;
    card.appendChild(body);

    var actions = document.createElement("div");
    actions.className = "sp-actions";

    var editBtn = document.createElement("button");
    editBtn.textContent = "Edit";
    editBtn.addEventListener("click", function () { startCompose(comment.blockAnchor.blockIdAtRender, comment); });

    var resolveBtn = document.createElement("button");
    resolveBtn.textContent = comment.resolvedAt ? "Reopen" : "Resolve";
    resolveBtn.addEventListener("click", function () {
      post("commentResolve", { commentId: comment.id, resolved: !comment.resolvedAt });
    });

    var deleteBtn = document.createElement("button");
    deleteBtn.textContent = "Delete";
    deleteBtn.addEventListener("click", function () {
      post("commentDelete", { commentId: comment.id });
    });

    actions.appendChild(editBtn);
    actions.appendChild(resolveBtn);
    actions.appendChild(deleteBtn);
    card.appendChild(actions);

    return card;
  }

  function startCompose(blockId, existing) {
    var block = document.querySelector('[data-block-id="' + blockId + '"]');
    if (!block) return;

    var existingComposer = document.querySelector(".sp-composer");
    if (existingComposer) existingComposer.remove();

    var composer = document.createElement("div");
    composer.className = "sp-card sp-composer";

    var header = document.createElement("div");
    header.className = "sp-header";
    header.textContent = existing ? "Edit revision request" : "New revision request";
    composer.appendChild(header);

    var textarea = document.createElement("textarea");
    textarea.value = existing ? existing.body : "";
    composer.appendChild(textarea);

    var actions = document.createElement("div");
    actions.className = "sp-actions";

    var saveBtn = document.createElement("button");
    saveBtn.className = "sp-primary";
    saveBtn.textContent = "Save";
    saveBtn.disabled = textarea.value.trim().length === 0;
    saveBtn.addEventListener("click", function () { commit(); });

    var cancelBtn = document.createElement("button");
    cancelBtn.textContent = "Cancel";
    cancelBtn.addEventListener("click", function () { composer.remove(); });

    textarea.addEventListener("input", function () {
      saveBtn.disabled = textarea.value.trim().length === 0;
    });
    textarea.addEventListener("keydown", function (e) {
      if (e.key === "Escape") { composer.remove(); }
      else if (e.key === "Enter" && (e.ctrlKey || e.metaKey)) {
        if (!saveBtn.disabled) commit();
      }
    });

    actions.appendChild(saveBtn);
    actions.appendChild(cancelBtn);
    composer.appendChild(actions);

    function commit() {
      var body = textarea.value.trim();
      if (body.length === 0) return;
      if (existing) {
        post("commentSave", { commentId: existing.id, blockId: blockId, body: body });
      } else {
        post("commentSave", { commentId: uuid(), blockId: blockId, body: body });
      }
    }

    block.insertAdjacentElement("afterend", composer);
    textarea.focus();
  }

  function renderExistingComments() {
    var byBlock = {};
    (data.comments || []).forEach(function (c) {
      var key = c.blockAnchor.blockIdAtRender;
      if (!key) return;
      (byBlock[key] = byBlock[key] || []).push(c);
    });

    Object.keys(byBlock).forEach(function (blockId) {
      var block = document.querySelector('[data-block-id="' + blockId + '"]');
      if (!block) return;
      var comments = byBlock[blockId];

      block.setAttribute("data-has-comments", "true");
      block.setAttribute("data-comment-count", String(comments.length));

      var anchor = block;
      comments.forEach(function (c, i) {
        var card = buildCard(c, i + 1);
        anchor.insertAdjacentElement("afterend", card);
        anchor = card;
      });
    });
  }

  function renderOrphans() {
    if (!data.orphaned || data.orphaned.length === 0) return;
    var main = document.querySelector("main") || document.body;
    var panel = document.createElement("div");
    panel.className = "sp-orphans";
    panel.setAttribute("role", "region");
    panel.setAttribute("aria-label", "Orphaned revision requests");

    var header = document.createElement("div");
    header.className = "sp-orphans-header";
    header.textContent = "Orphaned (" + data.orphaned.length + ") ▾";
    panel.appendChild(header);

    var list = document.createElement("ul");
    data.orphaned.forEach(function (c) {
      var li = document.createElement("li");
      li.innerHTML = "<strong>" + escapeHtml(c.blockAnchor.kind) + "</strong>: " +
        escapeHtml(c.blockAnchor.leadingText) + " — " +
        '<button type="button" data-action="delete" data-id="' + escapeHtml(c.id) + '">Delete</button> ' +
        '<button type="button" data-action="reanchor" data-id="' + escapeHtml(c.id) + '">Re-anchor manually</button>';
      list.appendChild(li);
    });
    panel.appendChild(list);

    list.addEventListener("click", function (e) {
      var btn = e.target.closest("button");
      if (!btn) return;
      var id = btn.getAttribute("data-id");
      var action = btn.getAttribute("data-action");
      if (action === "delete") post("commentDelete", { commentId: id });
      else if (action === "reanchor") beginReanchor(id);
    });

    main.insertBefore(panel, main.firstChild);
  }

  function beginReanchor(commentId) {
    document.body.classList.add("sp-reanchor-mode");
    function onClick(e) {
      var block = e.target.closest(".md-block");
      if (!block) return;
      e.preventDefault();
      e.stopPropagation();
      document.body.classList.remove("sp-reanchor-mode");
      document.removeEventListener("click", onClick, true);
      post("orphanReanchor", {
        commentId: commentId,
        blockId: block.getAttribute("data-block-id")
      });
    }
    document.addEventListener("click", onClick, true);
  }

  function wireBlockClicks() {
    var blocks = document.querySelectorAll(".md-block");
    var downAt = null;
    blocks.forEach(function (b) {
      b.addEventListener("mousedown", function (e) { downAt = { x: e.clientX, y: e.clientY }; });
      b.addEventListener("mouseup", function (e) {
        if (!downAt) return;
        var dx = e.clientX - downAt.x, dy = e.clientY - downAt.y;
        var moved = Math.sqrt(dx * dx + dy * dy);
        downAt = null;
        if (moved > 4) return;
        if (window.getSelection && String(window.getSelection())) return;
        if (document.body.classList.contains("sp-reanchor-mode")) return;
        startCompose(b.getAttribute("data-block-id"), null);
      });
      b.addEventListener("keydown", function (e) {
        if (e.key === "Enter") {
          e.preventDefault();
          startCompose(b.getAttribute("data-block-id"), null);
        }
      });
    });
  }

  function init() {
    renderOrphans();
    renderExistingComments();
    wireBlockClicks();
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", init);
  } else {
    init();
  }
})();
```

- [ ] **Step 2: Embed the asset in the csproj**

Modify `src/Spectacle/Spectacle.csproj`. Add another line to the existing `<ItemGroup>`:

```xml
<EmbeddedResource Include="Render\Assets\preview-annotations.js" />
```

- [ ] **Step 3: Verify build**

Run: `dotnet build C:\GIT\Spectacle\Spectacle.slnx`
Expected: success.

- [ ] **Step 4: Commit**

```bash
git add src/Spectacle/Render/Assets/preview-annotations.js src/Spectacle/Spectacle.csproj
git commit -m "feat(render): preview-annotations.js — composer, comment cards, orphan panel"
```

---

### Task 8: Extend `PreviewHtml.Build` to inject annotation assets and payload

**Files:**
- Modify: `src/Spectacle/Render/PreviewHtml.cs`
- Modify: `test/Spectacle.Tests/PreviewHtmlTests.cs`

- [ ] **Step 1: Add failing tests**

Append to `test/Spectacle.Tests/PreviewHtmlTests.cs`:

```csharp
    [Fact]
    public void Build_with_match_result_embeds_annotations_css()
    {
        var matched = new Spectacle.Annotations.MatchResult(
            System.Array.Empty<Spectacle.Annotations.MatchedComment>(),
            System.Array.Empty<Spectacle.Annotations.Comment>());
        var html = PreviewHtml.Build("<p>hi</p>", "https://h/", PreviewTheme.Dark, matched);

        html.Should().Contain(".md-block");
        html.Should().Contain(".sp-composer");
    }

    [Fact]
    public void Build_with_match_result_embeds_annotations_js()
    {
        var matched = new Spectacle.Annotations.MatchResult(
            System.Array.Empty<Spectacle.Annotations.MatchedComment>(),
            System.Array.Empty<Spectacle.Annotations.Comment>());
        var html = PreviewHtml.Build("", "x", PreviewTheme.Dark, matched);

        html.Should().Contain("__spectacleAnnotations__");
        html.Should().Contain("postMessage");
    }

    [Fact]
    public void Build_with_match_result_includes_matched_comments_in_payload()
    {
        var anchor = new Spectacle.Annotations.BlockAnchor("paragraph", 1, "h", 0, "lead");
        var c = new Spectacle.Annotations.Comment("c1", anchor, "orig", "rev",
            new System.DateTime(2026, 5, 15, 0, 0, 0, System.DateTimeKind.Utc), null);
        var b = new Spectacle.Render.TaggedBlock("b0", "paragraph", 1, "h", 0, "orig");
        var match = new Spectacle.Annotations.MatchedComment(c, b);
        var matched = new Spectacle.Annotations.MatchResult(
            new[] { match },
            System.Array.Empty<Spectacle.Annotations.Comment>());

        var html = PreviewHtml.Build("", "x", PreviewTheme.Dark, matched);

        html.Should().Contain("\"c1\"");
        html.Should().Contain("\"blockIdAtRender\":\"b0\"");
        html.Should().Contain("\"rev\"");
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test C:\GIT\Spectacle\Spectacle.slnx --filter "FullyQualifiedName~PreviewHtmlTests"`
Expected: 3 failures (no overload with `MatchResult`).

- [ ] **Step 3: Extend `PreviewHtml`**

Replace contents of `src/Spectacle/Render/PreviewHtml.cs`:

```csharp
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Spectacle.Annotations;

namespace Spectacle.Render;

public enum PreviewTheme { Dark, HighContrast }

public static class PreviewHtml
{
    private static readonly Lazy<string> PreviewCss = new(() => LoadAsset("preview.css"));
    private static readonly Lazy<string> DarkCss = new(() => LoadAsset("dark.css"));
    private static readonly Lazy<string> HcCss = new(() => LoadAsset("hc.css"));
    private static readonly Lazy<string> PrismCss = new(() => LoadAsset("prism.css"));
    private static readonly Lazy<string> PrismJs = new(() => LoadAsset("prism.min.js"));
    private static readonly Lazy<string> AnnotationsCss = new(() => LoadAsset("preview-annotations.css"));
    private static readonly Lazy<string> AnnotationsJs = new(() => LoadAsset("preview-annotations.js"));

    private static readonly JsonSerializerOptions PayloadOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string Build(string bodyHtml, string baseHref, PreviewTheme theme) =>
        Build(bodyHtml, baseHref, theme, matchResult: null);

    public static string Build(
        string bodyHtml, string baseHref, PreviewTheme theme, MatchResult? matchResult)
    {
        var themeCss = theme == PreviewTheme.HighContrast ? HcCss.Value : DarkCss.Value;
        var payloadJson = BuildPayload(matchResult);

        return $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
              <meta charset="utf-8" />
              <meta name="viewport" content="width=device-width,initial-scale=1" />
              <base href="{{baseHref}}" />
              <style>{{themeCss}}</style>
              <style>{{PreviewCss.Value}}</style>
              <style>{{PrismCss.Value}}</style>
              <style>{{AnnotationsCss.Value}}</style>
            </head>
            <body>
              <main role="main">
            {{bodyHtml}}
              </main>
              <script>{{PrismJs.Value}}</script>
              <script>window.__spectacleAnnotations__ = {{payloadJson}};</script>
              <script>{{AnnotationsJs.Value}}</script>
            </body>
            </html>
            """;
    }

    private static string BuildPayload(MatchResult? matchResult)
    {
        if (matchResult is null)
            return JsonSerializer.Serialize(new { comments = Array.Empty<object>(), orphaned = Array.Empty<object>() }, PayloadOpts);

        var comments = matchResult.Matched.Select(m => new
        {
            id = m.Comment.Id,
            body = m.Comment.Body,
            originalText = m.Comment.OriginalText,
            createdAt = m.Comment.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            resolvedAt = m.Comment.ResolvedAt?.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            blockAnchor = new
            {
                kind = m.Comment.BlockAnchor.Kind,
                line = m.CurrentBlock.Line,
                textHash = m.Comment.BlockAnchor.TextHash,
                occurrenceIndex = m.Comment.BlockAnchor.OccurrenceIndex,
                leadingText = m.Comment.BlockAnchor.LeadingText,
                blockIdAtRender = m.CurrentBlock.BlockId
            }
        });

        var orphans = matchResult.Orphaned.Select(c => new
        {
            id = c.Id,
            body = c.Body,
            blockAnchor = new
            {
                kind = c.BlockAnchor.Kind,
                line = c.BlockAnchor.Line,
                leadingText = c.BlockAnchor.LeadingText
            }
        });

        return JsonSerializer.Serialize(new { comments, orphaned = orphans }, PayloadOpts);
    }

    private static string LoadAsset(string name)
    {
        var asm = typeof(PreviewHtml).Assembly;
        var resource = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("." + name, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Embedded asset '{name}' not found.");
        using var s = asm.GetManifestResourceStream(resource)!;
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test C:\GIT\Spectacle\Spectacle.slnx --filter "FullyQualifiedName~PreviewHtmlTests"`
Expected: 9 passing (6 existing + 3 new).

- [ ] **Step 5: Commit**

```bash
git add src/Spectacle/Render/PreviewHtml.cs test/Spectacle.Tests/PreviewHtmlTests.cs
git commit -m "feat(render): inject annotation CSS, JS, and payload via PreviewHtml.Build overload"
```

---

### Task 9: Integrate annotations into `PreviewPipeline`

**Files:**
- Modify: `src/Spectacle/Render/PreviewPipeline.cs`
- Modify: `test/Spectacle.Tests/PreviewPipelineTests.cs`

The pipeline gains a sidecar store, an in-memory `AnnotationFile`, a public way to mutate it (called from host-side message handling), and a re-render hook.

- [ ] **Step 1: Add failing tests**

Replace contents of `test/Spectacle.Tests/PreviewPipelineTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using Spectacle.Annotations;
using Spectacle.Documents;
using Spectacle.Render;
using Xunit;

namespace Spectacle.Tests;

public class PreviewPipelineTests : IDisposable
{
    private readonly string _root;

    public PreviewPipelineTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"spectacle-pipe-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class StubDocument : Document
    {
        private string _text = "";
        public override string Text => _text;
        public override string BaseDirectory => @"C:\";
        public void Update(string text) { _text = text; OnChanged(); }
    }

    private sealed class StubSink : IPreviewSink
    {
        public List<string> Pushed { get; } = new();
        public void Push(string html) => Pushed.Add(html);
    }

    private PreviewPipeline NewPipeline(StubDocument doc, StubSink sink, string sourcePath = "")
    {
        var store = new AnnotationStore(
            sourcePath: string.IsNullOrEmpty(sourcePath) ? Path.Combine(_root, "doc.md") : sourcePath,
            sidecarRoot: _root);
        return new PreviewPipeline(doc, sink, PreviewTheme.Dark, store);
    }

    [Fact]
    public void Renders_initial_document_with_zero_annotations()
    {
        var doc = new StubDocument();
        doc.Update("# hello");
        var sink = new StubSink();

        using var p = NewPipeline(doc, sink);
        p.Start();

        sink.Pushed.Should().HaveCount(1);
        sink.Pushed[0].Should().Contain("<h1");
        sink.Pushed[0].Should().Contain("\"comments\":[]");
    }

    [Fact]
    public void Re_renders_on_document_change()
    {
        var doc = new StubDocument();
        doc.Update("# a");
        var sink = new StubSink();
        using var p = NewPipeline(doc, sink);
        p.Start();

        doc.Update("# b");

        sink.Pushed.Should().HaveCount(2);
    }

    [Fact]
    public void HandleHostMessage_saves_new_comment_and_refreshes()
    {
        var doc = new StubDocument();
        doc.Update("Hello world.\n");
        var sink = new StubSink();
        using var p = NewPipeline(doc, sink);
        p.Start();

        var msg = """
        {"type":"commentSave","commentId":"c-new","blockId":"b0","body":"reword"}
        """;
        p.HandleHostMessage(msg);

        sink.Pushed.Last().Should().Contain("\"c-new\"");
        sink.Pushed.Last().Should().Contain("\"reword\"");
    }

    [Fact]
    public void HandleHostMessage_deletes_comment()
    {
        var doc = new StubDocument();
        doc.Update("Hello world.\n");
        var sink = new StubSink();
        using var p = NewPipeline(doc, sink);
        p.Start();
        p.HandleHostMessage("""
        {"type":"commentSave","commentId":"c-1","blockId":"b0","body":"x"}
        """);

        p.HandleHostMessage("""
        {"type":"commentDelete","commentId":"c-1"}
        """);

        sink.Pushed.Last().Should().NotContain("\"c-1\"");
    }

    [Fact]
    public void HandleHostMessage_resolves_comment()
    {
        var doc = new StubDocument();
        doc.Update("Hello world.\n");
        var sink = new StubSink();
        using var p = NewPipeline(doc, sink);
        p.Start();
        p.HandleHostMessage("""
        {"type":"commentSave","commentId":"c-1","blockId":"b0","body":"x"}
        """);

        p.HandleHostMessage("""
        {"type":"commentResolve","commentId":"c-1","resolved":true}
        """);

        sink.Pushed.Last().Should().Contain("\"resolvedAt\":\"");
    }

    [Fact]
    public void Snapshot_returns_current_matched_comments()
    {
        var doc = new StubDocument();
        doc.Update("Hello.\n");
        var sink = new StubSink();
        using var p = NewPipeline(doc, sink);
        p.Start();
        p.HandleHostMessage("""
        {"type":"commentSave","commentId":"c-1","blockId":"b0","body":"x"}
        """);

        var snap = p.SnapshotMatched();
        snap.Should().ContainSingle().Which.Comment.Id.Should().Be("c-1");
    }

    [Fact]
    public void Comment_becomes_orphan_when_anchor_block_text_changes()
    {
        var doc = new StubDocument();
        doc.Update("Hello.\n");
        var sink = new StubSink();
        using var p = NewPipeline(doc, sink);
        p.Start();
        p.HandleHostMessage("""
        {"type":"commentSave","commentId":"c-1","blockId":"b0","body":"x"}
        """);

        doc.Update("Goodbye.\n");

        sink.Pushed.Last().Should().Contain("\"orphaned\":[")
            .And.Contain("\"c-1\"");
        sink.Pushed.Last().Should().NotContain("\"blockIdAtRender\":\"b0\",\"");
    }

    [Fact]
    public void OrphanReanchor_binds_comment_to_new_block()
    {
        var doc = new StubDocument();
        doc.Update("Hello.\n");
        var sink = new StubSink();
        using var p = NewPipeline(doc, sink);
        p.Start();
        p.HandleHostMessage("""
        {"type":"commentSave","commentId":"c-1","blockId":"b0","body":"x"}
        """);
        doc.Update("Goodbye.\n");

        p.HandleHostMessage("""
        {"type":"orphanReanchor","commentId":"c-1","blockId":"b0"}
        """);

        sink.Pushed.Last().Should().Contain("\"c-1\"");
        sink.Pushed.Last().Should().NotContain("\"orphaned\":[{\"id\":\"c-1\"");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test C:\GIT\Spectacle\Spectacle.slnx --filter "FullyQualifiedName~PreviewPipelineTests"`
Expected: build failures (`HandleHostMessage`, `SnapshotMatched`, new constructor signature).

- [ ] **Step 3: Rewrite `PreviewPipeline`**

Replace contents of `src/Spectacle/Render/PreviewPipeline.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Spectacle.Annotations;
using Spectacle.Documents;

namespace Spectacle.Render;

public interface IPreviewSink
{
    void Push(string html);
}

public sealed class PreviewPipeline : IDisposable
{
    private readonly Document _document;
    private readonly IPreviewSink _sink;
    private readonly MdRenderer _renderer = new();
    private readonly AnnotationStore _store;
    private PreviewTheme _theme;
    private bool _started;
    private AnnotationFile _file;
    private RenderResult? _lastRender;
    private MatchResult? _lastMatch;

    public PreviewPipeline(Document document, IPreviewSink sink, PreviewTheme theme, AnnotationStore store)
    {
        _document = document;
        _sink = sink;
        _theme = theme;
        _store = store;
        _file = _store.Load();
    }

    public void Start()
    {
        if (_started) return;
        _started = true;
        _document.Changed += OnDocumentChanged;
        Render();
    }

    public void SetTheme(PreviewTheme theme)
    {
        _theme = theme;
        if (_started) Render();
    }

    public IReadOnlyList<MatchedComment> SnapshotMatched() =>
        _lastMatch?.Matched ?? Array.Empty<MatchedComment>();

    public string CurrentSourceText => _document.Text;
    public string CurrentSourcePath => _store.SidecarPath; // for display only

    public void HandleHostMessage(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("type", out var typeEl)) return;
        var type = typeEl.GetString();

        switch (type)
        {
            case "commentSave":    OnCommentSave(root); break;
            case "commentDelete":  OnCommentDelete(root); break;
            case "commentResolve": OnCommentResolve(root); break;
            case "orphanReanchor": OnOrphanReanchor(root); break;
            default: return;
        }
        Persist();
        Render();
    }

    private void OnCommentSave(JsonElement root)
    {
        var commentId = root.GetProperty("commentId").GetString()!;
        var blockId = root.GetProperty("blockId").GetString()!;
        var body = root.GetProperty("body").GetString()!;

        var block = (_lastRender?.Blocks ?? Array.Empty<TaggedBlock>())
            .FirstOrDefault(b => b.BlockId == blockId);
        if (block is null) return;

        var anchor = new BlockAnchor(
            Kind: block.Kind,
            Line: block.Line,
            TextHash: block.TextHash,
            OccurrenceIndex: block.OccurrenceIndex,
            LeadingText: block.OriginalText.Split('\n')[0] is var first && first.Length > 80
                ? first.Substring(0, 80) : first);

        var existing = _file.Comments.FirstOrDefault(c => c.Id == commentId);
        Comment updated;
        if (existing is not null)
        {
            updated = existing with { Body = body, BlockAnchor = anchor, OriginalText = block.OriginalText };
            _file = _file with { Comments = _file.Comments.Select(c => c.Id == commentId ? updated : c).ToArray() };
        }
        else
        {
            updated = new Comment(
                Id: commentId,
                BlockAnchor: anchor,
                OriginalText: block.OriginalText,
                Body: body,
                CreatedAt: DateTime.UtcNow,
                ResolvedAt: null);
            _file = _file with { Comments = _file.Comments.Concat(new[] { updated }).ToArray() };
        }
    }

    private void OnCommentDelete(JsonElement root)
    {
        var commentId = root.GetProperty("commentId").GetString()!;
        _file = _file with { Comments = _file.Comments.Where(c => c.Id != commentId).ToArray() };
    }

    private void OnCommentResolve(JsonElement root)
    {
        var commentId = root.GetProperty("commentId").GetString()!;
        var resolved = root.GetProperty("resolved").GetBoolean();
        _file = _file with
        {
            Comments = _file.Comments.Select(c =>
                c.Id == commentId ? c with { ResolvedAt = resolved ? DateTime.UtcNow : null } : c
            ).ToArray()
        };
    }

    private void OnOrphanReanchor(JsonElement root)
    {
        var commentId = root.GetProperty("commentId").GetString()!;
        var blockId = root.GetProperty("blockId").GetString()!;
        var block = (_lastRender?.Blocks ?? Array.Empty<TaggedBlock>())
            .FirstOrDefault(b => b.BlockId == blockId);
        if (block is null) return;

        var newAnchor = new BlockAnchor(
            Kind: block.Kind,
            Line: block.Line,
            TextHash: block.TextHash,
            OccurrenceIndex: block.OccurrenceIndex,
            LeadingText: block.OriginalText.Split('\n')[0] is var first && first.Length > 80
                ? first.Substring(0, 80) : first);

        _file = _file with
        {
            Comments = _file.Comments.Select(c =>
                c.Id == commentId ? c with { BlockAnchor = newAnchor, OriginalText = block.OriginalText } : c
            ).ToArray()
        };
    }

    private void Persist() => _store.Save(_file);

    private void OnDocumentChanged(object? sender, EventArgs e) => Render();

    private void Render()
    {
        _lastRender = _renderer.Render(_document.Text);
        _lastMatch = AnnotationMatcher.Match(_lastRender.Blocks, _file.Comments);
        var html = PreviewHtml.Build(
            _lastRender.Html,
            $"https://{Web.WebViewHost.VirtualHost}/",
            _theme,
            _lastMatch);
        _sink.Push(html);
    }

    public void Dispose() => _document.Changed -= OnDocumentChanged;
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test C:\GIT\Spectacle\Spectacle.slnx --filter "FullyQualifiedName~PreviewPipelineTests"`
Expected: 8 passing.

- [ ] **Step 5: Build the whole solution to surface any callers**

Run: `dotnet build C:\GIT\Spectacle\Spectacle.slnx`
Expected: failure in `MainWindow.xaml.cs` — `PreviewPipeline` constructor signature changed.

- [ ] **Step 6: Fix `MainWindow.xaml.cs` to construct the new pipeline**

In `src/Spectacle/MainWindow.xaml.cs`, modify the constructor. After `_document = FileDocument.Open(filePath);`, add a store and pass it through. The relevant region becomes:

```csharp
_document = FileDocument.Open(filePath);
Title = $"{System.IO.Path.GetFileName(filePath)} — Spectacle";
Web.SetVirtualFolder(_document.BaseDirectory);

var theme = _hcWatcher.IsActive ? PreviewTheme.HighContrast : PreviewTheme.Dark;
_store = new Spectacle.Annotations.AnnotationStore(filePath);
_pipeline = new PreviewPipeline(_document, this, theme, _store);
```

Add the field next to the existing `_document` / `_pipeline` fields:

```csharp
private readonly Spectacle.Annotations.AnnotationStore _store;
```

- [ ] **Step 7: Run full build + all tests**

Run: `dotnet build C:\GIT\Spectacle\Spectacle.slnx`
Run: `dotnet test C:\GIT\Spectacle\Spectacle.slnx`
Expected: all green.

- [ ] **Step 8: Commit**

```bash
git add src/Spectacle/Render/PreviewPipeline.cs src/Spectacle/MainWindow.xaml.cs test/Spectacle.Tests/PreviewPipelineTests.cs
git commit -m "feat(render): PreviewPipeline owns AnnotationStore and dispatches host messages"
```

---

### Task 10: WebViewHost — expose `HostMessageReceived` event

**Files:**
- Modify: `src/Spectacle/Web/WebViewHost.xaml.cs`

The WebView already initializes `CoreWebView2`. We add a `WebMessageReceived` subscription that forwards the JSON payload to subscribers.

- [ ] **Step 1: Add the event and wire it**

Edit `src/Spectacle/Web/WebViewHost.xaml.cs`. Inside the `WebViewHost` class, add an event field and forward messages.

After the `_virtualFolder` field, add:

```csharp
public event EventHandler<string>? HostMessageReceived;
```

In `InitializeAsync()`, after the existing subscriptions, add the message subscription:

```csharp
Web.CoreWebView2.WebMessageReceived += (_, e) =>
{
    // WebView2 prefers the JSON payload via TryGetWebMessageAsString when posted with postMessage(JSON.stringify(...)).
    var json = e.TryGetWebMessageAsString();
    if (!string.IsNullOrEmpty(json))
        HostMessageReceived?.Invoke(this, json);
};
```

The complete `InitializeAsync` method now reads:

```csharp
private async Task InitializeAsync()
{
    await Web.EnsureCoreWebView2Async();
    Web.CoreWebView2.Settings.AreDevToolsEnabled = false;
    Web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
    Web.CoreWebView2.NewWindowRequested += OnNewWindow;
    Web.CoreWebView2.NavigationStarting += OnNavStarting;
    Web.CoreWebView2.WebMessageReceived += (_, e) =>
    {
        var json = e.TryGetWebMessageAsString();
        if (!string.IsNullOrEmpty(json))
            HostMessageReceived?.Invoke(this, json);
    };
    _ready = true;
    if (_pendingHtml is not null) DoSetHtml(_pendingHtml);
}
```

- [ ] **Step 2: Build the solution**

Run: `dotnet build C:\GIT\Spectacle\Spectacle.slnx`
Expected: success (the event is unused for now — Task 11 will subscribe to it).

- [ ] **Step 3: Commit**

```bash
git add src/Spectacle/Web/WebViewHost.xaml.cs
git commit -m "feat(web): forward WebMessageReceived as HostMessageReceived to the host"
```

---

### Task 11: MainWindow top bar — Copy revision plan / Export / status

**Files:**
- Modify: `src/Spectacle/MainWindow.xaml`
- Modify: `src/Spectacle/MainWindow.xaml.cs`

- [ ] **Step 1: Add the top bar to the XAML**

Replace the `<Grid>` block in `src/Spectacle/MainWindow.xaml` with:

```xml
<Grid>
    <Grid.RowDefinitions>
        <RowDefinition Height="Auto" />
        <RowDefinition Height="*" />
    </Grid.RowDefinitions>
    <Border x:Name="TopBar" Grid.Row="0"
            Background="#252526" BorderBrush="#3c3c3c" BorderThickness="0,0,0,1"
            Padding="10,6" Visibility="Collapsed">
        <StackPanel Orientation="Horizontal">
            <Button Content="Copy revision plan" Command="{Binding CopyRevisionPlanCommand}"
                    Padding="10,4" Margin="0,0,8,0" Background="#4ea1ff" Foreground="#0a0a0a"
                    BorderThickness="0" />
            <Button Content="Export revision plan…" Command="{Binding ExportRevisionPlanCommand}"
                    Padding="10,4" Margin="0,0,16,0" Background="#1c1c1c" Foreground="#d4d4d4"
                    BorderBrush="#3c3c3c" BorderThickness="1" />
            <TextBlock x:Name="StatusText" VerticalAlignment="Center" Foreground="#9aa0a6" Text="" />
        </StackPanel>
    </Border>
    <Grid Grid.Row="1">
        <Grid.ColumnDefinitions>
            <ColumnDefinition x:Name="EditorColumn" Width="0" />
            <ColumnDefinition x:Name="SplitterColumn" Width="0" />
            <ColumnDefinition Width="*" />
        </Grid.ColumnDefinitions>
        <editor:EditorHost x:Name="Editor" Grid.Column="0" />
        <GridSplitter x:Name="Splitter" Grid.Column="1" Width="0" Visibility="Collapsed"
                      HorizontalAlignment="Stretch" Background="#3c3c3c" />
        <web:WebViewHost x:Name="Web" Grid.Column="2" />
    </Grid>
</Grid>
```

- [ ] **Step 2: Add the wiring in code-behind**

Edit `src/Spectacle/MainWindow.xaml.cs`.

Add usings at the top:

```csharp
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Controls;
using Microsoft.Win32;
using Spectacle.Annotations;
```

Add new command properties (next to the existing `ICommand` declarations):

```csharp
public ICommand CopyRevisionPlanCommand { get; }
public ICommand ExportRevisionPlanCommand { get; }
```

Add the `_sourcePath` field next to the other `private readonly` fields:

```csharp
private readonly string _sourcePath;
```

In the constructor body, **first line** (before `_document = FileDocument.Open(filePath);`):

```csharp
_sourcePath = System.IO.Path.GetFullPath(filePath);
```

In the constructor, after the existing command initializations and before `DataContext = this;`, wire the new commands and the host-message handler:

```csharp
CopyRevisionPlanCommand = new RelayCommand(_ => CopyRevisionPlan());
ExportRevisionPlanCommand = new RelayCommand(_ => ExportRevisionPlan());

Web.HostMessageReceived += (_, json) => Dispatcher.Invoke(() =>
{
    _pipeline.HandleHostMessage(json);
    UpdateTopBar();
});
```

Replace the existing `Loaded` lambda with one that also updates the top bar after the first render:

```csharp
Loaded += (_, _) => { _pipeline.Start(); UpdateTopBar(); };
```

Add these helper methods inside the class (next to the existing private methods):

```csharp
private void UpdateTopBar()
{
    var matched = _pipeline.SnapshotMatched();
    var loaded = _store.Load();
    var orphanCount = loaded.Comments.Count - matched.Count;

    if (matched.Count + orphanCount == 0)
    {
        TopBar.Visibility = System.Windows.Visibility.Collapsed;
        StatusText.Text = "";
        return;
    }
    TopBar.Visibility = System.Windows.Visibility.Visible;
    StatusText.Text = orphanCount > 0
        ? $"{matched.Count} comment(s) • {orphanCount} orphaned"
        : $"{matched.Count} comment(s)";
}

private string BuildRevisionPlan()
{
    var matched = _pipeline.SnapshotMatched();
    var content = File.ReadAllText(_sourcePath);
    var sha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
    return RevisionPlanExporter.Build(_sourcePath, sha, DateTime.UtcNow, matched);
}

private void CopyRevisionPlan()
{
    var text = BuildRevisionPlan();
    System.Windows.Clipboard.SetText(text);
}

private void ExportRevisionPlan()
{
    var text = BuildRevisionPlan();
    var dlg = new SaveFileDialog
    {
        FileName = System.IO.Path.GetFileNameWithoutExtension(_sourcePath) + ".revisions.md",
        Filter = "Markdown (*.md)|*.md|All files (*.*)|*.*",
        InitialDirectory = System.IO.Path.GetDirectoryName(_sourcePath)
    };
    if (dlg.ShowDialog() == true)
        File.WriteAllText(dlg.FileName, text);
}
```

- [ ] **Step 3: Build and run tests**

Run: `dotnet build C:\GIT\Spectacle\Spectacle.slnx`
Run: `dotnet test C:\GIT\Spectacle\Spectacle.slnx`
Expected: build success, all tests green.

- [ ] **Step 4: Commit**

```bash
git add src/Spectacle/MainWindow.xaml src/Spectacle/MainWindow.xaml.cs
git commit -m "feat(window): top bar with Copy / Export revision plan and count status"
```

---

### Task 12: Manual smoke verification

This task is human-in-the-loop — Spectacle requires a desktop / WebView2 environment and cannot be exercised by automated tests for the UI parts. Build a publish output and walk through the spec's smoke checklist.

- [ ] **Step 1: Publish a binary**

Run:

```bash
dotnet publish C:\GIT\Spectacle\src\Spectacle -p:PublishProfile=win-x64
```

Output path: `publish/win-x64/Spectacle.exe`.

- [ ] **Step 2: Run through the smoke checklist (spec §11)**

Open a markdown file (e.g., `docs/superpowers/specs/2026-05-15-md-preview-annotations-design.md`):

```bash
publish/win-x64/Spectacle.exe docs/superpowers/specs/2026-05-15-md-preview-annotations-design.md
```

Verify, ticking each:

- [ ] Hover over a paragraph → 3px accent left-border appears.
- [ ] Click a paragraph → composer card opens below it with focused textarea.
- [ ] Type instruction → press **Save**. Composer collapses into a saved card; `💬 1` badge appears on the block.
- [ ] Close Spectacle. Reopen the same file. The saved comment is still there.
- [ ] Edit the source `.md` externally to change the commented block's text. Save. Spectacle auto-reloads. The comment appears in the **Orphaned** panel at the top.
- [ ] Click **Re-anchor manually** on the orphan; click a current block. The orphan re-binds (no longer in the orphan list).
- [ ] Add three comments across three different blocks. Click **Copy revision plan**. Paste into an LLM chat. Verify the LLM can apply all three revisions.
- [ ] Tab through the document — each `.md-block` gets focus in order; `Enter` opens the composer.
- [ ] Toggle Windows Contrast Themes (Settings → Accessibility → Contrast themes). The annotation UI engages the forced-colors palette.
- [ ] Run Narrator (`Win+Ctrl+Enter`). Comment cards announce as "Revision request N on paragraph at line M".
- [ ] Open a `.txt` file — confirm FileGuard rejection still works (regression check).
- [ ] Click an external link in the preview — opens in default browser, not in Spectacle (regression check).

- [ ] **Step 3: Commit the smoke evidence**

If any defects surface during smoke, file follow-up commits to fix them. Once green:

```bash
# No code change to commit; tag the work or move on.
git log -1 --oneline
```

---

## Self-Review

This section is internal to the plan author. The implementer should ignore it.

**Spec coverage:**

| Spec section | Task(s) implementing it |
|---|---|
| §1.1 reconcile non-goal | Stated in plan header + Task 9 (`AnnotationStore` is sidecar-only — never touches `.md`). |
| §2 revision plan format | Task 5. |
| §3 inline-after-block + sidecar JSON | Tasks 2, 7, 9. |
| §4 block identity (kind/line/textHash/occurrenceIndex/leadingText) | Tasks 1, 3. |
| §4.1 strict matching + orphan handling | Task 4. |
| §5 UI behavior (hover, click, composer, badge, top bar) | Tasks 6, 7, 11. |
| §6 architecture units | All. |
| §6.2 sidecar JSON schema | Tasks 1, 2. |
| §6.3 sidecar path | Task 2. |
| §7 non-goals | No tasks needed — the absence of features is enforced by the plan's narrow scope. |
| §8 accessibility (tabindex, focus, ARIA, badge) | Tasks 6, 7. |
| §9 keyboard shortcuts (`Tab`, `Enter`, `Esc`, `Ctrl+Enter`) | Task 7. |
| §10 persistence / lifecycle | Tasks 2, 9. |
| §11 testing | Tasks 1, 2, 3, 4, 5, 8, 9, 12. |
| §12 risks: identical-text disambiguation | Task 3 (occurrence-index), Task 4 (matcher tests cover the case). |
| §12 risks: line-ending normalization | Task 3 (`NormalizeText` + dedicated test). |
| §12 risks: atomic write | Task 2 (test asserts no `.tmp` remains). |
| §12 risks: corrupt sidecar | Task 2 (test asserts rename to `.corrupt-*` and empty load). |
| §13 defaults | All defaults are encoded in the task implementations. |

**Type consistency check:** `BlockAnchor` properties match across Tasks 1, 2, 3, 4, 5, 7, 9. `TaggedBlock` shape is identical between Render and Annotations namespaces (the Annotations namespace imports it from Render). `MatchedComment` and `MatchResult` defined in Task 4 are consumed unchanged by Tasks 5, 8, 9. `HandleHostMessage` JSON payloads are identical between Task 7 (producer) and Task 9 (consumer): `commentSave {commentId, blockId, body}`, `commentDelete {commentId}`, `commentResolve {commentId, resolved}`, `orphanReanchor {commentId, blockId}`.

**Placeholder scan:** no `TBD`, `TODO`, `implement later`, or "similar to Task N" references. Every code step shows the actual code. Manual smoke (Task 12) is explicit human verification, not a placeholder for code.

---
