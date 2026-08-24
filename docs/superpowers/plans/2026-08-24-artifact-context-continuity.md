# Cross-Session Artifact Continuity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make a Markdown artifact carry its own cross-session memory, so a brand-new `claude -p` process launched by the viewer continues prior work correctly from the file alone.

**Architecture:** A reserved `artifact_context` front-matter namespace holds the compressed durable state of every prior session. Spectacle never writes it — it resolves the correct Claude project root, reads and validates the namespace, and carries merge-and-recompress instructions in the prompt. One service (`ClaudeArtifactRevisionService`) is the only path from the viewer to `claude -p`.

**Tech Stack:** C# 13 / .NET 10 (`net10.0-windows`, WPF), xUnit 2.9.3, FluentAssertions 7.2.2. Nullable enabled, `TreatWarningsAsErrors=true`.

**Spec:** `docs/superpowers/specs/2026-08-24-artifact-context-continuity-design.md`

> **Amended after implementation (2026-08-24).** Tasks 4 and 5 below describe a *refusal*: a
> managed artifact with no resolvable project root aborts with `AbortMessage`. That was reversed
> during execution once the premise behind it proved wrong — Claude Code loads the user-scope
> `~/.claude` configuration whatever the working directory is, so a fallback run is governed by
> less, not by nothing. The shipped behavior runs anyway from the document's folder and names the
> scope on the chip (`user scope only — no project root for this artifact`). `AbortMessage` and
> `ArtifactRevisionStatus.Refused` do not exist; `Revise` takes an `onLaunching` callback instead.
> The design doc's **Fallback rule** section carries the current rule and the reasoning. Everything
> else in this plan matches what shipped.

## Global Constraints

- **Never write YAML from C#.** `FrontMatter` stays read-only. The agent writes `artifact_context`; Spectacle reads it and instructs.
- **Never use `--bare`.** `ClaudeRevisionRunner.BuildStartInfo`'s argument string stays exactly `-p --output-format stream-json --verbose --permission-mode acceptEdits`.
- **`TreatWarningsAsErrors=true`.** An unused `using`, a nullable warning, or a missing XML nullability annotation fails the build. Build before claiming a task done.
- **Nullable reference types are enabled** project-wide. Annotate accordingly.
- **Test project keeps its surface public** rather than using `InternalsVisibleTo` — anything a test asserts must be `public`.
- **No commits.** Per repository owner's standing instruction, stage completed work with `git add` and stop. Do not `git commit`, `git merge`, or `git push`. No AI attribution anywhere.
- **Verification commands** (run from the repository root, `C:\GIT\Spectacle`):
  - Build: `dotnet build test/Spectacle.Tests/Spectacle.Tests.csproj`
  - Full suite: `dotnet test test/Spectacle.Tests/Spectacle.Tests.csproj`
  - One class: `dotnet test test/Spectacle.Tests/Spectacle.Tests.csproj --filter "FullyQualifiedName~ClaudeProjectRootTests"`

---

### Task 1: `ClaudeProjectRoot` — resolve the launch directory

**Files:**
- Create: `src/Spectacle/Ai/ClaudeProjectRoot.cs`
- Test: `test/Spectacle.Tests/ClaudeProjectRootTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces:
  - `Spectacle.Ai.ClaudeProjectRootResult(string? Path, string Reason)` — record.
  - `Spectacle.Ai.ClaudeProjectRoot.Resolve(string startDirectory)` → `ClaudeProjectRootResult`
  - `Spectacle.Ai.ClaudeProjectRoot.Resolve(string startDirectory, string? userProfile, Func<string,bool> fileExists, Func<string,bool> directoryExists)` → `ClaudeProjectRootResult`

- [x] **Step 1: Write the failing tests**

Create `test/Spectacle.Tests/ClaudeProjectRootTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using FluentAssertions;
using Spectacle.Ai;
using Xunit;

namespace Spectacle.Tests;

public class ClaudeProjectRootTests
{
    private const string Home = @"C:\Users\dev";

    // A fake filesystem: every path listed exists. Directories are listed without a trailing
    // separator; the probes below compare case-insensitively, as Windows does.
    private static (Func<string, bool> Files, Func<string, bool> Dirs) Fs(
        IEnumerable<string> files, IEnumerable<string> dirs)
    {
        var f = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
        var d = new HashSet<string>(dirs, StringComparer.OrdinalIgnoreCase);
        return (p => f.Contains(p), p => d.Contains(p));
    }

    [Fact]
    public void The_repository_root_wins_over_a_nested_marker()
    {
        // A docs/.claude holding one narrow setting must not shadow the repository root: launching
        // there would silently drop the root's CLAUDE.md, settings, rules and hooks.
        var fs = Fs(
            files: new[] { @"C:\repo\CLAUDE.md" },
            dirs: new[] { @"C:\repo\.git", @"C:\repo\.claude", @"C:\repo\docs\.claude" });

        var result = ClaudeProjectRoot.Resolve(@"C:\repo\docs", Home, fs.Files, fs.Dirs);

        result.Path.Should().Be(@"C:\repo");
    }

    [Fact]
    public void A_nested_marker_is_the_root_when_the_repository_root_has_none()
    {
        // Outermost *marker-bearing* ancestor — the git root itself needs no marker of its own.
        var fs = Fs(
            files: new[] { @"C:\repo\projects\argus\CLAUDE.md" },
            dirs: new[] { @"C:\repo\.git" });

        var result = ClaudeProjectRoot.Resolve(@"C:\repo\projects\argus\docs", Home, fs.Files, fs.Dirs);

        result.Path.Should().Be(@"C:\repo\projects\argus");
    }

    [Fact]
    public void The_walk_stops_at_the_git_root()
    {
        // A parent checkout's .claude has nothing to do with this artifact.
        var fs = Fs(
            files: new[] { @"C:\work\CLAUDE.md", @"C:\work\inner\CLAUDE.md" },
            dirs: new[] { @"C:\work\inner\.git" });

        var result = ClaudeProjectRoot.Resolve(@"C:\work\inner\docs", Home, fs.Files, fs.Dirs);

        result.Path.Should().Be(@"C:\work\inner");
    }

    [Fact]
    public void The_home_directory_is_never_a_project_root()
    {
        // ~/.claude is user scope, present on nearly every machine. Selecting it would report a
        // resolved project root while loading no project configuration at all.
        var fs = Fs(files: Array.Empty<string>(), dirs: new[] { @"C:\Users\dev\.claude" });

        var result = ClaudeProjectRoot.Resolve(@"C:\Users\dev\notes", Home, fs.Files, fs.Dirs);

        result.Path.Should().BeNull();
        result.Reason.Should().Contain("no CLAUDE.md or .claude");
    }

    [Fact]
    public void A_marker_below_the_home_directory_still_resolves()
    {
        var fs = Fs(files: new[] { @"C:\Users\dev\argus\CLAUDE.md" }, dirs: new[] { @"C:\Users\dev\.claude" });

        ClaudeProjectRoot.Resolve(@"C:\Users\dev\argus\docs", Home, fs.Files, fs.Dirs)
            .Path.Should().Be(@"C:\Users\dev\argus");
    }

    [Fact]
    public void A_bare_claude_directory_is_marker_enough()
    {
        var fs = Fs(files: Array.Empty<string>(), dirs: new[] { @"C:\repo\.claude" });

        ClaudeProjectRoot.Resolve(@"C:\repo\docs\deep", Home, fs.Files, fs.Dirs)
            .Path.Should().Be(@"C:\repo");
    }

    [Fact]
    public void A_document_with_no_marker_anywhere_resolves_to_nothing_with_a_reason()
    {
        var fs = Fs(files: Array.Empty<string>(), dirs: Array.Empty<string>());

        var result = ClaudeProjectRoot.Resolve(@"C:\scratch\notes", Home, fs.Files, fs.Dirs);

        result.Path.Should().BeNull();
        result.Reason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void An_unusable_start_directory_resolves_to_nothing_rather_than_throwing()
    {
        // A headless gate or a background run must never die on a malformed path.
        var fs = Fs(files: Array.Empty<string>(), dirs: Array.Empty<string>());

        ClaudeProjectRoot.Resolve("   ", Home, fs.Files, fs.Dirs).Path.Should().BeNull();
    }

    [Fact]
    public void The_reason_names_the_marker_that_resolved_the_root()
    {
        var fs = Fs(files: new[] { @"C:\repo\CLAUDE.md" }, dirs: Array.Empty<string>());

        ClaudeProjectRoot.Resolve(@"C:\repo\docs", Home, fs.Files, fs.Dirs)
            .Reason.Should().Contain("CLAUDE.md");
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail** (not executed — implementation and tests were written together and run once)

Run: `dotnet test test/Spectacle.Tests/Spectacle.Tests.csproj --filter "FullyQualifiedName~ClaudeProjectRootTests"`

Expected: build failure — `The name 'ClaudeProjectRoot' does not exist in the current context`.

- [x] **Step 3: Write the implementation**

Create `src/Spectacle/Ai/ClaudeProjectRoot.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;

namespace Spectacle.Ai;

/// <summary>
/// Where a viewer-started Claude process resolved to, and why. <see cref="Path"/> is
/// <c>null</c> when no project scope could be established; <see cref="Reason"/> is a one-line
/// account either way, shown on the run chip so a fallback is never silent.
/// </summary>
public sealed record ClaudeProjectRootResult(string? Path, string Reason);

/// <summary>
/// Finds the directory a viewer-started <c>claude -p</c> must run in, so the artifact's own
/// project instructions, settings, rules and hooks load.
///
/// Supplying an absolute filename does not pull in that file's project configuration — the
/// working directory does. A run launched from the viewer's own folder, or from a document folder
/// beneath the project, is an ungoverned session that looks governed.
///
/// The rule is the <em>outermost</em> marker-bearing ancestor, not the nearest one.
/// <see cref="Spectacle.Cli.ConfigLocator"/> uses nearest-wins for <c>.spectacle.json</c>, which is
/// right for a config meant to be overridden per directory; Claude Code configuration is not that
/// shape. A <c>docs/.claude/</c> holding one narrow setting would shadow the repository root and
/// silently drop everything above it. Subdirectory instructions still apply, because Claude Code
/// reads nested <c>CLAUDE.md</c> files when it reads files beneath them.
///
/// Two ceilings bound the walk. The enclosing git repository, so it cannot escape into a parent
/// checkout. And the user profile directory, because nearly every machine has a <c>~/.claude/</c>
/// holding user-level settings — an unbounded walk would name the home directory as the project
/// root for any document under it, reporting a resolved scope while loading no project
/// configuration at all.
/// </summary>
public static class ClaudeProjectRoot
{
    /// <summary>Files whose presence marks a directory as a Claude Code project root.</summary>
    public static readonly string[] MarkerFiles = { "CLAUDE.md" };

    /// <summary>Directories whose presence marks a directory as a Claude Code project root.</summary>
    public static readonly string[] MarkerDirectories = { ".claude" };

    /// <summary>Resolves against the real filesystem and the real user profile.</summary>
    public static ClaudeProjectRootResult Resolve(string startDirectory) => Resolve(
        startDirectory,
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        File.Exists,
        Directory.Exists);

    /// <summary>
    /// The walk itself, with the home directory and the filesystem passed in so it can be
    /// exercised without either.
    /// </summary>
    public static ClaudeProjectRootResult Resolve(
        string startDirectory, string? userProfile, Func<string, bool> fileExists, Func<string, bool> directoryExists)
    {
        DirectoryInfo? dir;
        try { dir = new DirectoryInfo(Path.GetFullPath(startDirectory)); }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException or IOException)
        {
            return new ClaudeProjectRootResult(null, $"'{startDirectory}' is not a usable directory path");
        }

        var home = Normalize(userProfile);

        // Climb once, recording every marker-bearing ancestor and where the git root sits. The
        // last marker recorded before the ceiling is the outermost one.
        string? outermost = null;
        string? marker = null;
        string? gitRoot = null;

        for (; dir is not null; dir = dir.Parent)
        {
            var path = dir.FullName;

            // At or above the home directory the markers belong to the user scope, not to any
            // project — stop before considering them.
            if (home is not null && IsAtOrAbove(path, home)) break;

            foreach (var name in MarkerFiles)
                if (fileExists(Path.Combine(path, name))) { outermost = path; marker = name; }
            foreach (var name in MarkerDirectories)
                if (directoryExists(Path.Combine(path, name))) { outermost = path; marker ??= name; }

            if (gitRoot is null && directoryExists(Path.Combine(path, ".git"))) gitRoot = path;
            if (gitRoot is not null && string.Equals(gitRoot, path, StringComparison.OrdinalIgnoreCase)) break;
        }

        return outermost is null
            ? new ClaudeProjectRootResult(null, $"no CLAUDE.md or .claude directory above '{startDirectory}'")
            : new ClaudeProjectRootResult(outermost, $"{marker} in '{outermost}'");
    }

    private static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)); }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException or IOException)
        {
            return null;
        }
    }

    /// <summary>Whether <paramref name="path"/> is <paramref name="ceiling"/> or an ancestor of it.</summary>
    private static bool IsAtOrAbove(string path, string ceiling)
    {
        var p = Path.TrimEndingDirectorySeparator(path);
        if (string.Equals(p, ceiling, StringComparison.OrdinalIgnoreCase)) return true;
        // A drive root keeps its trailing separator, so appending another would never match.
        var prefix = p.EndsWith(Path.DirectorySeparatorChar) ? p : p + Path.DirectorySeparatorChar;
        return ceiling.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
```

The `IEnumerable`/`List` usings are deliberately absent — add none. `TreatWarningsAsErrors` fails the build on an unused `using`.

- [x] **Step 4: Run the tests to verify they pass**

Run: `dotnet test test/Spectacle.Tests/Spectacle.Tests.csproj --filter "FullyQualifiedName~ClaudeProjectRootTests"`

Expected: PASS, 9 tests.

- [x] **Step 5: Stage**

```bash
git add src/Spectacle/Ai/ClaudeProjectRoot.cs test/Spectacle.Tests/ClaudeProjectRootTests.cs
```

Do not commit.

---

### Task 2: `ArtifactContext` — read and validate the namespace

**Files:**
- Create: `src/Spectacle/Ai/ArtifactContext.cs`
- Test: `test/Spectacle.Tests/ArtifactContextTests.cs`

**Interfaces:**
- Consumes: `Spectacle.Gate.FrontMatter.Parse(string?)` → `FrontMatterBlock` with `Present`, `Closed`, `EndLine` (1-based line number of the closing fence, or of the last line when unclosed).
- Produces:
  - `Spectacle.Ai.ArtifactContextState` — enum `Absent`, `Present`, `Malformed`.
  - `Spectacle.Ai.ArtifactContextView(ArtifactContextState State, IReadOnlyList<string> Sections, IReadOnlyList<string> Issues)` with `ArtifactContextView.None`, `ArtifactContextView.Key`, `ArtifactContextView.KnownSections`, and `bool IsManaged`.
  - `Spectacle.Ai.ArtifactContext.Read(string? documentText)` → `ArtifactContextView`

**Why this does not use `FrontMatterEntry`:** the front-matter parser is a deliberate YAML subset with no block-scalar support. `history: >` parses to the literal value `">"` with its continuation lines dropped, and `- decision: X` / `reason: Y` parses the first line as a sequence item and the second as a nested key `decisions.reason`. Extending the parser would change what every existing document's metadata card and required-key template see. This reader slices the raw front-matter line region instead.

- [x] **Step 1: Write the failing tests**

Create `test/Spectacle.Tests/ArtifactContextTests.cs`:

```csharp
using FluentAssertions;
using Spectacle.Ai;
using Xunit;

namespace Spectacle.Tests;

public class ArtifactContextTests
{
    private const string Managed = """
---
title: Retry design
status: draft
artifact_context:
  purpose: >
    Decide how the poller retries a failed fetch.
  decisions:
    - decision: Use a 30-second retry delay.
      reason: Telemetry showed 10 seconds was too aggressive.
  unresolved:
    - Whether the backoff should be exponential.
---

# Retry design

Body.
""";

    [Fact]
    public void A_document_with_no_front_matter_has_no_context()
    {
        ArtifactContext.Read("# Just a heading\n\nBody.").State.Should().Be(ArtifactContextState.Absent);
    }

    [Fact]
    public void A_header_without_the_namespace_has_no_context()
    {
        ArtifactContext.Read("---\ntitle: Draft\n---\n\n# Draft\n")
            .State.Should().Be(ArtifactContextState.Absent);
    }

    [Fact]
    public void A_well_formed_capsule_is_present_with_its_sections_named()
    {
        var view = ArtifactContext.Read(Managed);

        view.State.Should().Be(ArtifactContextState.Present);
        view.IsManaged.Should().BeTrue();
        view.Sections.Should().BeEquivalentTo(new[] { "purpose", "decisions", "unresolved" });
        view.Issues.Should().BeEmpty();
    }

    [Fact]
    public void A_block_scalar_continuation_is_not_mistaken_for_a_section()
    {
        // Only keys at the sections' own indent count. A block scalar's prose sits deeper, so a
        // sentence that happens to read like a section key must not invent one.
        const string trap = """
---
artifact_context:
  purpose: >
    We investigated three architectures: queue, projection, direct.
    evidence: this sentence is prose inside a block scalar, not a section.
  decisions:
    - decision: Chose the projection reader.
---

# Doc
""";

        var view = ArtifactContext.Read(trap);

        view.State.Should().Be(ArtifactContextState.Present);
        view.Sections.Should().BeEquivalentTo(new[] { "purpose", "decisions" });
        view.Sections.Should().NotContain("evidence");
    }

    [Fact]
    public void A_scalar_where_a_mapping_belongs_is_malformed()
    {
        var view = ArtifactContext.Read("---\nartifact_context: none yet\n---\n\n# Doc\n");

        view.State.Should().Be(ArtifactContextState.Malformed);
        view.IsManaged.Should().BeTrue();
        view.Issues.Should().ContainSingle().Which.Should().Contain("block mapping");
    }

    [Fact]
    public void An_empty_namespace_is_malformed()
    {
        var view = ArtifactContext.Read("---\nartifact_context:\ntitle: Draft\n---\n\n# Doc\n");

        view.State.Should().Be(ArtifactContextState.Malformed);
        view.Issues.Should().ContainSingle().Which.Should().Contain("no context sections");
    }

    [Fact]
    public void A_duplicated_namespace_is_malformed()
    {
        var text = "---\nartifact_context:\n  purpose: A\nartifact_context:\n  purpose: B\n---\n\n# Doc\n";

        var view = ArtifactContext.Read(text);

        view.State.Should().Be(ArtifactContextState.Malformed);
        view.Issues.Should().Contain(i => i.Contains("declared 2 times"));
    }

    [Fact]
    public void An_unclosed_header_carrying_the_namespace_is_malformed()
    {
        var view = ArtifactContext.Read("---\nartifact_context:\n  purpose: A\n\n# Doc\n");

        view.State.Should().Be(ArtifactContextState.Malformed);
        view.Issues.Should().Contain(i => i.Contains("never closed"));
    }

    [Fact]
    public void A_namespace_with_only_unrecognized_children_is_malformed_but_kept()
    {
        var view = ArtifactContext.Read("---\nartifact_context:\n  notes: something\n---\n\n# Doc\n");

        view.State.Should().Be(ArtifactContextState.Malformed);
        view.Sections.Should().BeEmpty();
        view.Issues.Should().Contain(i => i.Contains("no recognized context section"));
    }

    [Fact]
    public void A_crlf_document_reads_the_same_as_an_lf_one()
    {
        // Normalize first: a raw string literal keeps the source file's own line endings, which
        // .gitattributes may make either — so neither form can be assumed here.
        var lf = Managed.Replace("\r\n", "\n");

        ArtifactContext.Read(lf.Replace("\n", "\r\n")).Sections
            .Should().BeEquivalentTo(ArtifactContext.Read(lf).Sections);
    }

    [Fact]
    public void Null_and_empty_input_are_absent_rather_than_throwing()
    {
        ArtifactContext.Read(null).State.Should().Be(ArtifactContextState.Absent);
        ArtifactContext.Read("").State.Should().Be(ArtifactContextState.Absent);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail** (not executed — implementation and tests were written together and run once)

Run: `dotnet test test/Spectacle.Tests/Spectacle.Tests.csproj --filter "FullyQualifiedName~ArtifactContextTests"`

Expected: build failure — `ArtifactContext` does not exist.

- [x] **Step 3: Write the implementation**

Create `src/Spectacle/Ai/ArtifactContext.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Spectacle.Gate;

namespace Spectacle.Ai;

/// <summary>Whether a document carries a usable context capsule.</summary>
public enum ArtifactContextState
{
    /// <summary>No <c>artifact_context</c> namespace — an unmanaged document.</summary>
    Absent,

    /// <summary>A well-formed capsule this session inherits.</summary>
    Present,

    /// <summary>A capsule that is there but structurally broken. Repaired, never discarded.</summary>
    Malformed,
}

/// <summary>
/// What the <c>artifact_context</c> namespace looks like right now: whether it exists, which
/// recognized sections it carries, and what is wrong with it. Read-only by design — the agent
/// writes the capsule, Spectacle reads it and says what it found.
/// </summary>
public sealed record ArtifactContextView(
    ArtifactContextState State,
    IReadOnlyList<string> Sections,
    IReadOnlyList<string> Issues)
{
    /// <summary>The reserved front-matter key holding cross-session state.</summary>
    public const string Key = "artifact_context";

    /// <summary>
    /// The sections a capsule is built from, ordered for reconstruction rather than chronology:
    /// what the artifact is for, what was decided, what bounds it, what is still open, what the
    /// evidence was, and only then how it got here.
    /// </summary>
    public static readonly string[] KnownSections =
        { "purpose", "decisions", "constraints", "unresolved", "evidence", "assumptions", "rejected", "history" };

    /// <summary>A document with no capsule.</summary>
    public static readonly ArtifactContextView None =
        new(ArtifactContextState.Absent, Array.Empty<string>(), Array.Empty<string>());

    /// <summary>
    /// Whether this document is a managed artifact — one whose revision must run under the
    /// project's artifact-context policy. A broken capsule still counts: it is evidence the
    /// document is managed, and losing it is exactly the failure this guards against.
    /// </summary>
    public bool IsManaged => State != ArtifactContextState.Absent;
}

/// <summary>
/// Reads the <c>artifact_context</c> namespace out of a document's front matter.
///
/// It works on the raw header lines rather than on <see cref="FrontMatterEntry"/> values, because
/// the front-matter parser is a deliberate YAML subset with no block-scalar support: a
/// <c>history: &gt;</c> section parses to the literal value <c>"&gt;"</c> with its continuation
/// lines dropped, and a sequence of mappings — the shape <c>decisions</c> uses — parses its second
/// line as a nested key. Teaching the parser those constructs would change what every existing
/// document's metadata card and required-key template see, for no gain here: this reader needs
/// structure, not values.
/// </summary>
public static class ArtifactContext
{
    /// <summary>Inspects <paramref name="documentText"/>'s capsule.</summary>
    public static ArtifactContextView Read(string? documentText)
    {
        var header = FrontMatter.Parse(documentText);
        if (!header.Present) return ArtifactContextView.None;

        var lines = (documentText ?? string.Empty).Split('\n');

        // Header body: everything between the opening fence and the closing one. An unclosed
        // header has no closing fence to stop at, so the whole remainder is scanned and the
        // missing fence is reported as an issue.
        const int from = 1;
        var to = header.Closed ? Math.Min(header.EndLine - 1, lines.Length) : lines.Length;

        var starts = new List<int>();
        for (var i = from; i < to; i++)
        {
            var raw = lines[i].TrimEnd('\r');
            if (raw.Length == 0 || raw[0] == ' ' || raw[0] == '\t' || raw[0] == '#') continue;
            var colon = raw.IndexOf(':');
            if (colon <= 0) continue;
            if (raw[..colon].Trim().Equals(ArtifactContextView.Key, StringComparison.OrdinalIgnoreCase))
                starts.Add(i);
        }

        if (starts.Count == 0) return ArtifactContextView.None;

        var issues = new List<string>();
        if (!header.Closed) issues.Add("the front-matter header is never closed by a --- fence");
        if (starts.Count > 1) issues.Add($"'{ArtifactContextView.Key}' is declared {starts.Count} times");

        var start = starts[0];
        var opening = lines[start].TrimEnd('\r');
        var inline = opening[(opening.IndexOf(':') + 1)..].Trim();
        var children = Children(lines, start, to);

        if (children.Count == 0)
            issues.Add(inline.Length == 0
                ? $"'{ArtifactContextView.Key}' is declared with no context sections beneath it"
                : $"'{ArtifactContextView.Key}' holds a value on its own line where a block mapping of context sections belongs");

        var sections = Sections(children);
        if (children.Count != 0 && sections.Count == 0)
            issues.Add($"'{ArtifactContextView.Key}' has no recognized context section ({string.Join(", ", ArtifactContextView.KnownSections)})");

        return new ArtifactContextView(
            issues.Count == 0 ? ArtifactContextState.Present : ArtifactContextState.Malformed,
            sections,
            issues);
    }

    /// <summary>The indented lines beneath the namespace key, up to the next unindented key.</summary>
    private static List<string> Children(string[] lines, int start, int toExclusive)
    {
        var children = new List<string>();
        for (var i = start + 1; i < toExclusive; i++)
        {
            var raw = lines[i].TrimEnd('\r');
            if (raw.Trim().Length == 0) continue;
            if (raw[0] != ' ' && raw[0] != '\t') break;
            children.Add(raw);
        }
        return children;
    }

    /// <summary>
    /// The recognized section names, taken only from keys at the first child's own indent. That
    /// indent rule is what keeps a block scalar's prose out: a sentence like "investigated three
    /// architectures: A, B and C" sits deeper than the section keys and is never read as one.
    /// </summary>
    private static IReadOnlyList<string> Sections(IReadOnlyList<string> children)
    {
        if (children.Count == 0) return Array.Empty<string>();

        var indent = Indent(children[0]);
        var found = new List<string>();
        foreach (var raw in children)
        {
            if (Indent(raw) != indent) continue;
            var trimmed = raw.Trim();
            if (trimmed[0] == '#' || trimmed.StartsWith("- ", StringComparison.Ordinal)) continue;
            var colon = trimmed.IndexOf(':');
            if (colon <= 0) continue;
            var name = trimmed[..colon].Trim();
            if (ArtifactContextView.KnownSections.Contains(name, StringComparer.OrdinalIgnoreCase)
                && !found.Contains(name, StringComparer.OrdinalIgnoreCase))
                found.Add(name.ToLowerInvariant());
        }
        return found;
    }

    private static int Indent(string raw) => raw.Length - raw.TrimStart(' ', '\t').Length;
}
```

- [x] **Step 4: Run the tests to verify they pass**

Run: `dotnet test test/Spectacle.Tests/Spectacle.Tests.csproj --filter "FullyQualifiedName~ArtifactContextTests"`

Expected: PASS, 11 tests.

- [x] **Step 5: Run the full suite**

Run: `dotnet test test/Spectacle.Tests/Spectacle.Tests.csproj`

Expected: PASS. Nothing in this task changes existing behavior, so a failure here means the new file broke a build assumption — fix before continuing.

- [x] **Step 6: Stage**

```bash
git add src/Spectacle/Ai/ArtifactContext.cs test/Spectacle.Tests/ArtifactContextTests.cs
```

---

### Task 3: The cross-session handoff section in the prompt

**Files:**
- Modify: `src/Spectacle/Ai/ClaudeRevisionPrompt.cs`
- Test: `test/Spectacle.Tests/ArtifactContextPromptTests.cs` (create)

**Interfaces:**
- Consumes: `ArtifactContextView`, `ArtifactContextState` (Task 2).
- Produces:
  - `ClaudeRevisionPrompt.Build(string documentPath, string fixBrief)` — unchanged signature, now delegates with `ArtifactContextView.None`.
  - `ClaudeRevisionPrompt.Build(string documentPath, string fixBrief, ArtifactContextView context)` → `string`

**Placement matters:** the handoff section is inserted **before** the `"The revision brief:"` line. `ClaudeCliTests.The_prompt_ends_with_the_brief` asserts the prompt ends with the brief; appending after it would break that test and, worse, bury the brief.

- [x] **Step 1: Write the failing tests**

Create `test/Spectacle.Tests/ArtifactContextPromptTests.cs`:

```csharp
using System;
using FluentAssertions;
using Spectacle.Ai;
using Xunit;

namespace Spectacle.Tests;

public class ArtifactContextPromptTests
{
    private const string DocPath = @"C:\repo\docs\architecture.md";
    private const string Brief = "1. Change the retry interval to 30 seconds.";

    private static ArtifactContextView Present(params string[] sections) =>
        new(ArtifactContextState.Present, sections, Array.Empty<string>());

    [Fact]
    public void The_brief_is_still_the_last_thing_in_the_prompt()
    {
        // The handoff section goes before the brief: the agent reads the contract, then the ask.
        var nl = Environment.NewLine;
        ClaudeRevisionPrompt.Build(DocPath, Brief, Present("decisions"))
            .Should().EndWith("The revision brief:" + nl + nl + Brief);
    }

    [Fact]
    public void The_in_place_contract_survives_the_addition()
    {
        var prompt = ClaudeRevisionPrompt.Build(DocPath, Brief, Present("decisions"));

        prompt.Should().Contain("revise it IN PLACE: " + DocPath);
        prompt.Should().Contain("Create no other file");
        prompt.Should().Contain("records each save as an iteration");
    }

    [Fact]
    public void The_default_overload_still_builds_a_prompt_without_a_capsule()
    {
        ClaudeRevisionPrompt.Build(DocPath, Brief)
            .Should().Be(ClaudeRevisionPrompt.Build(DocPath, Brief, ArtifactContextView.None));
    }

    [Fact]
    public void An_existing_capsule_is_declared_inherited_and_authoritative()
    {
        var prompt = ClaudeRevisionPrompt.Build(DocPath, Brief, Present("purpose", "decisions", "unresolved"));

        prompt.Should().Contain("independent session");
        prompt.Should().Contain("artifact_context");
        prompt.Should().Contain("inherited");
        prompt.Should().Contain("Read the complete file before");
        prompt.Should().Contain("purpose, decisions, unresolved");
    }

    [Fact]
    public void The_merge_rules_forbid_replacing_the_capsule_and_forbid_an_append_only_log()
    {
        var prompt = ClaudeRevisionPrompt.Build(DocPath, Brief, Present("decisions"));

        prompt.Should().Contain("merge");
        prompt.Should().Contain("recompress");
        prompt.Should().Contain("not an append-only event log");
        prompt.Should().Contain("two contradictory current decisions");
    }

    [Fact]
    public void A_resolved_question_must_leave_the_unresolved_section()
    {
        ClaudeRevisionPrompt.Build(DocPath, Brief, Present("unresolved"))
            .Should().Contain("no longer belongs under `unresolved`");
    }

    [Fact]
    public void The_request_is_stored_as_intent_rather_than_as_its_wording()
    {
        ClaudeRevisionPrompt.Build(DocPath, Brief, Present("history"))
            .Should().Contain("not the conversational wording");
    }

    [Fact]
    public void Unrelated_front_matter_is_declared_off_limits()
    {
        ClaudeRevisionPrompt.Build(DocPath, Brief, Present("decisions"))
            .Should().Contain("Do not overwrite or delete unrelated front matter");
    }

    [Fact]
    public void An_absent_capsule_is_seeded_rather_than_assumed()
    {
        var prompt = ClaudeRevisionPrompt.Build(DocPath, Brief, ArtifactContextView.None);

        prompt.Should().Contain("does not carry an `artifact_context`");
        prompt.Should().Contain("Create it");
    }

    [Fact]
    public void A_malformed_capsule_is_repaired_conservatively_and_never_discarded()
    {
        var broken = new ArtifactContextView(
            ArtifactContextState.Malformed,
            Array.Empty<string>(),
            new[] { "'artifact_context' is declared 2 times" });

        var prompt = ClaudeRevisionPrompt.Build(DocPath, Brief, broken);

        prompt.Should().Contain("is declared 2 times");
        prompt.Should().Contain("Do not discard it");
        prompt.Should().Contain("Preserve every readable");
    }

    [Fact]
    public void The_prompt_never_finishes_on_an_invalid_artifact()
    {
        ClaudeRevisionPrompt.Build(DocPath, Brief, Present("decisions"))
            .Should().Contain("Do not finish while the artifact is structurally invalid");
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail** (not executed — implementation and tests were written together and run once)

Run: `dotnet test test/Spectacle.Tests/Spectacle.Tests.csproj --filter "FullyQualifiedName~ArtifactContextPromptTests"`

Expected: build failure — no three-argument `Build` overload.

- [x] **Step 3: Extend the prompt**

In `src/Spectacle/Ai/ClaudeRevisionPrompt.cs`, replace the existing `Build` method's signature line and add the handoff builder. Keep every existing rule string byte for byte.

Replace:

```csharp
    public static string Build(string documentPath, string fixBrief)
    {
        var name = Path.GetFileName(documentPath);
```

with:

```csharp
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
```

Then, immediately before the existing lines:

```csharp
        sb.AppendLine();
        sb.AppendLine("The revision brief:");
```

insert:

```csharp
        AppendHandoff(sb, context);
```

And add this method to the class:

```csharp
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
```

Add `using` for nothing new — `ArtifactContextView` is in the same `Spectacle.Ai` namespace, and `System.Text`/`System.IO` are already imported.

- [x] **Step 4: Run the tests to verify they pass**

Run: `dotnet test test/Spectacle.Tests/Spectacle.Tests.csproj --filter "FullyQualifiedName~ArtifactContextPromptTests"`

Expected: PASS, 11 tests.

- [x] **Step 5: Verify the existing prompt tests still pass**

Run: `dotnet test test/Spectacle.Tests/Spectacle.Tests.csproj --filter "FullyQualifiedName~ClaudeCliTests"`

Expected: PASS. If `The_prompt_ends_with_the_brief` fails, the handoff section was appended after the brief instead of before it.

- [x] **Step 6: Stage**

```bash
git add src/Spectacle/Ai/ClaudeRevisionPrompt.cs test/Spectacle.Tests/ArtifactContextPromptTests.cs
```

---

### Task 4: `ClaudeArtifactRevisionService` — the single integration boundary

**Files:**
- Create: `src/Spectacle/Ai/ClaudeArtifactRevisionService.cs`
- Test: `test/Spectacle.Tests/ClaudeArtifactRevisionServiceTests.cs`
- Modify: `test/Spectacle.Tests/ClaudeCliTests.cs` (add the `--bare` regression test)

**Interfaces:**
- Consumes: `ClaudeProjectRoot.Resolve` (Task 1), `ArtifactContext.Read` (Task 2), `ClaudeRevisionPrompt.Build(path, brief, context)` (Task 3), `ClaudeRevisionRunner.TryStart(string workingDirectory, string prompt)` → `bool`.
- Produces:
  - `Spectacle.Ai.ArtifactRevisionStatus` — enum `Started`, `Refused`, `Busy`.
  - `Spectacle.Ai.ArtifactRevisionOutcome(ArtifactRevisionStatus Status, string? ProjectRoot, string WorkingDirectory, string Detail)`
  - `Spectacle.Ai.ClaudeArtifactRevisionService(ClaudeRevisionRunner runner)` — production wiring.
  - `Spectacle.Ai.ClaudeArtifactRevisionService(Func<string,string,bool> startRun, Func<string,string?> readFile, Func<string,ClaudeProjectRootResult> resolveRoot)` — seam.
  - `ClaudeArtifactRevisionService.Revise(string documentPath, string documentDirectory, string brief)` → `ArtifactRevisionOutcome`
  - `ClaudeArtifactRevisionService.AbortMessage(string documentPath)` → `string`

The runner is injected as a `Func<string,string,bool>` rather than as the concrete class, so the decision logic is testable without spawning a process and the service depends on the behavior it needs rather than on the runner type.

- [x] **Step 1: Write the failing tests**

Create `test/Spectacle.Tests/ClaudeArtifactRevisionServiceTests.cs`:

```csharp
using System.Collections.Generic;
using FluentAssertions;
using Spectacle.Ai;
using Xunit;

namespace Spectacle.Tests;

public class ClaudeArtifactRevisionServiceTests
{
    private const string Root = @"C:\repo";
    private const string DocDir = @"C:\repo\docs";
    private const string DocPath = @"C:\repo\docs\architecture.md";
    private const string Brief = "1. Change the retry interval to 30 seconds.";

    private const string Managed = """
---
title: Architecture
artifact_context:
  decisions:
    - decision: Use the projection reader.
      reason: The queue reader could not replay.
---

# Architecture
""";

    private const string Unmanaged = "---\ntitle: Notes\n---\n\n# Notes\n";

    private sealed class Spy
    {
        public readonly List<(string WorkingDirectory, string Prompt)> Runs = new();
        public bool Accept = true;
        public bool Start(string workingDirectory, string prompt)
        {
            Runs.Add((workingDirectory, prompt));
            return Accept;
        }
    }

    private static ClaudeArtifactRevisionService Service(Spy spy, string? document, ClaudeProjectRootResult root) =>
        new(spy.Start, _ => document, _ => root);

    private static ClaudeProjectRootResult Found => new(Root, @"CLAUDE.md in 'C:\repo'");
    private static ClaudeProjectRootResult NotFound => new(null, "no CLAUDE.md or .claude directory above 'C:\\repo\\docs'");

    [Fact]
    public void A_resolved_project_root_is_the_working_directory_not_the_document_folder()
    {
        // Supplying an absolute filename does not load that file's project configuration; the
        // working directory does.
        var spy = new Spy();

        var outcome = Service(spy, Managed, Found).Revise(DocPath, DocDir, Brief);

        outcome.Status.Should().Be(ArtifactRevisionStatus.Started);
        outcome.ProjectRoot.Should().Be(Root);
        outcome.WorkingDirectory.Should().Be(Root);
        spy.Runs.Should().ContainSingle().Which.WorkingDirectory.Should().Be(Root);
    }

    [Fact]
    public void The_prompt_carries_the_documents_own_capsule_state()
    {
        var spy = new Spy();

        Service(spy, Managed, Found).Revise(DocPath, DocDir, Brief);

        var prompt = spy.Runs[0].Prompt;
        prompt.Should().Contain("inherited");
        prompt.Should().Contain("It currently carries: decisions.");
        prompt.Should().EndWith(Brief);
    }

    [Fact]
    public void A_managed_artifact_with_no_project_root_is_refused_rather_than_run_ungoverned()
    {
        var spy = new Spy();

        var outcome = Service(spy, Managed, NotFound).Revise(DocPath, DocDir, Brief);

        outcome.Status.Should().Be(ArtifactRevisionStatus.Refused);
        outcome.Detail.Should().Be(ClaudeArtifactRevisionService.AbortMessage(DocPath));
        outcome.Detail.Should().Contain("Artifact revision aborted");
        outcome.Detail.Should().Contain("artifact-context policy");
        spy.Runs.Should().BeEmpty();
    }

    [Fact]
    public void A_malformed_capsule_still_counts_as_managed_and_is_still_refused()
    {
        // A broken capsule is evidence the document is managed; losing it is the failure being
        // guarded against.
        var spy = new Spy();

        var outcome = Service(spy, "---\nartifact_context: none\n---\n\n# Doc\n", NotFound)
            .Revise(DocPath, DocDir, Brief);

        outcome.Status.Should().Be(ArtifactRevisionStatus.Refused);
        spy.Runs.Should().BeEmpty();
    }

    [Fact]
    public void An_unmanaged_document_with_no_project_root_still_runs_from_its_own_folder()
    {
        // Pressing "a" on a loose .md outside any repository works today and keeps working.
        var spy = new Spy();

        var outcome = Service(spy, Unmanaged, NotFound).Revise(DocPath, DocDir, Brief);

        outcome.Status.Should().Be(ArtifactRevisionStatus.Started);
        outcome.ProjectRoot.Should().BeNull();
        outcome.WorkingDirectory.Should().Be(DocDir);
        spy.Runs.Should().ContainSingle().Which.WorkingDirectory.Should().Be(DocDir);
    }

    [Fact]
    public void The_fallback_reason_is_surfaced_rather_than_swallowed()
    {
        var spy = new Spy();

        Service(spy, Unmanaged, NotFound).Revise(DocPath, DocDir, Brief)
            .Detail.Should().Contain("no CLAUDE.md or .claude");
    }

    [Fact]
    public void An_unmanaged_document_seeds_a_capsule_when_a_root_does_resolve()
    {
        var spy = new Spy();

        Service(spy, Unmanaged, Found).Revise(DocPath, DocDir, Brief);

        spy.Runs[0].Prompt.Should().Contain("does not carry an `artifact_context`");
    }

    [Fact]
    public void A_run_already_in_flight_is_reported_as_busy_not_as_a_failure()
    {
        // The chip must keep showing the running run rather than flipping to failed.
        var spy = new Spy { Accept = false };

        Service(spy, Managed, Found).Revise(DocPath, DocDir, Brief)
            .Status.Should().Be(ArtifactRevisionStatus.Busy);
    }

    [Fact]
    public void An_unreadable_document_is_treated_as_unmanaged_and_says_so()
    {
        var spy = new Spy();

        var outcome = new ClaudeArtifactRevisionService(spy.Start, _ => null, _ => Found)
            .Revise(DocPath, DocDir, Brief);

        outcome.Status.Should().Be(ArtifactRevisionStatus.Started);
        outcome.Detail.Should().Contain("could not be read");
    }
}
```

Add to the end of `test/Spectacle.Tests/ClaudeCliTests.cs`, inside the existing class (before its closing brace):

```csharp
    [Fact]
    public void The_launch_never_uses_bare_mode()
    {
        // --bare skips the project instructions, skills, hooks and settings that artifact-context
        // continuity depends on. A future startup-speed optimization must not silently disable
        // them: see docs/superpowers/specs/2026-08-24-artifact-context-continuity-design.md.
        foreach (var exe in new[] { "C:\\tools\\claude.exe", "C:\\npm\\claude.cmd" })
            ClaudeRevisionRunner.BuildStartInfo(exe, "C:\\specs")
                .Arguments.Should().NotContain("--bare");
    }
```

- [ ] **Step 2: Run the tests to verify they fail** (not executed — implementation and tests were written together and run once)

Run: `dotnet test test/Spectacle.Tests/Spectacle.Tests.csproj --filter "FullyQualifiedName~ClaudeArtifactRevisionServiceTests"`

Expected: build failure — `ClaudeArtifactRevisionService` does not exist. (`The_launch_never_uses_bare_mode` passes already; it is a regression guard, not a driver.)

- [x] **Step 3: Write the implementation**

Create `src/Spectacle/Ai/ClaudeArtifactRevisionService.cs`:

```csharp
using System;
using System.IO;

namespace Spectacle.Ai;

/// <summary>How a requested revision was disposed of.</summary>
public enum ArtifactRevisionStatus
{
    /// <summary>The run was launched.</summary>
    Started,

    /// <summary>Refused on policy: a managed artifact with no resolvable project scope.</summary>
    Refused,

    /// <summary>Another run is already rewriting this document.</summary>
    Busy,
}

/// <summary>
/// What one revision request produced: whether it ran, the project root it resolved (<c>null</c>
/// when it fell back), the directory the process actually started in, and a one-line account for
/// the run chip. Every path returns a reason — a fallback that says nothing is the ungoverned
/// session this design exists to prevent.
/// </summary>
public sealed record ArtifactRevisionOutcome(
    ArtifactRevisionStatus Status, string? ProjectRoot, string WorkingDirectory, string Detail);

/// <summary>
/// The one path from the viewer to <c>claude -p</c>.
///
/// Nothing else in the application assembles an invocation: the sequence that has to hold for
/// cross-session continuity — resolve the project scope, read the artifact's inherited context,
/// decide whether the run may proceed at all, build the prompt that carries the handoff contract,
/// launch in the resolved root — lives here rather than at each call site, where it would be
/// tribal knowledge one caller at a time.
///
/// Two invariants it exists to enforce. A run starts in the artifact's own Claude project root, so
/// the project's instructions, settings, rules and hooks load; supplying an absolute filename does
/// not do that, the working directory does. And a document that carries an <c>artifact_context</c>
/// capsule is never revised by a session that could not find the policy governing it — that run is
/// refused, loudly, rather than quietly producing a flattened capsule.
/// </summary>
public sealed class ClaudeArtifactRevisionService
{
    private readonly Func<string, string, bool> _startRun;
    private readonly Func<string, string?> _readFile;
    private readonly Func<string, ClaudeProjectRootResult> _resolveRoot;

    /// <summary>Production wiring: a real runner, the real filesystem, the real walk.</summary>
    public ClaudeArtifactRevisionService(ClaudeRevisionRunner runner)
        : this(runner.TryStart, ReadOrNull, ClaudeProjectRoot.Resolve) { }

    /// <summary>
    /// The seam, with the launch, the read and the root walk passed in — so the decision logic is
    /// exercised without spawning a process or touching disk.
    /// </summary>
    public ClaudeArtifactRevisionService(
        Func<string, string, bool> startRun,
        Func<string, string?> readFile,
        Func<string, ClaudeProjectRootResult> resolveRoot)
    {
        _startRun = startRun;
        _readFile = readFile;
        _resolveRoot = resolveRoot;
    }

    /// <summary>
    /// The message shown when a managed artifact cannot establish its project scope. Explicit
    /// failure, rather than a session that runs without the policy it is supposed to follow.
    /// </summary>
    public static string AbortMessage(string documentPath) =>
        "Artifact revision aborted: Could not locate a Claude Code project root containing the " +
        $"artifact-context policy for {documentPath}.";

    /// <summary>
    /// Starts one revision of <paramref name="documentPath"/> carrying <paramref name="brief"/>.
    /// <paramref name="documentDirectory"/> is the document's own folder — the working directory
    /// used only when no project root resolves and the document is unmanaged.
    /// </summary>
    public ArtifactRevisionOutcome Revise(string documentPath, string documentDirectory, string brief)
    {
        var text = _readFile(documentPath);
        var context = text is null ? ArtifactContextView.None : ArtifactContext.Read(text);
        var root = _resolveRoot(documentDirectory);

        if (root.Path is null && context.IsManaged)
            return new ArtifactRevisionOutcome(
                ArtifactRevisionStatus.Refused, null, string.Empty, AbortMessage(documentPath));

        var workingDirectory = root.Path ?? documentDirectory;
        var detail = root.Path is not null
            ? $"project root: {root.Reason}"
            : $"no project root ({root.Reason}); running in the document's folder";
        if (text is null) detail += "; the document could not be read, so no inherited context was found";

        var started = _startRun(workingDirectory, ClaudeRevisionPrompt.Build(documentPath, brief, context));
        return new ArtifactRevisionOutcome(
            started ? ArtifactRevisionStatus.Started : ArtifactRevisionStatus.Busy,
            root.Path,
            workingDirectory,
            started ? detail : "a revision run is already in flight");
    }

    /// <summary>The document's text, or <c>null</c> when it cannot be read — never an exception.</summary>
    private static string? ReadOrNull(string path)
    {
        try { return File.ReadAllText(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}
```

- [x] **Step 4: Run the tests to verify they pass**

Run: `dotnet test test/Spectacle.Tests/Spectacle.Tests.csproj --filter "FullyQualifiedName~ClaudeArtifactRevisionServiceTests|FullyQualifiedName~ClaudeCliTests"`

Expected: PASS.

- [x] **Step 5: Stage**

```bash
git add src/Spectacle/Ai/ClaudeArtifactRevisionService.cs test/Spectacle.Tests/ClaudeArtifactRevisionServiceTests.cs test/Spectacle.Tests/ClaudeCliTests.cs
```

---

### Task 5: Wire the viewer through the service

**Files:**
- Modify: `src/Spectacle/MainWindow.xaml.cs:85-99` (the `if (claudeCli is not null)` block)

**Interfaces:**
- Consumes: `ClaudeArtifactRevisionService` and `ArtifactRevisionStatus` (Task 4); the existing `_pipeline.ClaudeReviseRequested` event (`EventHandler<string>`, the brief), `_pipeline.SetClaudeStatus(ClaudeRevisionStatus)`, `_document.BaseDirectory`, `_sourcePath`.
- Produces: nothing consumed by later tasks.

This task has no unit test of its own: `MainWindow` is a WPF window whose construction requires an STA message pump, and the repository does not test it directly. The behavior it wires is covered by Task 4 (decision logic) and Task 7 (the real launch path). Verification here is a build plus the existing suite.

- [x] **Step 1: Replace the revise handler**

In `src/Spectacle/MainWindow.xaml.cs`, replace:

```csharp
            _pipeline.ClaudeReviseRequested += (_, brief) =>
                runner.TryStart(_document.BaseDirectory, ClaudeRevisionPrompt.Build(_sourcePath, brief));
```

with:

```csharp
            // Every revision goes through the service, never straight to the runner: it is what
            // establishes the artifact-continuity invariants — the run starts in the document's
            // own Claude project root so the project's instructions, settings, rules and hooks
            // load, and a document carrying an `artifact_context` capsule is refused outright
            // rather than revised by a session that could not find the policy governing it.
            var revisions = new ClaudeArtifactRevisionService(runner);
            _pipeline.ClaudeReviseRequested += (_, brief) =>
            {
                var outcome = revisions.Revise(_sourcePath, _document.BaseDirectory, brief);
                // A refusal is the one outcome the reader must state. A busy runner already has a
                // running chip on screen, and a started run reports itself through the stream.
                if (outcome.Status == ArtifactRevisionStatus.Refused)
                    _pipeline.SetClaudeStatus(ClaudeRevisionStatus.Failed(outcome.Detail));
            };
```

- [x] **Step 2: Build**

Run: `dotnet build test/Spectacle.Tests/Spectacle.Tests.csproj`

Expected: success with no warnings. If `ClaudeRevisionPrompt` is now unused in this file, remove nothing else — the `using Spectacle.Ai;` import is still needed for the other types.

- [x] **Step 3: Run the full suite**

Run: `dotnet test test/Spectacle.Tests/Spectacle.Tests.csproj`

Expected: PASS.

- [x] **Step 4: Stage**

```bash
git add src/Spectacle/MainWindow.xaml.cs
```

---

### Task 6: Fixture continuity — two independent sessions, asserted as data

**Files:**
- Create: `test/Spectacle.Tests/Fixtures/artifact-context/session-a.md`
- Create: `test/Spectacle.Tests/Fixtures/artifact-context/session-b.md`
- Create: `test/Spectacle.Tests/ArtifactContinuityTests.cs`

**Interfaces:**
- Consumes: `ArtifactContext.Read` (Task 2), `Spectacle.Gate.FrontMatter.Parse`.
- Produces: nothing consumed by later tasks.

The `.csproj` already copies `Fixtures\**\*.*` to the output directory with `PreserveNewest`, so no project change is needed.

These fixtures stand in for two processes that never met. Session A investigated three architectures, rejected two with reasons, chose one, and left a question open. Session B, a brand-new process with no memory of A, applied a new requirement — and the assertions are what a correct merge must look like *as data*: A's still-valid decisions survive, the superseded value appears once as history rather than twice as contradictory current decisions, and the question A left open is gone from `unresolved` because B answered it.

- [x] **Step 1: Write the session-A fixture**

Create `test/Spectacle.Tests/Fixtures/artifact-context/session-a.md`:

```markdown
---
title: Poller architecture
status: draft
owner: platform
artifact_context:
  purpose: >
    Decide how the ingest poller consumes upstream change events, and record why, so a later
    session does not re-litigate the choice.
  decisions:
    - decision: Consume changes through a projection reader.
      reason: >
        It is the only option that can replay a window after an outage without upstream
        cooperation, which the recovery requirement makes mandatory.
    - decision: Retry a failed fetch after 10 seconds.
      reason: A conservative starting value; no production data existed yet.
  constraints:
    - The upstream API is rate limited to 60 requests per minute per tenant.
    - Recovery must replay at least 24 hours without upstream cooperation.
  rejected:
    - alternative: Queue reader with at-least-once delivery.
      reason: Cannot replay past the queue retention window of one hour.
    - alternative: Direct polling of the changes endpoint.
      reason: Costs one request per tenant per interval and breaches the rate limit above 40 tenants.
  unresolved:
    - Determine the retry interval from production telemetry once the poller has run for a week.
  history: >
    Three consumption architectures were investigated. Queue reading and direct polling were
    rejected for retention and rate-limit reasons respectively; the projection reader was chosen
    for its replay window.
---

# Poller architecture

The ingest poller consumes upstream change events through a projection reader and retries a
failed fetch after 10 seconds.
```

- [x] **Step 2: Write the session-B fixture**

Create `test/Spectacle.Tests/Fixtures/artifact-context/session-b.md`:

```markdown
---
title: Poller architecture
status: draft
owner: platform
artifact_context:
  purpose: >
    Decide how the ingest poller consumes upstream change events, and record why, so a later
    session does not re-litigate the choice.
  decisions:
    - decision: Consume changes through a projection reader.
      reason: >
        It is the only option that can replay a window after an outage without upstream
        cooperation, which the recovery requirement makes mandatory.
    - decision: Retry a failed fetch after 30 seconds.
      reason: >
        Production telemetry over the first week showed the original 10-second interval
        exhausting the tenant rate limit during upstream incidents.
  constraints:
    - The upstream API is rate limited to 60 requests per minute per tenant.
    - Recovery must replay at least 24 hours without upstream cooperation.
  rejected:
    - alternative: Queue reader with at-least-once delivery.
      reason: Cannot replay past the queue retention window of one hour.
    - alternative: Direct polling of the changes endpoint.
      reason: Costs one request per tenant per interval and breaches the rate limit above 40 tenants.
  history: >
    Three consumption architectures were investigated. Queue reading and direct polling were
    rejected for retention and rate-limit reasons respectively; the projection reader was chosen
    for its replay window. The retry interval began at a conservative 10 seconds and was raised to
    30 after a week of production telemetry.
---

# Poller architecture

The ingest poller consumes upstream change events through a projection reader and retries a
failed fetch after 30 seconds.
```

- [x] **Step 3: Write the failing tests**

Create `test/Spectacle.Tests/ArtifactContinuityTests.cs`:

```csharp
using System;
using System.IO;
using FluentAssertions;
using Spectacle.Ai;
using Spectacle.Gate;
using Xunit;

namespace Spectacle.Tests;

/// <summary>
/// The primary use case, asserted on the artifact rather than on a model: session A wrote a
/// capsule, session A ended, and session B — a brand-new process with no conversational memory —
/// revised the document. What must hold is a property of the file, so it holds whichever model
/// wrote it.
/// </summary>
public class ArtifactContinuityTests
{
    // Line endings are normalized on read: .gitattributes decides what lands on disk, and no
    // assertion here is about newlines.
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "artifact-context", name))
            .Replace("\r\n", "\n");

    private static readonly string SessionA = Fixture("session-a.md");
    private static readonly string SessionB = Fixture("session-b.md");

    [Fact]
    public void Both_sessions_leave_a_well_formed_capsule()
    {
        ArtifactContext.Read(SessionA).State.Should().Be(ArtifactContextState.Present);
        ArtifactContext.Read(SessionB).State.Should().Be(ArtifactContextState.Present);
    }

    [Fact]
    public void Session_B_preserved_the_decision_session_A_made_and_its_reason()
    {
        // The new request said nothing about the consumption architecture. Losing it — or losing
        // why it was chosen — is the flattening this whole design exists to prevent.
        SessionB.Should().Contain("Consume changes through a projection reader.");
        SessionB.Should().Contain("replay a window after an outage without upstream");
    }

    [Fact]
    public void Session_B_preserved_why_the_alternatives_were_rejected()
    {
        SessionB.Should().Contain("Queue reader with at-least-once delivery.");
        SessionB.Should().Contain("retention window of one hour");
        SessionB.Should().Contain("Direct polling of the changes endpoint.");
        SessionB.Should().Contain("breaches the rate limit above 40 tenants");
    }

    [Fact]
    public void Session_B_preserved_the_constraints()
    {
        ArtifactContext.Read(SessionB).Sections.Should().Contain("constraints");
        SessionB.Should().Contain("rate limited to 60 requests per minute per tenant");
        SessionB.Should().Contain("replay at least 24 hours");
    }

    [Fact]
    public void The_superseded_value_is_history_not_a_second_current_decision()
    {
        // The capsule is current state plus causal history, not an append-only log: two
        // contradictory current decisions is the failure mode.
        var capsule = Capsule(SessionB);
        var decisions = Section(capsule, "decisions");
        var history = Section(capsule, "history");

        decisions.Should().Contain("30 seconds");
        decisions.Should().NotContain("10-second");
        decisions.Should().NotContain("10 seconds");
        history.Should().Contain("10 seconds");
        history.Should().Contain("30");
    }

    [Fact]
    public void The_change_carries_its_reason_rather_than_the_request_wording()
    {
        Section(Capsule(SessionB), "decisions").Should().Contain("Production telemetry");
        SessionB.Should().NotContain("way too aggressive");
        SessionB.Should().NotContain("Change it to 30 sec");
    }

    [Fact]
    public void The_question_session_A_left_open_is_gone_because_session_B_answered_it()
    {
        ArtifactContext.Read(SessionA).Sections.Should().Contain("unresolved");
        ArtifactContext.Read(SessionB).Sections.Should().NotContain("unresolved");
        SessionB.Should().NotContain("Determine the retry interval from production telemetry");
    }

    [Fact]
    public void Unrelated_front_matter_survived_the_revision()
    {
        var header = FrontMatter.Parse(SessionB);

        header.Find("title")!.Value.Should().Be("Poller architecture");
        header.Find("status")!.Value.Should().Be("draft");
        header.Find("owner")!.Value.Should().Be("platform");
    }

    [Fact]
    public void The_body_states_the_current_value_the_capsule_records()
    {
        FrontMatter.Strip(SessionB).Should().Contain("retries a\nfailed fetch after 30 seconds");
    }

    [Fact]
    public void The_capsule_did_not_grow_unboundedly_across_the_two_sessions()
    {
        // Merge-and-recompress, not append. A capsule that doubles per session stops being a
        // handoff and becomes a transcript.
        Capsule(SessionB).Length.Should().BeLessThan((int)(Capsule(SessionA).Length * 1.5));
    }

    /// <summary>The raw `artifact_context` region of a document's front matter.</summary>
    private static string Capsule(string document)
    {
        var lines = document.Replace("\r\n", "\n").Split('\n');
        var start = Array.FindIndex(lines, l => l.StartsWith("artifact_context:", StringComparison.Ordinal));
        start.Should().BeGreaterThan(-1);

        var end = start + 1;
        while (end < lines.Length && (lines[end].Length == 0 || lines[end][0] == ' ' || lines[end][0] == '\t')) end++;
        return string.Join("\n", lines[start..end]);
    }

    /// <summary>One section of a capsule, from its key line to the next key at the same indent.</summary>
    private static string Section(string capsule, string name)
    {
        var lines = capsule.Split('\n');
        var start = Array.FindIndex(lines, l => l.TrimStart().StartsWith(name + ":", StringComparison.Ordinal));
        start.Should().BeGreaterThan(-1, $"the capsule should carry a '{name}' section");

        var indent = lines[start].Length - lines[start].TrimStart().Length;
        var end = start + 1;
        while (end < lines.Length)
        {
            var line = lines[end];
            if (line.Trim().Length != 0 && line.Length - line.TrimStart().Length <= indent) break;
            end++;
        }
        return string.Join("\n", lines[start..end]);
    }
}
```

- [x] **Step 4: Run the tests**

Run: `dotnet test test/Spectacle.Tests/Spectacle.Tests.csproj --filter "FullyQualifiedName~ArtifactContinuityTests"`

Expected: PASS, 10 tests. If a fixture path fails to resolve, confirm the files landed under `bin\Debug\net10.0-windows\Fixtures\artifact-context\`; the `.csproj` copy rule uses `PreserveNewest`, so a stale output directory needs a rebuild.

- [x] **Step 5: Stage**

```bash
git add test/Spectacle.Tests/Fixtures/artifact-context test/Spectacle.Tests/ArtifactContinuityTests.cs
```

---

### Task 7: Stub-CLI process test — the real launch path, executed

**Files:**
- Create: `test/Spectacle.Tests/ArtifactRevisionLaunchTests.cs`

**Interfaces:**
- Consumes: `ClaudeRevisionRunner(string executable)`, its `Completed` event (`EventHandler<ClaudeRunResult>`), `ClaudeArtifactRevisionService(ClaudeRevisionRunner)` (Task 4).
- Produces: nothing consumed by later tasks.

This spawns a real process through the real wrapper — the production constructor, the real `ClaudeProjectRoot.Resolve`, the real `File.ReadAllText`, the real `BuildStartInfo` — with a stub standing in for the CLI. It proves the launch path: the working directory is the resolved project root, the prompt reaches stdin intact with its handoff contract, the target file is edited in place, and the stream decodes into a result. It does not prove a real model merges well; Task 6 covers that half.

The stub is a `.cmd` shim delegating to PowerShell, which also exercises the `cmd.exe` branch of `BuildStartInfo` that the npm install uses.

- [x] **Step 1: Write the failing test**

Create `test/Spectacle.Tests/ArtifactRevisionLaunchTests.cs`:

```csharp
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Spectacle.Ai;
using Xunit;

namespace Spectacle.Tests;

/// <summary>
/// The viewer's launch path, executed rather than asserted: a real process, started by the real
/// service through the real runner, with a stub standing in for the CLI so the run is
/// deterministic, free and needs no credentials.
/// </summary>
public class ArtifactRevisionLaunchTests : IDisposable
{
    private readonly string _temp;
    private readonly string _root;
    private readonly string _docs;
    private readonly string _artifact;
    private readonly string _stub;

    private const string Managed = """
---
title: Poller architecture
artifact_context:
  decisions:
    - decision: Consume changes through a projection reader.
      reason: Only option that can replay after an outage.
  unresolved:
    - Determine the retry interval from telemetry.
---

# Poller architecture

Retries after 10 seconds.
""";

    private const string Revised = """
---
title: Poller architecture
artifact_context:
  decisions:
    - decision: Consume changes through a projection reader.
      reason: Only option that can replay after an outage.
    - decision: Retry after 30 seconds.
      reason: Telemetry showed 10 seconds was too aggressive.
---

# Poller architecture

Retries after 30 seconds.
""";

    public ArtifactRevisionLaunchTests()
    {
        _temp = Path.Combine(Path.GetTempPath(), "spectacle-launch-" + Guid.NewGuid().ToString("n"));
        _root = Path.Combine(_temp, "repo");
        _docs = Path.Combine(_root, "docs");
        Directory.CreateDirectory(Path.Combine(_root, ".claude"));
        Directory.CreateDirectory(Path.Combine(_docs, ".claude")); // must NOT win over the root
        Directory.CreateDirectory(Path.Combine(_root, ".git"));
        File.WriteAllText(Path.Combine(_root, "CLAUDE.md"), "# Project instructions\n");

        _artifact = Path.Combine(_docs, "architecture.md");
        File.WriteAllText(_artifact, Managed);
        File.WriteAllText(Path.Combine(_temp, "revised.md"), Revised);

        _stub = WriteStub();
    }

    /// <summary>
    /// A stand-in for the CLI: it records the working directory it was launched in and the prompt
    /// it received on stdin, rewrites the target file, and emits genuine stream-json.
    /// </summary>
    private string WriteStub()
    {
        var ps1 = Path.Combine(_temp, "stub.ps1");
        File.WriteAllText(ps1, """
$ErrorActionPreference = 'Stop'
$out = $env:SPECTACLE_STUB_OUT
[System.IO.File]::WriteAllText((Join-Path $out 'cwd.txt'), (Get-Location).Path)
[System.IO.File]::WriteAllText((Join-Path $out 'prompt.txt'), [Console]::In.ReadToEnd())
$target = $env:SPECTACLE_STUB_TARGET
[System.IO.File]::WriteAllText($target, [System.IO.File]::ReadAllText((Join-Path $out 'revised.md')))
Write-Output '{"type":"system","subtype":"init","session_id":"stub-1","model":"stub"}'
Write-Output ('{"type":"assistant","message":{"content":[{"type":"tool_use","name":"Edit","input":{"file_path":' + (ConvertTo-Json $target) + '}}]}}')
Write-Output '{"type":"result","subtype":"success","is_error":false,"result":"merged the capsule and applied the ask","num_turns":1,"duration_ms":7,"total_cost_usd":0}'
""");

        // A .cmd shim, which also drives the cmd.exe branch of BuildStartInfo that the npm
        // install of the real CLI goes through.
        var cmd = Path.Combine(_temp, "claude.cmd");
        File.WriteAllText(cmd,
            "@echo off\r\npowershell -NoProfile -ExecutionPolicy Bypass -File \"%~dp0stub.ps1\"\r\n");
        return cmd;
    }

    private ClaudeRunResult Run()
    {
        Environment.SetEnvironmentVariable("SPECTACLE_STUB_OUT", _temp);
        Environment.SetEnvironmentVariable("SPECTACLE_STUB_TARGET", _artifact);

        var runner = new ClaudeRevisionRunner(_stub);
        var finished = new TaskCompletionSource<ClaudeRunResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        runner.Completed += (_, result) => finished.TrySetResult(result);

        var outcome = new ClaudeArtifactRevisionService(runner).Revise(_artifact, _docs, "1. Retry after 30 seconds.");
        outcome.Status.Should().Be(ArtifactRevisionStatus.Started);
        outcome.WorkingDirectory.Should().Be(_root);

        finished.Task.Wait(TimeSpan.FromSeconds(90)).Should().BeTrue("the stub run should finish");
        return finished.Task.Result;
    }

    [Fact]
    public void The_process_runs_in_the_resolved_project_root_not_the_document_folder()
    {
        // docs\.claude exists and must not win: launching there would drop the root's CLAUDE.md,
        // settings, rules and hooks.
        Run();

        var cwd = File.ReadAllText(Path.Combine(_temp, "cwd.txt")).Trim();

        Path.GetFullPath(cwd).Should().BeEquivalentTo(Path.GetFullPath(_root));
    }

    [Fact]
    public void The_prompt_reaches_stdin_carrying_the_handoff_contract_and_the_brief()
    {
        Run();

        var prompt = File.ReadAllText(Path.Combine(_temp, "prompt.txt"));
        prompt.Should().Contain("revise it IN PLACE: " + _artifact);
        prompt.Should().Contain("independent session");
        prompt.Should().Contain("It currently carries: decisions, unresolved.");
        prompt.Should().Contain("no longer belongs under `unresolved`");
        prompt.TrimEnd().Should().EndWith("1. Retry after 30 seconds.");
    }

    [Fact]
    public void The_edit_lands_in_the_open_document_and_the_stream_reports_the_run()
    {
        var result = Run();

        File.ReadAllText(_artifact).Should().Contain("Retries after 30 seconds.");
        result.Succeeded.Should().BeTrue(result.Detail);
        result.Message.Should().Be("merged the capsule and applied the ask");
        result.Stats!.Edits.Should().Be(1);
        result.Stats.SessionId.Should().Be("stub-1");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("SPECTACLE_STUB_OUT", null);
        Environment.SetEnvironmentVariable("SPECTACLE_STUB_TARGET", null);
        try { Directory.Delete(_temp, recursive: true); } catch (IOException) { }
    }
}
```

- [x] **Step 2: Run the test to verify it fails for the right reason**

Run: `dotnet test test/Spectacle.Tests/Spectacle.Tests.csproj --filter "FullyQualifiedName~ArtifactRevisionLaunchTests"`

Expected: PASS if Tasks 1–4 are complete. This task adds no production code — it is the executed proof of the path those tasks built. If it fails:

- `cwd.txt` says the `docs` folder → the outermost-marker rule in Task 1 is wrong.
- `cwd.txt` says a directory above `repo` → the git-root ceiling is not applied.
- The wait times out → the stub did not launch; run the `.cmd` by hand from `_temp` to see the error.
- `Stats.Edits` is `0` → the stub's `assistant` line is malformed JSON; `ClaudeStreamEvent.ParseLine` skips unrecognized lines by contract.

- [x] **Step 3: Run the full suite**

Run: `dotnet test test/Spectacle.Tests/Spectacle.Tests.csproj`

Expected: PASS.

- [x] **Step 4: Stage**

```bash
git add test/Spectacle.Tests/ArtifactRevisionLaunchTests.cs
```

---

### Task 8: The repository's artifact-context policy

**Files:**
- Create: `docs/artifact-context-policy.md`
- Modify: `docs/superpowers/specs/2026-08-24-artifact-context-continuity-design.md` (front matter `status`, acceptance checkboxes)

**Interfaces:**
- Consumes: nothing.
- Produces: nothing.

The prompt tells the agent to follow "the repository artifact-context policy". That policy has to exist as a readable document, or the instruction is a dangling reference — and this repository is itself a project where documents get revised this way.

- [x] **Step 1: Write the policy**

Create `docs/artifact-context-policy.md`:

```markdown
# Artifact context policy

A Markdown artifact revised by an agent carries its own memory in a reserved front-matter
namespace, `artifact_context`. Conversation continuity is not required for document continuity: a
brand-new `claude -p` process handed the file, the repository and a new request must be able to
continue the work correctly, with no `--continue`, no `--resume`, no session id, and no viewer-side
transcript.

## The namespace

```yaml
artifact_context:
  purpose: >
    What this artifact is for, in one or two sentences.
  decisions:
    - decision: The current decision, stated as current.
      reason: Why it holds.
  constraints:
    - Bounds the work must respect.
  unresolved:
    - Questions still open right now.
  evidence:
    - What was measured or observed, when it changes what a future session would do.
  assumptions:
    - What is being taken as true without proof.
  rejected:
    - alternative: What was considered.
      reason: Why it was not chosen.
  history: >
    The material causal history — how the artifact got to its current state, compressed.
```

Every section is optional; a capsule carries the ones that matter. The order above is the order to
write them in: it is optimized for reconstruction, not chronology, so a future session reaches the
current state without replaying every event.

## Rules for a revising session

1. Read the complete file before materially revising it, front matter included.
2. Treat `artifact_context` as authoritative inherited context and the Markdown body as the
   current state of the artifact.
3. Apply the requested revision.
4. Merge what this session materially introduced into the existing capsule and recompress. Merge —
   do not replace, and do not simply append.
5. The capsule is current semantic state plus material causal history, **not an append-only event
   log**. A superseded decision becomes one current decision carrying its reason; the transition
   goes into `history` only when the change itself is materially causal.
6. A question this session answered leaves `unresolved` and its outcome moves into the decision or
   constraint that answers it.
7. Record the request's material intent and reason, not the conversational wording it arrived in,
   unless the exact wording is itself significant.
8. Only `artifact_context` is yours to rewrite. Preserve unrelated front matter.
9. A malformed capsule is repaired conservatively. Never discard one and start over.
10. Do not finish while the artifact is structurally invalid.

## Worked example

A session changes a retry interval that a previous session set. Wrong:

```yaml
decisions:
  - decision: Use a 10-second retry delay.
    reason: Initial conservative value.
  - decision: Use a 30-second retry delay.
    reason: Telemetry.
```

Two contradictory current decisions. Right:

```yaml
decisions:
  - decision: Use a 30-second retry delay.
    reason: Production telemetry showed the original 10-second interval was too aggressive.
history: >
  The retry interval began at a conservative 10 seconds and was raised to 30 after a week of
  production telemetry.
```

## How Spectacle enforces this

Spectacle never writes `artifact_context`. It resolves the artifact's Claude project root and
launches the revision there so this policy and the project's other configuration load; it reads the
namespace and tells the run what it found; and it refuses to revise a document carrying a capsule
when no project root can be established, rather than running a session that cannot see this file.
See `docs/superpowers/specs/2026-08-24-artifact-context-continuity-design.md`.
```

- [x] **Step 2: Mark the design doc's acceptance criteria**

In `docs/superpowers/specs/2026-08-24-artifact-context-continuity-design.md`, change the front matter `status: draft` to `status: implemented`, and tick each acceptance checkbox `- [ ]` to `- [x]`, appending the evidence to each line:

- criterion 1 → `(ArtifactContinuityTests)`
- criterion 2 → `(ClaudeProjectRootTests, ClaudeCliTests.The_launch_never_uses_bare_mode)`
- criterion 3 → `(ArtifactContinuityTests)`
- criterion 4 → `(ArtifactRevisionLaunchTests)`

- [x] **Step 3: Final verification**

Run: `dotnet build test/Spectacle.Tests/Spectacle.Tests.csproj` then `dotnet test test/Spectacle.Tests/Spectacle.Tests.csproj`

Expected: build clean, full suite PASS. Record the actual test count in the completion report — do not claim a pass without the output in hand.

- [x] **Step 4: Stage**

```bash
git add docs/artifact-context-policy.md docs/superpowers/specs/2026-08-24-artifact-context-continuity-design.md docs/superpowers/plans/2026-08-24-artifact-context-continuity.md
```

Then stop. Report what changed and leave everything staged and uncommitted for review.

---

## Follow-ups to raise, not to do

- `README.md` and `QUICKSTART.md` describe the `a` key's behavior and do not mention that a
  managed artifact can now refuse to run. Both files have uncommitted local edits, and the
  repository owner's instruction is to scope changes strictly to what was requested — so ask
  before touching them.
- An advisory gate rule for a malformed `artifact_context` was considered and left out
  deliberately: any new rule changes verdicts on existing documents. `ArtifactContext.Read` already
  returns exactly what such a rule would need, if it is ever wanted.
