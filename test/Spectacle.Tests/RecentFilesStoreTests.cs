using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Spectacle.Files;
using Xunit;

namespace Spectacle.Tests;

public class RecentFilesStoreTests : IDisposable
{
    private readonly string _root;
    private readonly string _storePath;

    public RecentFilesStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"spectacle-recent-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _storePath = Path.Combine(_root, "recent.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private RecentFilesStore NewStore(int capacity = RecentFilesStore.DefaultCapacity) =>
        new(_storePath, capacity);

    private string TouchFile(string name)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, "# doc");
        return path;
    }

    [Fact]
    public void Load_returns_empty_when_store_missing() =>
        NewStore().Load().Should().BeEmpty();

    [Fact]
    public void Add_then_load_returns_the_entry()
    {
        var store = NewStore();
        var doc = TouchFile("a.md");

        store.Add(doc);

        store.Load().Should().ContainSingle().Which.Should().Be(Path.GetFullPath(doc));
    }

    [Fact]
    public void Add_normalizes_to_absolute_path()
    {
        var store = NewStore();

        store.Add("relative.md");

        store.Load().Single().Should().Be(Path.GetFullPath("relative.md"));
    }

    [Fact]
    public void Most_recently_added_comes_first()
    {
        var store = NewStore();
        var a = TouchFile("a.md");
        var b = TouchFile("b.md");

        store.Add(a);
        store.Add(b);

        store.Load().Should().Equal(Path.GetFullPath(b), Path.GetFullPath(a));
    }

    [Fact]
    public void Re_adding_moves_entry_to_front_without_duplicating()
    {
        var store = NewStore();
        var a = TouchFile("a.md");
        var b = TouchFile("b.md");

        store.Add(a);
        store.Add(b);
        store.Add(a);

        store.Load().Should().Equal(Path.GetFullPath(a), Path.GetFullPath(b));
    }

    [Fact]
    public void Duplicate_detection_is_case_insensitive()
    {
        var store = NewStore();
        var lower = Path.Combine(_root, "doc.md");

        store.Add(lower);
        store.Add(lower.ToUpperInvariant());

        store.Load().Should().ContainSingle();
    }

    [Fact]
    public void List_is_capped_at_capacity_keeping_newest()
    {
        var store = NewStore(capacity: 3);

        store.Add(Path.Combine(_root, "1.md"));
        store.Add(Path.Combine(_root, "2.md"));
        store.Add(Path.Combine(_root, "3.md"));
        store.Add(Path.Combine(_root, "4.md"));

        var loaded = store.Load();
        loaded.Should().HaveCount(3);
        loaded[0].Should().Be(Path.GetFullPath(Path.Combine(_root, "4.md")));
        loaded.Should().NotContain(Path.GetFullPath(Path.Combine(_root, "1.md")));
    }

    [Fact]
    public void LoadExisting_drops_entries_whose_file_was_deleted()
    {
        var store = NewStore();
        var present = TouchFile("present.md");
        var gone = TouchFile("gone.md");

        store.Add(present);
        store.Add(gone);
        File.Delete(gone);

        store.LoadExisting().Should().Equal(Path.GetFullPath(present));
        // Load (unfiltered) still has both — pruning is a view, not a mutation.
        store.Load().Should().HaveCount(2);
    }

    [Fact]
    public void Clear_empties_the_list()
    {
        var store = NewStore();
        store.Add(TouchFile("a.md"));

        store.Clear();

        store.Load().Should().BeEmpty();
    }

    [Fact]
    public void Add_ignores_null_or_whitespace()
    {
        var store = NewStore();

        store.Add("");
        store.Add("   ");

        store.Load().Should().BeEmpty();
    }

    [Fact]
    public void Corrupt_store_reads_back_as_empty()
    {
        File.WriteAllText(_storePath, "{ this is not valid json");

        NewStore().Load().Should().BeEmpty();
    }

    [Fact]
    public void Add_creates_the_store_directory_if_missing()
    {
        var nested = Path.Combine(_root, "nested", "dir", "recent.json");
        var store = new RecentFilesStore(nested);

        store.Add(TouchFile("a.md"));

        File.Exists(nested).Should().BeTrue();
        store.Load().Should().ContainSingle();
    }
}
