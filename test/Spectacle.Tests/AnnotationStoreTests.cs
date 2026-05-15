using System;
using System.IO;
using System.Linq;
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

    [Fact]
    public void Load_writes_corruption_message_to_stderr()
    {
        var store = new AnnotationStore(_sourceFile, sidecarRoot: _root);
        Directory.CreateDirectory(store.SidecarDirectory);
        File.WriteAllText(store.SidecarPath, "{ not valid json");

        var stderr = Console.Error;
        using var sw = new StringWriter();
        Console.SetError(sw);
        try { store.Load(); }
        finally { Console.SetError(stderr); }

        sw.ToString().Should().Contain("Corrupt sidecar")
                      .And.Contain(store.SidecarPath);
    }

    [Fact]
    public void Load_rethrows_on_transient_io_failure()
    {
        var store = new AnnotationStore(_sourceFile, sidecarRoot: _root);
        Directory.CreateDirectory(store.SidecarDirectory);

        // Open the sidecar with an exclusive lock so File.ReadAllText hits a sharing violation.
        using var fs = new FileStream(store.SidecarPath, FileMode.Create,
            FileAccess.Write, FileShare.None);
        fs.WriteByte((byte)'x');
        fs.Flush();

        Action act = () => store.Load();
        act.Should().Throw<IOException>();
    }

    [Fact]
    public void Saved_json_uses_camelCase_keys_per_spec_section_6_2()
    {
        var store = new AnnotationStore(_sourceFile, sidecarRoot: _root);
        var anchor = new BlockAnchor("paragraph", 1, "h", 0, "lead");
        var comment = new Comment("c1", anchor, "Hi.", "rename",
            new DateTime(2026, 5, 15, 10, 0, 0, DateTimeKind.Utc), null);

        store.Save(new AnnotationFile(1, _sourceFile, "src-hash", new[] { comment }));

        var json = File.ReadAllText(store.SidecarPath);
        json.Should().Contain("\"fileVersion\":");
        json.Should().Contain("\"sourcePath\":");
        json.Should().Contain("\"sourceHashAtWrite\":");
        json.Should().Contain("\"comments\":");
        json.Should().Contain("\"blockAnchor\":");
        json.Should().Contain("\"textHash\":");
        json.Should().Contain("\"occurrenceIndex\":");
        json.Should().Contain("\"leadingText\":");
        json.Should().Contain("\"originalText\":");
        json.Should().Contain("\"createdAt\":");
        json.Should().Contain("\"resolvedAt\":");
    }
}
