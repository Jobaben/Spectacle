# Spectacle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build `Spectacle.exe`, a Windows-only read-only Markdown viewer that renders `.md`/`.markdown` files with VS Code-preview fidelity, dark-themed and WCAG-accessible, registerable as the per-user file handler for Markdown.

**Architecture:** WPF (.NET 8) shell hosts a WebView2 control. A `Document` abstraction owns "markdown text + base directory + change notifications"; v1 ships one implementation backed by a file + `FileSystemWatcher`. A `PreviewPipeline` subscribes to `Document.Changed`, runs Markdig with GFM extensions, wraps output in an embedded HTML shell (dark or high-contrast CSS + bundled Prism.js for code highlighting), and pushes the result to the WebView. A two-column grid layout (editor column collapsed in v1) preserves the seam for future editing.

**Tech Stack:**
- .NET 8 (`net8.0-windows`), WPF (`UseWPF=true`)
- `Markdig` (Markdown → HTML)
- `Microsoft.Web.WebView2` (Chromium-based rendering)
- xUnit (tests), FluentAssertions (assertions)
- Prism.js (vendored, no CDN) for syntax highlighting

---

## File Map

```
Spectacle/
  .gitignore
  Spectacle.sln
  README.md
  Directory.Build.props                              # shared TFM, Nullable, LangVersion
  src/Spectacle/
    Spectacle.csproj
    Program.cs                                       # entry point, dispatches CLI to actions
    App.xaml(.cs)                                    # WPF application
    MainWindow.xaml(.cs)                             # window with 3-column grid
    Cli/CliArgs.cs                                   # argv parser → discriminated record
    Files/FileGuard.cs                               # .md/.markdown allowlist
    Documents/Document.cs                            # abstract: Text, BaseDirectory, Changed
    Documents/FileDocument.cs                        # Document backed by a file + watcher
    Documents/DebouncedFileWatcher.cs                # 150ms debounce wrapper
    Render/MdRenderer.cs                             # Markdig pipeline → HTML
    Render/PreviewHtml.cs                            # compose body + shell (CSS, JS, base)
    Render/PreviewPipeline.cs                        # Document.Changed → render → WebView
    Render/Assets/preview.css                        # layout, typography, semantic rules
    Render/Assets/dark.css                           # Dark+ palette (default)
    Render/Assets/hc.css                             # high-contrast variant
    Render/Assets/prism.min.js                       # vendored, curated language set
    Render/Assets/prism.css                          # Prism Dark+ token colors
    Web/WebViewHost.xaml(.cs)                        # UserControl wrapping WebView2
    Web/LinkInterceptor.cs                           # decide internal vs external nav
    Editor/EditorHost.xaml(.cs)                      # empty placeholder for future editor
    Theme/HighContrastWatcher.cs                     # detects Windows HC mode
    Install/FileAssocInstaller.cs                    # HKCU register/unregister
    Accessibility/WcagContrast.cs                    # relative luminance, contrast ratio
  test/Spectacle.Tests/
    Spectacle.Tests.csproj
    CliArgsTests.cs
    FileGuardTests.cs
    MdRendererTests.cs
    FileDocumentTests.cs
    PreviewPipelineTests.cs
    LinkInterceptorTests.cs
    WcagContrastTests.cs
    PaletteContrastTests.cs
    FileAssocInstallerTests.cs
    Fixtures/                                        # markdown + expected-html pairs
      tables.md, tables.html
      code.md, code.html
      task-list.md, task-list.html
      nested-lists.md, nested-lists.html
      footnotes.md, footnotes.html
      images.md, images.html
```

Tests for WPF/WebView2-bound classes (`MainWindow`, `WebViewHost`, `EditorHost`, `App`) are smoke-tested manually per the spec §12. Pure logic is unit-tested.

---

## Task 1: Repository Scaffolding

**Files:**
- Create: `.gitignore`
- Create: `Directory.Build.props`
- Create: `Spectacle.sln`
- Create: `src/Spectacle/Spectacle.csproj`
- Create: `src/Spectacle/Program.cs` (placeholder)
- Create: `test/Spectacle.Tests/Spectacle.Tests.csproj`
- Create: `test/Spectacle.Tests/SmokeTests.cs`

- [ ] **Step 1.1: Write `.gitignore`**

```
bin/
obj/
*.user
*.suo
.vs/
publish/
TestResults/
```

- [ ] **Step 1.2: Write `Directory.Build.props`**

```xml
<Project>
  <PropertyGroup>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
```

- [ ] **Step 1.3: Write `src/Spectacle/Spectacle.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <AssemblyName>Spectacle</AssemblyName>
    <RootNamespace>Spectacle</RootNamespace>
    <ApplicationManifest>app.manifest</ApplicationManifest>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Markdig" Version="0.37.0" />
    <PackageReference Include="Microsoft.Web.WebView2" Version="1.0.2592.51" />
  </ItemGroup>
</Project>
```

- [ ] **Step 1.4: Write `src/Spectacle/app.manifest`**

```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <assemblyIdentity version="1.0.0.0" name="Spectacle"/>
  <application xmlns="urn:schemas-microsoft-com:asm.v3">
    <windowsSettings>
      <dpiAware xmlns="http://schemas.microsoft.com/SMI/2005/WindowsSettings">true/pm</dpiAware>
      <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>
    </windowsSettings>
  </application>
</assembly>
```

- [ ] **Step 1.5: Write `src/Spectacle/Program.cs` (placeholder so the project builds)**

```csharp
namespace Spectacle;

public static class Program
{
    [STAThread]
    public static int Main(string[] args) => 0;
}
```

- [ ] **Step 1.6: Write `test/Spectacle.Tests/Spectacle.Tests.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <IsPackable>false</IsPackable>
    <UseWPF>true</UseWPF>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.10.0" />
    <PackageReference Include="xunit" Version="2.9.0" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageReference Include="FluentAssertions" Version="6.12.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Spectacle\Spectacle.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 1.7: Write `test/Spectacle.Tests/SmokeTests.cs`**

```csharp
namespace Spectacle.Tests;

public class SmokeTests
{
    [Fact]
    public void ProjectsCompile() => Spectacle.Program.Main(Array.Empty<string>()).Should().Be(0);
}
```

- [ ] **Step 1.8: Create solution**

Run:
```powershell
dotnet new sln -n Spectacle
dotnet sln add src/Spectacle/Spectacle.csproj
dotnet sln add test/Spectacle.Tests/Spectacle.Tests.csproj
```

- [ ] **Step 1.9: Build and test**

Run:
```powershell
dotnet build
dotnet test
```

Expected: `Build succeeded`, `Passed: 1`.

- [ ] **Step 1.10: Commit**

```powershell
git add .
git commit -m "chore: scaffold Spectacle solution"
```

---

## Task 2: CliArgs

**Files:**
- Create: `src/Spectacle/Cli/CliArgs.cs`
- Create: `test/Spectacle.Tests/CliArgsTests.cs`

- [ ] **Step 2.1: Write the failing tests**

`test/Spectacle.Tests/CliArgsTests.cs`:
```csharp
using Spectacle.Cli;

namespace Spectacle.Tests;

public class CliArgsTests
{
    [Fact]
    public void Empty_args_is_Help() =>
        CliArgs.Parse(Array.Empty<string>()).Should().BeOfType<CliCommand.Help>();

    [Fact]
    public void Dash_help_is_Help() =>
        CliArgs.Parse(new[] { "--help" }).Should().BeOfType<CliCommand.Help>();

    [Fact]
    public void Dash_h_is_Help() =>
        CliArgs.Parse(new[] { "-h" }).Should().BeOfType<CliCommand.Help>();

    [Fact]
    public void Dash_version_is_Version() =>
        CliArgs.Parse(new[] { "--version" }).Should().BeOfType<CliCommand.Version>();

    [Fact]
    public void Dash_register_is_Register() =>
        CliArgs.Parse(new[] { "--register" }).Should().BeOfType<CliCommand.Register>();

    [Fact]
    public void Dash_unregister_is_Unregister() =>
        CliArgs.Parse(new[] { "--unregister" }).Should().BeOfType<CliCommand.Unregister>();

    [Fact]
    public void File_path_is_Open()
    {
        var result = CliArgs.Parse(new[] { @"C:\docs\readme.md" });
        result.Should().BeOfType<CliCommand.Open>()
            .Which.Path.Should().Be(@"C:\docs\readme.md");
    }

    [Fact]
    public void Unknown_flag_is_Help() =>
        CliArgs.Parse(new[] { "--what" }).Should().BeOfType<CliCommand.Help>();

    [Fact]
    public void Multiple_positionals_uses_first()
    {
        var result = CliArgs.Parse(new[] { "a.md", "b.md" });
        result.Should().BeOfType<CliCommand.Open>().Which.Path.Should().Be("a.md");
    }
}
```

- [ ] **Step 2.2: Run tests to verify they fail**

Run:
```powershell
dotnet test --filter FullyQualifiedName~CliArgsTests
```
Expected: FAIL (`CliArgs` and `CliCommand` not found).

- [ ] **Step 2.3: Write `src/Spectacle/Cli/CliArgs.cs`**

```csharp
namespace Spectacle.Cli;

public abstract record CliCommand
{
    public sealed record Open(string Path) : CliCommand;
    public sealed record Register : CliCommand;
    public sealed record Unregister : CliCommand;
    public sealed record Help : CliCommand;
    public sealed record Version : CliCommand;
}

public static class CliArgs
{
    public static CliCommand Parse(string[] args)
    {
        if (args.Length == 0) return new CliCommand.Help();

        var first = args[0];
        return first switch
        {
            "-h" or "--help" => new CliCommand.Help(),
            "--version" => new CliCommand.Version(),
            "--register" => new CliCommand.Register(),
            "--unregister" => new CliCommand.Unregister(),
            _ when first.StartsWith('-') => new CliCommand.Help(),
            _ => new CliCommand.Open(first),
        };
    }
}
```

- [ ] **Step 2.4: Run tests to verify they pass**

Run:
```powershell
dotnet test --filter FullyQualifiedName~CliArgsTests
```
Expected: PASS (9 tests).

- [ ] **Step 2.5: Commit**

```powershell
git add src/Spectacle/Cli test/Spectacle.Tests/CliArgsTests.cs
git commit -m "feat(cli): parse argv into CliCommand variants"
```

---

## Task 3: FileGuard

**Files:**
- Create: `src/Spectacle/Files/FileGuard.cs`
- Create: `test/Spectacle.Tests/FileGuardTests.cs`

- [ ] **Step 3.1: Write the failing tests**

`test/Spectacle.Tests/FileGuardTests.cs`:
```csharp
using Spectacle.Files;

namespace Spectacle.Tests;

public class FileGuardTests
{
    [Theory]
    [InlineData("readme.md")]
    [InlineData("README.MD")]
    [InlineData("notes.markdown")]
    [InlineData("notes.MarkDown")]
    [InlineData(@"C:\path\to\file.md")]
    public void Accepts_markdown_extensions(string path) =>
        FileGuard.IsAllowed(path).Should().BeTrue();

    [Theory]
    [InlineData("readme.txt")]
    [InlineData("script.ps1")]
    [InlineData("noext")]
    [InlineData("file.md.bak")]
    [InlineData("file.mdx")]
    public void Rejects_other_extensions(string path) =>
        FileGuard.IsAllowed(path).Should().BeFalse();

    [Fact]
    public void Rejects_null() =>
        FileGuard.IsAllowed(null!).Should().BeFalse();

    [Fact]
    public void Rejects_empty() =>
        FileGuard.IsAllowed("").Should().BeFalse();
}
```

- [ ] **Step 3.2: Run tests to verify they fail**

Run:
```powershell
dotnet test --filter FullyQualifiedName~FileGuardTests
```
Expected: FAIL (`FileGuard` not found).

- [ ] **Step 3.3: Write `src/Spectacle/Files/FileGuard.cs`**

```csharp
namespace Spectacle.Files;

public static class FileGuard
{
    private static readonly string[] Allowed = { ".md", ".markdown" };

    public static bool IsAllowed(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var ext = System.IO.Path.GetExtension(path);
        return Allowed.Any(a => string.Equals(a, ext, StringComparison.OrdinalIgnoreCase));
    }
}
```

- [ ] **Step 3.4: Run tests to verify they pass**

Run:
```powershell
dotnet test --filter FullyQualifiedName~FileGuardTests
```
Expected: PASS.

- [ ] **Step 3.5: Commit**

```powershell
git add src/Spectacle/Files test/Spectacle.Tests/FileGuardTests.cs
git commit -m "feat(files): allowlist .md and .markdown extensions"
```

---

## Task 4: Document Abstraction + FileDocument

**Files:**
- Create: `src/Spectacle/Documents/Document.cs`
- Create: `src/Spectacle/Documents/DebouncedFileWatcher.cs`
- Create: `src/Spectacle/Documents/FileDocument.cs`
- Create: `test/Spectacle.Tests/FileDocumentTests.cs`

- [ ] **Step 4.1: Write the failing tests**

`test/Spectacle.Tests/FileDocumentTests.cs`:
```csharp
using Spectacle.Documents;

namespace Spectacle.Tests;

public class FileDocumentTests : IDisposable
{
    private readonly string _path;

    public FileDocumentTests()
    {
        _path = Path.Combine(Path.GetTempPath(), $"spectacle-{Guid.NewGuid():N}.md");
        File.WriteAllText(_path, "# hello");
    }

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Fact]
    public void Reads_initial_text()
    {
        using var doc = FileDocument.Open(_path);
        doc.Text.Should().Be("# hello");
    }

    [Fact]
    public void BaseDirectory_is_parent_of_file()
    {
        using var doc = FileDocument.Open(_path);
        doc.BaseDirectory.Should().Be(Path.GetDirectoryName(_path));
    }

    [Fact]
    public async Task Changed_fires_on_save()
    {
        using var doc = FileDocument.Open(_path);
        var tcs = new TaskCompletionSource();
        doc.Changed += (_, _) => tcs.TrySetResult();

        await Task.Delay(50); // let the watcher settle
        File.WriteAllText(_path, "# updated");

        var fired = await Task.WhenAny(tcs.Task, Task.Delay(2000));
        fired.Should().Be(tcs.Task, "Changed should fire within 2s of a write");
        doc.Text.Should().Be("# updated");
    }
}
```

- [ ] **Step 4.2: Run tests to verify they fail**

Run:
```powershell
dotnet test --filter FullyQualifiedName~FileDocumentTests
```
Expected: FAIL (types not found).

- [ ] **Step 4.3: Write `src/Spectacle/Documents/Document.cs`**

```csharp
namespace Spectacle.Documents;

public abstract class Document : IDisposable
{
    public abstract string Text { get; }
    public abstract string BaseDirectory { get; }
    public event EventHandler? Changed;

    protected void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);

    public virtual void Dispose() => GC.SuppressFinalize(this);
}
```

- [ ] **Step 4.4: Write `src/Spectacle/Documents/DebouncedFileWatcher.cs`**

```csharp
namespace Spectacle.Documents;

internal sealed class DebouncedFileWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly System.Threading.Timer _timer;
    private readonly Action _onChanged;
    private const int DebounceMs = 150;

    public DebouncedFileWatcher(string fullPath, Action onChanged)
    {
        _onChanged = onChanged;
        var dir = Path.GetDirectoryName(fullPath)!;
        var name = Path.GetFileName(fullPath);
        _watcher = new FileSystemWatcher(dir, name)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        _watcher.Changed += OnRaw;
        _watcher.Created += OnRaw;
        _watcher.Renamed += OnRaw;
        _timer = new System.Threading.Timer(_ => _onChanged(), null, Timeout.Infinite, Timeout.Infinite);
    }

    private void OnRaw(object sender, FileSystemEventArgs e) =>
        _timer.Change(DebounceMs, Timeout.Infinite);

    public void Dispose()
    {
        _watcher.Dispose();
        _timer.Dispose();
    }
}
```

- [ ] **Step 4.5: Write `src/Spectacle/Documents/FileDocument.cs`**

```csharp
namespace Spectacle.Documents;

public sealed class FileDocument : Document
{
    private readonly string _path;
    private readonly DebouncedFileWatcher _watcher;
    private string _text;

    private FileDocument(string path, string text)
    {
        _path = path;
        _text = text;
        _watcher = new DebouncedFileWatcher(path, ReloadAndNotify);
    }

    public static FileDocument Open(string path)
    {
        var full = Path.GetFullPath(path);
        return new FileDocument(full, ReadAllTextSafely(full));
    }

    public override string Text => _text;
    public override string BaseDirectory => Path.GetDirectoryName(_path)!;

    private void ReloadAndNotify()
    {
        try { _text = ReadAllTextSafely(_path); }
        catch (IOException) { return; } // editor still writing — let next debounce catch it
        OnChanged();
    }

    private static string ReadAllTextSafely(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var sr = new StreamReader(fs);
        return sr.ReadToEnd();
    }

    public override void Dispose()
    {
        _watcher.Dispose();
        base.Dispose();
    }
}
```

- [ ] **Step 4.6: Run tests to verify they pass**

Run:
```powershell
dotnet test --filter FullyQualifiedName~FileDocumentTests
```
Expected: PASS (3 tests). If the Changed test is flaky on slower disks, increase the timeout in the test to 4000 ms — do not change debounce in production code.

- [ ] **Step 4.7: Commit**

```powershell
git add src/Spectacle/Documents test/Spectacle.Tests/FileDocumentTests.cs
git commit -m "feat(documents): Document abstraction + FileDocument with debounced watcher"
```

---

## Task 5: MdRenderer

**Files:**
- Create: `src/Spectacle/Render/MdRenderer.cs`
- Create: `test/Spectacle.Tests/Fixtures/tables.md`, `tables.html`
- Create: `test/Spectacle.Tests/Fixtures/code.md`, `code.html`
- Create: `test/Spectacle.Tests/Fixtures/task-list.md`, `task-list.html`
- Create: `test/Spectacle.Tests/Fixtures/footnotes.md`, `footnotes.html`
- Create: `test/Spectacle.Tests/MdRendererTests.cs`
- Modify: `test/Spectacle.Tests/Spectacle.Tests.csproj` (copy fixtures to output)

- [ ] **Step 5.1: Modify `test/Spectacle.Tests/Spectacle.Tests.csproj` to copy fixtures**

Add inside `<Project>`:
```xml
<ItemGroup>
  <None Update="Fixtures\**\*.*">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
</ItemGroup>
```

- [ ] **Step 5.2: Write the failing tests**

`test/Spectacle.Tests/MdRendererTests.cs`:
```csharp
using Spectacle.Render;

namespace Spectacle.Tests;

public class MdRendererTests
{
    [Theory]
    [InlineData("tables")]
    [InlineData("code")]
    [InlineData("task-list")]
    [InlineData("footnotes")]
    public void Renders_fixture(string name)
    {
        var md = File.ReadAllText(Path.Combine("Fixtures", $"{name}.md"));
        var expected = File.ReadAllText(Path.Combine("Fixtures", $"{name}.html")).Trim();
        var actual = new MdRenderer().ToHtml(md).Trim();
        actual.Should().Be(expected);
    }

    [Fact]
    public void Code_block_gets_language_class()
    {
        var html = new MdRenderer().ToHtml("```cs\nvar x = 1;\n```\n");
        html.Should().Contain("class=\"language-cs\"");
    }

    [Fact]
    public void Soft_break_is_not_hard_break()
    {
        var html = new MdRenderer().ToHtml("line1\nline2\n");
        html.Should().NotContain("<br");
    }
}
```

- [ ] **Step 5.3: Write fixtures**

`test/Spectacle.Tests/Fixtures/tables.md`:
````
| a | b |
|---|---|
| 1 | 2 |
````

`test/Spectacle.Tests/Fixtures/tables.html`:
```html
<table>
<thead>
<tr>
<th>a</th>
<th>b</th>
</tr>
</thead>
<tbody>
<tr>
<td>1</td>
<td>2</td>
</tr>
</tbody>
</table>
```

`test/Spectacle.Tests/Fixtures/code.md`:
````
```cs
var x = 1;
```
````

`test/Spectacle.Tests/Fixtures/code.html`:
```html
<pre><code class="language-cs">var x = 1;
</code></pre>
```

`test/Spectacle.Tests/Fixtures/task-list.md`:
```
- [x] done
- [ ] todo
```

`test/Spectacle.Tests/Fixtures/task-list.html`:
```html
<ul class="contains-task-list">
<li class="task-list-item"><input type="checkbox" disabled="disabled" checked="checked" /> done</li>
<li class="task-list-item"><input type="checkbox" disabled="disabled" /> todo</li>
</ul>
```

`test/Spectacle.Tests/Fixtures/footnotes.md`:
```
Here[^1].

[^1]: note
```

`test/Spectacle.Tests/Fixtures/footnotes.html`:
```html
<p>Here<a id="fnref:1" href="#fn:1" class="footnote-ref"><sup>1</sup></a>.</p>
<div class="footnotes" role="doc-endnotes">
<hr />
<ol>
<li id="fn:1">
<p>note <a href="#fnref:1" class="footnote-back-ref">&#8617;</a></p>
</li>
</ol>
</div>
```

- [ ] **Step 5.4: Run tests to verify they fail**

Run:
```powershell
dotnet test --filter FullyQualifiedName~MdRendererTests
```
Expected: FAIL (`MdRenderer` not found).

- [ ] **Step 5.5: Write `src/Spectacle/Render/MdRenderer.cs`**

```csharp
using Markdig;

namespace Spectacle.Render;

public sealed class MdRenderer
{
    private readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseEmojiAndSmiley()
        .UseAutoIdentifiers()
        .Build();

    public string ToHtml(string markdown) => Markdown.ToHtml(markdown ?? string.Empty, _pipeline);
}
```

- [ ] **Step 5.6: Run tests; reconcile fixtures with actual Markdig output**

Run:
```powershell
dotnet test --filter FullyQualifiedName~MdRendererTests
```

Markdig's exact byte-for-byte output may differ from the fixtures above (whitespace, attribute order). If a test fails, **inspect the actual output** with a temporary `Console.WriteLine(actual)` or by reading the failure message, then update the **fixture** to match — the renderer's behavior is the source of truth, the fixtures are the snapshot. Do not loosen the equality check.

Iterate until all fixture tests pass.

Expected after iteration: PASS (6 tests).

- [ ] **Step 5.7: Commit**

```powershell
git add src/Spectacle/Render/MdRenderer.cs test/Spectacle.Tests/Fixtures test/Spectacle.Tests/MdRendererTests.cs test/Spectacle.Tests/Spectacle.Tests.csproj
git commit -m "feat(render): Markdig-based renderer with GFM fixture tests"
```

---

## Task 6: WcagContrast Helper

**Files:**
- Create: `src/Spectacle/Accessibility/WcagContrast.cs`
- Create: `test/Spectacle.Tests/WcagContrastTests.cs`

- [ ] **Step 6.1: Write the failing tests**

`test/Spectacle.Tests/WcagContrastTests.cs`:
```csharp
using Spectacle.Accessibility;

namespace Spectacle.Tests;

public class WcagContrastTests
{
    [Fact]
    public void White_on_black_is_21() =>
        WcagContrast.Ratio("#ffffff", "#000000").Should().BeApproximately(21.0, 0.01);

    [Fact]
    public void Black_on_black_is_1() =>
        WcagContrast.Ratio("#000000", "#000000").Should().BeApproximately(1.0, 0.01);

    [Fact]
    public void DarkPlus_body_exceeds_AAA()
    {
        // #d4d4d4 on #1e1e1e is the VS Code Dark+ body pair
        var ratio = WcagContrast.Ratio("#d4d4d4", "#1e1e1e");
        ratio.Should().BeGreaterThan(7.0);
    }

    [Fact]
    public void Throws_on_bad_hex() =>
        FluentActions.Invoking(() => WcagContrast.Ratio("not-a-color", "#000000"))
            .Should().Throw<FormatException>();

    [Fact]
    public void Accepts_uppercase_and_short_form()
    {
        WcagContrast.Ratio("#FFF", "#000").Should().BeApproximately(21.0, 0.01);
    }
}
```

- [ ] **Step 6.2: Run tests to verify they fail**

Run:
```powershell
dotnet test --filter FullyQualifiedName~WcagContrastTests
```
Expected: FAIL.

- [ ] **Step 6.3: Write `src/Spectacle/Accessibility/WcagContrast.cs`**

```csharp
using System.Globalization;

namespace Spectacle.Accessibility;

public static class WcagContrast
{
    public static double Ratio(string fgHex, string bgHex)
    {
        var l1 = RelativeLuminance(fgHex);
        var l2 = RelativeLuminance(bgHex);
        var (lighter, darker) = l1 >= l2 ? (l1, l2) : (l2, l1);
        return (lighter + 0.05) / (darker + 0.05);
    }

    public static double RelativeLuminance(string hex)
    {
        var (r, g, b) = ParseHex(hex);
        double lin(int c)
        {
            var s = c / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * lin(r) + 0.7152 * lin(g) + 0.0722 * lin(b);
    }

    private static (int r, int g, int b) ParseHex(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex) || hex[0] != '#')
            throw new FormatException($"Expected #RGB or #RRGGBB, got '{hex}'.");
        var body = hex[1..];
        if (body.Length == 3) body = string.Concat(body.Select(c => $"{c}{c}"));
        if (body.Length != 6 || !body.All(IsHex))
            throw new FormatException($"Expected #RGB or #RRGGBB, got '{hex}'.");
        return (
            int.Parse(body[0..2], NumberStyles.HexNumber),
            int.Parse(body[2..4], NumberStyles.HexNumber),
            int.Parse(body[4..6], NumberStyles.HexNumber));
    }

    private static bool IsHex(char c) =>
        (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
}
```

- [ ] **Step 6.4: Run tests to verify they pass**

Run:
```powershell
dotnet test --filter FullyQualifiedName~WcagContrastTests
```
Expected: PASS (5 tests).

- [ ] **Step 6.5: Commit**

```powershell
git add src/Spectacle/Accessibility test/Spectacle.Tests/WcagContrastTests.cs
git commit -m "feat(a11y): WCAG relative-luminance contrast calculator"
```

---

## Task 7: CSS Assets + Prism Vendoring

**Files:**
- Create: `src/Spectacle/Render/Assets/preview.css`
- Create: `src/Spectacle/Render/Assets/dark.css`
- Create: `src/Spectacle/Render/Assets/hc.css`
- Create: `src/Spectacle/Render/Assets/prism.min.js`
- Create: `src/Spectacle/Render/Assets/prism.css`
- Modify: `src/Spectacle/Spectacle.csproj` (embed assets as resources)

This task ships static assets only. No unit tests — the next task validates the palette via `WcagContrast`.

- [ ] **Step 7.1: Write `src/Spectacle/Render/Assets/preview.css`**

```css
:root {
  color-scheme: dark;
}
html, body {
  margin: 0;
  padding: 0;
  font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", "Segoe UI Variable",
               Roboto, "Helvetica Neue", Arial, sans-serif;
  font-size: 16px;
  line-height: 1.6;
}
main {
  max-width: 980px;
  margin: 0 auto;
  padding: 32px 48px 64px;
}
h1, h2, h3, h4, h5, h6 { line-height: 1.25; margin-top: 1.6em; margin-bottom: 0.6em; }
h1 { font-size: 2em; border-bottom: 1px solid var(--rule); padding-bottom: .3em; }
h2 { font-size: 1.5em; border-bottom: 1px solid var(--rule); padding-bottom: .3em; }
h3 { font-size: 1.25em; }
p, ul, ol, blockquote, pre, table { margin: 0 0 1em; }
a { color: var(--link); text-decoration: underline; text-underline-offset: 2px; }
a:focus-visible { outline: 2px solid var(--focus); outline-offset: 2px; border-radius: 2px; }
code { font-family: Consolas, "Cascadia Code", "Fira Code", monospace; font-size: 0.92em;
       background: var(--code-bg); padding: 2px 6px; border-radius: 4px; }
pre { background: var(--code-bg); padding: 14px 16px; border-radius: 6px; overflow-x: auto; }
pre code { background: transparent; padding: 0; font-size: 0.92em; }
blockquote { border-left: 4px solid var(--rule); padding: 0 1em; color: var(--muted); }
table { border-collapse: collapse; }
th, td { border: 1px solid var(--rule); padding: 6px 12px; }
th { background: var(--code-bg); font-weight: 600; }
img { max-width: 100%; height: auto; }
hr { border: 0; border-top: 1px solid var(--rule); margin: 2em 0; }
input[type="checkbox"] { margin-right: .4em; }
.contains-task-list { list-style: none; padding-left: 1em; }
.footnote-ref sup { font-size: .8em; }
@media (prefers-reduced-motion: no-preference) { /* no animations defined */ }
@media (forced-colors: active) {
  :root { color-scheme: only light; }
  a { color: LinkText; }
  pre, code { background: Canvas; border: 1px solid CanvasText; }
}
```

- [ ] **Step 7.2: Write `src/Spectacle/Render/Assets/dark.css`**

```css
:root {
  --bg: #1e1e1e;
  --fg: #d4d4d4;
  --muted: #9da5b4;
  --link: #4ea1ff;
  --focus: #7cb7ff;
  --rule: #3c3c3c;
  --code-bg: #252526;
}
html, body { background: var(--bg); color: var(--fg); }
```

- [ ] **Step 7.3: Write `src/Spectacle/Render/Assets/hc.css`**

```css
:root {
  --bg: #000000;
  --fg: #ffffff;
  --muted: #ffffff;
  --link: #ffff00;
  --focus: #ffff00;
  --rule: #ffffff;
  --code-bg: #000000;
}
html, body { background: var(--bg); color: var(--fg); }
pre, code { border: 1px solid var(--fg); }
```

- [ ] **Step 7.4: Vendor Prism**

Go to https://prismjs.com/download.html and configure:
- **Theme:** Default
- **Languages (check):** Markup, CSS, C-like, JavaScript, TypeScript, Bash, C#, JSON, Markdown, PowerShell, SQL, YAML, XML
- **Plugins:** none

Click **Download JS** → save as `src/Spectacle/Render/Assets/prism.min.js`.
Click **Download CSS** → save as `src/Spectacle/Render/Assets/prism.css`.

Then overwrite the top of `prism.css` with this dark-palette override so it matches the spec:

Append to `src/Spectacle/Render/Assets/prism.css` (after the original content):
```css
code[class*="language-"], pre[class*="language-"] {
  color: #d4d4d4; background: #1e1e1e; text-shadow: none;
}
:not(pre) > code[class*="language-"], pre[class*="language-"] {
  background: #1e1e1e;
}
.token.comment, .token.prolog, .token.doctype, .token.cdata { color: #6a9955; }
.token.punctuation { color: #d4d4d4; }
.token.property, .token.tag, .token.boolean, .token.number,
.token.constant, .token.symbol, .token.deleted { color: #b5cea8; }
.token.selector, .token.attr-name, .token.string, .token.char,
.token.builtin, .token.inserted { color: #ce9178; }
.token.operator, .token.entity, .token.url, .language-css .token.string,
.style .token.string { color: #d4d4d4; }
.token.atrule, .token.attr-value, .token.keyword { color: #569cd6; }
.token.function, .token.class-name { color: #dcdcaa; }
.token.regex, .token.important, .token.variable { color: #d16969; }
```

- [ ] **Step 7.5: Modify `src/Spectacle/Spectacle.csproj` to embed assets**

Add inside the existing `<Project>`:
```xml
<ItemGroup>
  <EmbeddedResource Include="Render\Assets\preview.css" />
  <EmbeddedResource Include="Render\Assets\dark.css" />
  <EmbeddedResource Include="Render\Assets\hc.css" />
  <EmbeddedResource Include="Render\Assets\prism.min.js" />
  <EmbeddedResource Include="Render\Assets\prism.css" />
</ItemGroup>
```

- [ ] **Step 7.6: Build**

Run:
```powershell
dotnet build
```
Expected: `Build succeeded`.

- [ ] **Step 7.7: Commit**

```powershell
git add src/Spectacle/Render/Assets src/Spectacle/Spectacle.csproj
git commit -m "feat(render): vendor CSS + Prism.js assets, embed as resources"
```

---

## Task 8: PaletteContrastTests

**Files:**
- Create: `test/Spectacle.Tests/PaletteContrastTests.cs`

This task validates that the actual hex values in the palette meet WCAG. The pairs come from `dark.css` and `hc.css`.

- [ ] **Step 8.1: Write the tests**

`test/Spectacle.Tests/PaletteContrastTests.cs`:
```csharp
using Spectacle.Accessibility;

namespace Spectacle.Tests;

public class PaletteContrastTests
{
    // Dark+ palette from src/Spectacle/Render/Assets/dark.css
    private const string DarkBg = "#1e1e1e";
    private const string DarkFg = "#d4d4d4";
    private const string DarkLink = "#4ea1ff";
    private const string DarkFocus = "#7cb7ff";
    private const string DarkMuted = "#9da5b4";
    private const string DarkCodeBg = "#252526";

    // High-contrast palette from src/Spectacle/Render/Assets/hc.css
    private const string HcBg = "#000000";
    private const string HcFg = "#ffffff";
    private const string HcLink = "#ffff00";

    [Fact]
    public void Dark_body_meets_AAA() =>
        WcagContrast.Ratio(DarkFg, DarkBg).Should().BeGreaterThanOrEqualTo(7.0);

    [Fact]
    public void Dark_link_meets_AA() =>
        WcagContrast.Ratio(DarkLink, DarkBg).Should().BeGreaterThanOrEqualTo(4.5);

    [Fact]
    public void Dark_focus_outline_meets_AA() =>
        WcagContrast.Ratio(DarkFocus, DarkBg).Should().BeGreaterThanOrEqualTo(3.0);

    [Fact]
    public void Dark_muted_meets_AA() =>
        WcagContrast.Ratio(DarkMuted, DarkBg).Should().BeGreaterThanOrEqualTo(4.5);

    [Fact]
    public void Dark_body_on_code_bg_meets_AAA() =>
        WcagContrast.Ratio(DarkFg, DarkCodeBg).Should().BeGreaterThanOrEqualTo(7.0);

    [Fact]
    public void Hc_body_meets_AAA() =>
        WcagContrast.Ratio(HcFg, HcBg).Should().BeGreaterThanOrEqualTo(7.0);

    [Fact]
    public void Hc_link_meets_AA() =>
        WcagContrast.Ratio(HcLink, HcBg).Should().BeGreaterThanOrEqualTo(4.5);
}
```

- [ ] **Step 8.2: Run tests**

Run:
```powershell
dotnet test --filter FullyQualifiedName~PaletteContrastTests
```
Expected: PASS (7 tests). If any fail, **adjust the hex in the corresponding `.css` file** to meet the threshold, then update the constants in this test to match the new value. The CSS palette is the contract — if a hex needs to change for contrast, change it in both places.

- [ ] **Step 8.3: Commit**

```powershell
git add test/Spectacle.Tests/PaletteContrastTests.cs
git commit -m "test(a11y): assert palette contrast against WCAG AA/AAA"
```

---

## Task 9: PreviewHtml

**Files:**
- Create: `src/Spectacle/Render/PreviewHtml.cs`
- Create: `test/Spectacle.Tests/PreviewHtmlTests.cs`

- [ ] **Step 9.1: Write the failing tests**

`test/Spectacle.Tests/PreviewHtmlTests.cs`:
```csharp
using Spectacle.Render;

namespace Spectacle.Tests;

public class PreviewHtmlTests
{
    [Fact]
    public void Wraps_body_in_main_landmark()
    {
        var html = PreviewHtml.Build("<p>hi</p>", "https://spectacle.local/", PreviewTheme.Dark);
        html.Should().Contain("<main role=\"main\">").And.Contain("<p>hi</p>").And.Contain("</main>");
    }

    [Fact]
    public void Embeds_preview_css() =>
        PreviewHtml.Build("", "x", PreviewTheme.Dark).Should().Contain("--bg:");

    [Fact]
    public void Dark_theme_includes_dark_css() =>
        PreviewHtml.Build("", "x", PreviewTheme.Dark).Should().Contain("#1e1e1e");

    [Fact]
    public void HighContrast_theme_includes_hc_css() =>
        PreviewHtml.Build("", "x", PreviewTheme.HighContrast).Should().Contain("#ffff00");

    [Fact]
    public void Sets_base_href()
    {
        var html = PreviewHtml.Build("", "https://spectacle.local/", PreviewTheme.Dark);
        html.Should().Contain("<base href=\"https://spectacle.local/\"");
    }

    [Fact]
    public void Includes_prism_script() =>
        PreviewHtml.Build("", "x", PreviewTheme.Dark).Should().Contain("Prism");
}
```

- [ ] **Step 9.2: Run tests to verify they fail**

Run:
```powershell
dotnet test --filter FullyQualifiedName~PreviewHtmlTests
```
Expected: FAIL.

- [ ] **Step 9.3: Write `src/Spectacle/Render/PreviewHtml.cs`**

```csharp
using System.Reflection;

namespace Spectacle.Render;

public enum PreviewTheme { Dark, HighContrast }

public static class PreviewHtml
{
    private static readonly Lazy<string> PreviewCss = new(() => LoadAsset("preview.css"));
    private static readonly Lazy<string> DarkCss = new(() => LoadAsset("dark.css"));
    private static readonly Lazy<string> HcCss = new(() => LoadAsset("hc.css"));
    private static readonly Lazy<string> PrismCss = new(() => LoadAsset("prism.css"));
    private static readonly Lazy<string> PrismJs = new(() => LoadAsset("prism.min.js"));

    public static string Build(string bodyHtml, string baseHref, PreviewTheme theme)
    {
        var themeCss = theme == PreviewTheme.HighContrast ? HcCss.Value : DarkCss.Value;
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
            </head>
            <body>
              <main role="main">
            {{bodyHtml}}
              </main>
              <script>{{PrismJs.Value}}</script>
            </body>
            </html>
            """;
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

- [ ] **Step 9.4: Run tests to verify they pass**

Run:
```powershell
dotnet test --filter FullyQualifiedName~PreviewHtmlTests
```
Expected: PASS (6 tests).

- [ ] **Step 9.5: Commit**

```powershell
git add src/Spectacle/Render/PreviewHtml.cs test/Spectacle.Tests/PreviewHtmlTests.cs
git commit -m "feat(render): compose preview HTML with embedded CSS/JS and base href"
```

---

## Task 10: LinkInterceptor

**Files:**
- Create: `src/Spectacle/Web/LinkInterceptor.cs`
- Create: `test/Spectacle.Tests/LinkInterceptorTests.cs`

- [ ] **Step 10.1: Write the failing tests**

`test/Spectacle.Tests/LinkInterceptorTests.cs`:
```csharp
using Spectacle.Web;

namespace Spectacle.Tests;

public class LinkInterceptorTests
{
    [Theory]
    [InlineData("https://spectacle.local/", "https://spectacle.local/#section", NavDecision.AllowInPage)]
    [InlineData("https://spectacle.local/", "https://spectacle.local/", NavDecision.AllowInPage)]
    [InlineData("https://spectacle.local/", "https://example.com/", NavDecision.OpenInBrowser)]
    [InlineData("https://spectacle.local/", "http://example.com/", NavDecision.OpenInBrowser)]
    [InlineData("https://spectacle.local/", "mailto:a@b.com", NavDecision.OpenInBrowser)]
    [InlineData("https://spectacle.local/", "file:///C:/x.md", NavDecision.OpenInBrowser)]
    [InlineData("https://spectacle.local/", "about:blank", NavDecision.AllowInPage)]
    public void Decides_navigation(string currentUrl, string targetUrl, NavDecision expected) =>
        LinkInterceptor.Decide(currentUrl, targetUrl).Should().Be(expected);
}
```

- [ ] **Step 10.2: Run tests to verify they fail**

Run:
```powershell
dotnet test --filter FullyQualifiedName~LinkInterceptorTests
```
Expected: FAIL.

- [ ] **Step 10.3: Write `src/Spectacle/Web/LinkInterceptor.cs`**

```csharp
namespace Spectacle.Web;

public enum NavDecision { AllowInPage, OpenInBrowser }

public static class LinkInterceptor
{
    public static NavDecision Decide(string currentUrl, string targetUrl)
    {
        if (string.IsNullOrEmpty(targetUrl) || targetUrl.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
            return NavDecision.AllowInPage;

        if (!Uri.TryCreate(currentUrl, UriKind.Absolute, out var current)
            || !Uri.TryCreate(targetUrl, UriKind.Absolute, out var target))
            return NavDecision.OpenInBrowser;

        var sameOrigin = string.Equals(current.Scheme, target.Scheme, StringComparison.OrdinalIgnoreCase)
                      && string.Equals(current.Host, target.Host, StringComparison.OrdinalIgnoreCase)
                      && current.Port == target.Port;

        return sameOrigin ? NavDecision.AllowInPage : NavDecision.OpenInBrowser;
    }
}
```

- [ ] **Step 10.4: Run tests to verify they pass**

Run:
```powershell
dotnet test --filter FullyQualifiedName~LinkInterceptorTests
```
Expected: PASS.

- [ ] **Step 10.5: Commit**

```powershell
git add src/Spectacle/Web/LinkInterceptor.cs test/Spectacle.Tests/LinkInterceptorTests.cs
git commit -m "feat(web): decide same-origin vs external for link navigation"
```

---

## Task 11: WebViewHost

**Files:**
- Create: `src/Spectacle/Web/WebViewHost.xaml`
- Create: `src/Spectacle/Web/WebViewHost.xaml.cs`

WPF UserControl wrapping `WebView2`. Smoke-tested in Task 16 (`MainWindow` composition); no unit tests because the control requires a UI thread and Win32 window handle.

- [ ] **Step 11.1: Write `src/Spectacle/Web/WebViewHost.xaml`**

```xml
<UserControl x:Class="Spectacle.Web.WebViewHost"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:wv2="clr-namespace:Microsoft.Web.WebView2.Wpf;assembly=Microsoft.Web.WebView2.Wpf"
             FocusVisualStyle="{x:Null}">
    <wv2:WebView2 x:Name="Web" />
</UserControl>
```

- [ ] **Step 11.2: Write `src/Spectacle/Web/WebViewHost.xaml.cs`**

```csharp
using System.Diagnostics;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;

namespace Spectacle.Web;

public partial class WebViewHost : UserControl
{
    public const string VirtualHost = "spectacle.local";
    private bool _ready;
    private string? _pendingHtml;
    private string? _virtualFolder;

    public WebViewHost()
    {
        InitializeComponent();
        Loaded += async (_, _) => await InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        await Web.EnsureCoreWebView2Async();
        Web.CoreWebView2.Settings.AreDevToolsEnabled = false;
        Web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        Web.CoreWebView2.NewWindowRequested += OnNewWindow;
        Web.CoreWebView2.NavigationStarting += OnNavStarting;
        _ready = true;
        if (_pendingHtml is not null) DoSetHtml(_pendingHtml);
    }

    public void SetVirtualFolder(string absolutePath)
    {
        _virtualFolder = absolutePath;
        if (_ready)
            Web.CoreWebView2.SetVirtualHostNameToFolderMapping(
                VirtualHost, absolutePath, CoreWebView2HostResourceAccessKind.Allow);
    }

    public void SetHtml(string html)
    {
        if (!_ready) { _pendingHtml = html; return; }
        DoSetHtml(html);
    }

    public void SetZoom(double factor) => Web.ZoomFactor = factor;

    public void Reload() => Web.Reload();

    private void DoSetHtml(string html)
    {
        if (_virtualFolder is not null)
            Web.CoreWebView2.SetVirtualHostNameToFolderMapping(
                VirtualHost, _virtualFolder, CoreWebView2HostResourceAccessKind.Allow);
        Web.NavigateToString(html);
    }

    private void OnNewWindow(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        OpenInBrowser(e.Uri);
    }

    private void OnNavStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        var current = Web.Source?.ToString() ?? $"https://{VirtualHost}/";
        var decision = LinkInterceptor.Decide(current, e.Uri);
        if (decision == NavDecision.OpenInBrowser)
        {
            e.Cancel = true;
            OpenInBrowser(e.Uri);
        }
    }

    private static void OpenInBrowser(string uri)
    {
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); }
        catch { /* user cancelled or no handler */ }
    }
}
```

- [ ] **Step 11.3: Build**

Run:
```powershell
dotnet build
```
Expected: `Build succeeded`. If you see `CoreWebView2HostResourceAccessKind` errors, confirm `Microsoft.Web.WebView2` is at version 1.0.2592.51 or newer in `Spectacle.csproj`.

- [ ] **Step 11.4: Commit**

```powershell
git add src/Spectacle/Web
git commit -m "feat(web): WebViewHost with virtual-host mapping and link interception"
```

---

## Task 12: PreviewPipeline

**Files:**
- Create: `src/Spectacle/Render/PreviewPipeline.cs`
- Create: `test/Spectacle.Tests/PreviewPipelineTests.cs`

- [ ] **Step 12.1: Write the failing tests**

`test/Spectacle.Tests/PreviewPipelineTests.cs`:
```csharp
using Spectacle.Documents;
using Spectacle.Render;

namespace Spectacle.Tests;

public class PreviewPipelineTests
{
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

    [Fact]
    public void Renders_initial_document_immediately()
    {
        var doc = new StubDocument();
        doc.Update("# hello");
        var sink = new StubSink();

        using var pipeline = new PreviewPipeline(doc, sink, PreviewTheme.Dark);
        pipeline.Start();

        sink.Pushed.Should().HaveCount(1);
        sink.Pushed[0].Should().Contain("<h1");
    }

    [Fact]
    public void Re_renders_on_Changed()
    {
        var doc = new StubDocument();
        doc.Update("# a");
        var sink = new StubSink();
        using var pipeline = new PreviewPipeline(doc, sink, PreviewTheme.Dark);
        pipeline.Start();

        doc.Update("# b");

        sink.Pushed.Should().HaveCount(2);
        sink.Pushed[1].Should().Contain("# b".Replace("# ", ""));
    }

    [Fact]
    public void SetTheme_re_renders()
    {
        var doc = new StubDocument();
        doc.Update("# a");
        var sink = new StubSink();
        using var pipeline = new PreviewPipeline(doc, sink, PreviewTheme.Dark);
        pipeline.Start();

        pipeline.SetTheme(PreviewTheme.HighContrast);

        sink.Pushed.Should().HaveCount(2);
        sink.Pushed[1].Should().Contain("#ffff00"); // hc.css link color
    }
}
```

- [ ] **Step 12.2: Run tests to verify they fail**

Run:
```powershell
dotnet test --filter FullyQualifiedName~PreviewPipelineTests
```
Expected: FAIL.

- [ ] **Step 12.3: Write `src/Spectacle/Render/PreviewPipeline.cs`**

```csharp
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
    private PreviewTheme _theme;
    private bool _started;

    public PreviewPipeline(Document document, IPreviewSink sink, PreviewTheme theme)
    {
        _document = document;
        _sink = sink;
        _theme = theme;
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

    private void OnDocumentChanged(object? sender, EventArgs e) => Render();

    private void Render()
    {
        var body = _renderer.ToHtml(_document.Text);
        var html = PreviewHtml.Build(body, $"https://{Web.WebViewHost.VirtualHost}/", _theme);
        _sink.Push(html);
    }

    public void Dispose() => _document.Changed -= OnDocumentChanged;
}
```

- [ ] **Step 12.4: Run tests to verify they pass**

Run:
```powershell
dotnet test --filter FullyQualifiedName~PreviewPipelineTests
```
Expected: PASS (3 tests).

- [ ] **Step 12.5: Commit**

```powershell
git add src/Spectacle/Render/PreviewPipeline.cs test/Spectacle.Tests/PreviewPipelineTests.cs
git commit -m "feat(render): PreviewPipeline ties Document changes to sink output"
```

---

## Task 13: HighContrastWatcher

**Files:**
- Create: `src/Spectacle/Theme/HighContrastWatcher.cs`

No unit test — wraps `SystemParameters.HighContrast` and `SystemEvents.UserPreferenceChanged`, both Win32-bound. Smoke-tested manually per spec §12.

- [ ] **Step 13.1: Write `src/Spectacle/Theme/HighContrastWatcher.cs`**

```csharp
using Microsoft.Win32;
using System.Windows;

namespace Spectacle.Theme;

public sealed class HighContrastWatcher : IDisposable
{
    public event EventHandler? Changed;

    public bool IsActive => SystemParameters.HighContrast;

    public HighContrastWatcher()
    {
        SystemEvents.UserPreferenceChanged += OnPrefChanged;
    }

    private void OnPrefChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.Accessibility ||
            e.Category == UserPreferenceCategory.General ||
            e.Category == UserPreferenceCategory.Color)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose() => SystemEvents.UserPreferenceChanged -= OnPrefChanged;
}
```

- [ ] **Step 13.2: Build**

Run:
```powershell
dotnet build
```
Expected: `Build succeeded`.

- [ ] **Step 13.3: Commit**

```powershell
git add src/Spectacle/Theme
git commit -m "feat(theme): watch Windows High Contrast preference changes"
```

---

## Task 14: EditorHost Placeholder

**Files:**
- Create: `src/Spectacle/Editor/EditorHost.xaml`
- Create: `src/Spectacle/Editor/EditorHost.xaml.cs`

Empty placeholder for the future editor. Lives in MainWindow's left grid column with `Width=0` in v1.

- [ ] **Step 14.1: Write `src/Spectacle/Editor/EditorHost.xaml`**

```xml
<UserControl x:Class="Spectacle.Editor.EditorHost"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" />
```

- [ ] **Step 14.2: Write `src/Spectacle/Editor/EditorHost.xaml.cs`**

```csharp
using System.Windows.Controls;

namespace Spectacle.Editor;

public partial class EditorHost : UserControl
{
    public EditorHost() => InitializeComponent();
}
```

- [ ] **Step 14.3: Build**

Run:
```powershell
dotnet build
```
Expected: `Build succeeded`.

- [ ] **Step 14.4: Commit**

```powershell
git add src/Spectacle/Editor
git commit -m "feat(editor): empty EditorHost placeholder for future editor"
```

---

## Task 15: FileAssocInstaller

**Files:**
- Create: `src/Spectacle/Install/FileAssocInstaller.cs`
- Create: `test/Spectacle.Tests/FileAssocInstallerTests.cs`

- [ ] **Step 15.1: Write the failing tests**

`test/Spectacle.Tests/FileAssocInstallerTests.cs`:
```csharp
using Microsoft.Win32;
using Spectacle.Install;

namespace Spectacle.Tests;

public class FileAssocInstallerTests : IDisposable
{
    private readonly string _rootKey;

    public FileAssocInstallerTests()
    {
        _rootKey = $"Software\\Classes\\Spectacle.Tests.{Guid.NewGuid():N}";
    }

    public void Dispose()
    {
        try { Registry.CurrentUser.DeleteSubKeyTree(_rootKey, throwOnMissingSubKey: false); }
        catch { /* test cleanup, ignore */ }
    }

    [Fact]
    public void Register_creates_progid_and_extensions()
    {
        var installer = new FileAssocInstaller(@"C:\Tools\Spectacle\Spectacle.exe", _rootKey);

        installer.Register();

        using var prog = Registry.CurrentUser.OpenSubKey($@"{_rootKey}\Spectacle.MarkdownFile\shell\open\command");
        prog.Should().NotBeNull();
        prog!.GetValue(null).Should().Be(@"""C:\Tools\Spectacle\Spectacle.exe"" ""%1""");

        using var md = Registry.CurrentUser.OpenSubKey($@"{_rootKey}\.md");
        md!.GetValue(null).Should().Be("Spectacle.MarkdownFile");

        using var markdown = Registry.CurrentUser.OpenSubKey($@"{_rootKey}\.markdown");
        markdown!.GetValue(null).Should().Be("Spectacle.MarkdownFile");
    }

    [Fact]
    public void Register_is_idempotent()
    {
        var installer = new FileAssocInstaller(@"C:\x\Spectacle.exe", _rootKey);
        installer.Register();
        installer.Register();

        using var md = Registry.CurrentUser.OpenSubKey($@"{_rootKey}\.md");
        md!.GetValue(null).Should().Be("Spectacle.MarkdownFile");
    }

    [Fact]
    public void Unregister_removes_keys()
    {
        var installer = new FileAssocInstaller(@"C:\x\Spectacle.exe", _rootKey);
        installer.Register();
        installer.Unregister();

        Registry.CurrentUser.OpenSubKey($@"{_rootKey}\.md").Should().BeNull();
        Registry.CurrentUser.OpenSubKey($@"{_rootKey}\Spectacle.MarkdownFile").Should().BeNull();
    }

    [Fact]
    public void Unregister_is_idempotent()
    {
        var installer = new FileAssocInstaller(@"C:\x\Spectacle.exe", _rootKey);
        installer.Unregister(); // never registered — should not throw
    }
}
```

- [ ] **Step 15.2: Run tests to verify they fail**

Run:
```powershell
dotnet test --filter FullyQualifiedName~FileAssocInstallerTests
```
Expected: FAIL.

- [ ] **Step 15.3: Write `src/Spectacle/Install/FileAssocInstaller.cs`**

```csharp
using Microsoft.Win32;

namespace Spectacle.Install;

public sealed class FileAssocInstaller
{
    private const string ProgId = "Spectacle.MarkdownFile";
    private readonly string _exePath;
    private readonly string _rootSubKey;

    public FileAssocInstaller(string exePath)
        : this(exePath, @"Software\Classes") { }

    public FileAssocInstaller(string exePath, string rootSubKey)
    {
        _exePath = exePath;
        _rootSubKey = rootSubKey;
    }

    public void Register()
    {
        using (var prog = Registry.CurrentUser.CreateSubKey($@"{_rootSubKey}\{ProgId}"))
            prog!.SetValue(null, "Markdown Document");

        using (var cmd = Registry.CurrentUser.CreateSubKey(
                   $@"{_rootSubKey}\{ProgId}\shell\open\command"))
            cmd!.SetValue(null, $"\"{_exePath}\" \"%1\"");

        foreach (var ext in new[] { ".md", ".markdown" })
        {
            using var key = Registry.CurrentUser.CreateSubKey($@"{_rootSubKey}\{ext}");
            key!.SetValue(null, ProgId);
        }
    }

    public void Unregister()
    {
        foreach (var ext in new[] { ".md", ".markdown" })
            Registry.CurrentUser.DeleteSubKeyTree($@"{_rootSubKey}\{ext}", throwOnMissingSubKey: false);

        Registry.CurrentUser.DeleteSubKeyTree($@"{_rootSubKey}\{ProgId}", throwOnMissingSubKey: false);
    }
}
```

- [ ] **Step 15.4: Run tests to verify they pass**

Run:
```powershell
dotnet test --filter FullyQualifiedName~FileAssocInstallerTests
```
Expected: PASS (4 tests).

- [ ] **Step 15.5: Commit**

```powershell
git add src/Spectacle/Install test/Spectacle.Tests/FileAssocInstallerTests.cs
git commit -m "feat(install): per-user HKCU file association registrar"
```

---

## Task 16: MainWindow, App, and Program Composition

**Files:**
- Create: `src/Spectacle/App.xaml`
- Create: `src/Spectacle/App.xaml.cs`
- Create: `src/Spectacle/MainWindow.xaml`
- Create: `src/Spectacle/MainWindow.xaml.cs`
- Modify: `src/Spectacle/Program.cs`
- Modify: `src/Spectacle/Spectacle.csproj` (set StartupObject)

- [ ] **Step 16.1: Write `src/Spectacle/App.xaml`**

```xml
<Application x:Class="Spectacle.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" />
```

- [ ] **Step 16.2: Write `src/Spectacle/App.xaml.cs`**

```csharp
using System.Windows;

namespace Spectacle;

public partial class App : Application
{
}
```

- [ ] **Step 16.3: Write `src/Spectacle/MainWindow.xaml`**

```xml
<Window x:Class="Spectacle.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:web="clr-namespace:Spectacle.Web"
        xmlns:editor="clr-namespace:Spectacle.Editor"
        Title="Spectacle"
        Width="1100" Height="800"
        Background="#1e1e1e"
        Foreground="#d4d4d4"
        UseLayoutRounding="True">
    <Window.InputBindings>
        <KeyBinding Key="F5" Command="{Binding ReloadCommand}" />
        <KeyBinding Key="R" Modifiers="Control" Command="{Binding ReloadCommand}" />
        <KeyBinding Key="OemPlus" Modifiers="Control" Command="{Binding ZoomInCommand}" />
        <KeyBinding Key="Add" Modifiers="Control" Command="{Binding ZoomInCommand}" />
        <KeyBinding Key="OemMinus" Modifiers="Control" Command="{Binding ZoomOutCommand}" />
        <KeyBinding Key="Subtract" Modifiers="Control" Command="{Binding ZoomOutCommand}" />
        <KeyBinding Key="D0" Modifiers="Control" Command="{Binding ZoomResetCommand}" />
        <KeyBinding Key="NumPad0" Modifiers="Control" Command="{Binding ZoomResetCommand}" />
        <KeyBinding Key="F11" Command="{Binding FullscreenCommand}" />
        <KeyBinding Key="Escape" Command="{Binding CloseCommand}" />
    </Window.InputBindings>
    <Grid>
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
</Window>
```

- [ ] **Step 16.4: Write `src/Spectacle/MainWindow.xaml.cs`**

```csharp
using System.Windows;
using System.Windows.Input;
using Spectacle.Documents;
using Spectacle.Render;
using Spectacle.Theme;

namespace Spectacle;

public partial class MainWindow : Window, IPreviewSink
{
    private readonly FileDocument _document;
    private readonly PreviewPipeline _pipeline;
    private readonly HighContrastWatcher _hcWatcher = new();
    private double _zoom = 1.0;
    private WindowState _preFullScreenState;
    private WindowStyle _preFullScreenStyle;

    public ICommand ReloadCommand { get; }
    public ICommand ZoomInCommand { get; }
    public ICommand ZoomOutCommand { get; }
    public ICommand ZoomResetCommand { get; }
    public ICommand FullscreenCommand { get; }
    public ICommand CloseCommand { get; }

    public MainWindow(string filePath)
    {
        InitializeComponent();

        _document = FileDocument.Open(filePath);
        Title = $"{System.IO.Path.GetFileName(filePath)} — Spectacle";
        Web.SetVirtualFolder(_document.BaseDirectory);

        var theme = _hcWatcher.IsActive ? PreviewTheme.HighContrast : PreviewTheme.Dark;
        _pipeline = new PreviewPipeline(_document, this, theme);
        _hcWatcher.Changed += (_, _) => Dispatcher.Invoke(() =>
            _pipeline.SetTheme(_hcWatcher.IsActive ? PreviewTheme.HighContrast : PreviewTheme.Dark));

        ReloadCommand = new RelayCommand(_ => Web.Reload());
        ZoomInCommand = new RelayCommand(_ => SetZoom(_zoom + 0.1));
        ZoomOutCommand = new RelayCommand(_ => SetZoom(_zoom - 0.1));
        ZoomResetCommand = new RelayCommand(_ => SetZoom(1.0));
        FullscreenCommand = new RelayCommand(_ => ToggleFullscreen());
        CloseCommand = new RelayCommand(_ => Close());

        DataContext = this;
        Loaded += (_, _) => _pipeline.Start();
        Closed += (_, _) =>
        {
            _pipeline.Dispose();
            _document.Dispose();
            _hcWatcher.Dispose();
        };
    }

    public void Push(string html) => Dispatcher.Invoke(() => Web.SetHtml(html));

    private void SetZoom(double factor)
    {
        _zoom = Math.Clamp(factor, 0.5, 3.0);
        Web.SetZoom(_zoom);
    }

    private void ToggleFullscreen()
    {
        if (WindowStyle == WindowStyle.None)
        {
            WindowStyle = _preFullScreenStyle;
            WindowState = _preFullScreenState;
        }
        else
        {
            _preFullScreenStyle = WindowStyle;
            _preFullScreenState = WindowState;
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;
        }
    }
}

internal sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _exec;
    public RelayCommand(Action<object?> exec) => _exec = exec;
    public bool CanExecute(object? p) => true;
    public void Execute(object? p) => _exec(p);
    public event EventHandler? CanExecuteChanged;
}
```

- [ ] **Step 16.5: Rewrite `src/Spectacle/Program.cs`**

```csharp
using System.Windows;
using Spectacle.Cli;
using Spectacle.Files;
using Spectacle.Install;

namespace Spectacle;

public static class Program
{
    private const string UsageText = """
        Spectacle — Markdown viewer

        Usage:
          Spectacle.exe <file.md|file.markdown>   Open and render a Markdown file
          Spectacle.exe --register                Register as default handler for .md/.markdown (per-user)
          Spectacle.exe --unregister              Remove the file association
          Spectacle.exe --help, -h                Show this help
          Spectacle.exe --version                 Show version
        """;

    [STAThread]
    public static int Main(string[] args)
    {
        var command = CliArgs.Parse(args);
        return command switch
        {
            CliCommand.Help => Print(UsageText, 0),
            CliCommand.Version => Print(GetVersion(), 0),
            CliCommand.Register => DoRegister(),
            CliCommand.Unregister => DoUnregister(),
            CliCommand.Open open => DoOpen(open.Path),
            _ => Print(UsageText, 0),
        };
    }

    private static int DoOpen(string path)
    {
        if (!FileGuard.IsAllowed(path))
        {
            Console.Error.WriteLine($"Spectacle only opens .md and .markdown files. Refusing: {path}");
            return 2;
        }
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"File not found: {path}");
            return 2;
        }

        var app = new App();
        var window = new MainWindow(path);
        return app.Run(window);
    }

    private static int DoRegister()
    {
        var exe = Environment.ProcessPath
            ?? throw new InvalidOperationException("Could not resolve own executable path.");
        new FileAssocInstaller(exe).Register();
        Console.WriteLine("Registered .md and .markdown to Spectacle for the current user.");
        return 0;
    }

    private static int DoUnregister()
    {
        var exe = Environment.ProcessPath ?? "";
        new FileAssocInstaller(exe).Unregister();
        Console.WriteLine("Removed Spectacle file associations for the current user.");
        return 0;
    }

    private static int Print(string text, int code) { Console.WriteLine(text); return code; }

    private static string GetVersion() =>
        typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0";
}
```

- [ ] **Step 16.6: Modify `src/Spectacle/Spectacle.csproj` to set StartupObject and remove default Main generation**

Add inside the existing `<PropertyGroup>`:
```xml
<StartupObject>Spectacle.Program</StartupObject>
```

WPF normally auto-generates an entry point from `App.xaml`. Override that with the custom `Program.Main` above. If `dotnet build` complains about multiple entry points, add to the csproj:
```xml
<PropertyGroup>
  <DefineConstants>$(DefineConstants);DISABLE_XAML_GENERATED_MAIN</DefineConstants>
</PropertyGroup>
```
and set `App.xaml`'s build action — open `App.xaml` properties: ensure `Build Action = Page` (not `ApplicationDefinition`). In project file terms, replace any `<ApplicationDefinition Include="App.xaml" />` with `<Page Include="App.xaml" />` (the SDK glob may already include it; if `dotnet build` errors with `CS0017`, this is the fix).

- [ ] **Step 16.7: Build**

Run:
```powershell
dotnet build
```
Expected: `Build succeeded`. Resolve any XAML/entry-point conflicts using the note in Step 16.6.

- [ ] **Step 16.8: Smoke test**

Create a test markdown file `C:\Temp\spectacle-smoke.md`:
```markdown
# Spectacle smoke test

A paragraph with **bold**, *italic*, and `code`.

| h1 | h2 |
|----|----|
| a  | b  |

- [x] done
- [ ] todo

```cs
var x = 1;
```

A [link to example](https://example.com) and a [section link](#a-paragraph-with-bold-italic-and-code).
```

Run:
```powershell
dotnet run --project src/Spectacle -- C:\Temp\spectacle-smoke.md
```

Verify:
1. Window opens, dark background, body text light grey.
2. Heading rendered as `<h1>` style.
3. Table is bordered and styled.
4. Task list shows checked/unchecked boxes.
5. Code block has syntax-highlighted C#.
6. Clicking the external link opens the default browser (not in-app).
7. Pressing `Ctrl+R` reloads. Edit the file in another editor, save, observe auto-reload within ~1s.
8. Pressing `Ctrl+=` zooms in, `Ctrl+0` resets.
9. Pressing `F11` toggles fullscreen.
10. Pressing `Esc` closes the window.

(Per `CLAUDE.md`: `dotnet run` requires no service stack for Spectacle since it's a standalone WPF app — this differs from the user's microservice projects.)

- [ ] **Step 16.9: Negative smoke test**

Run:
```powershell
dotnet run --project src/Spectacle -- C:\Windows\System32\drivers\etc\hosts
```
Expected: exit code 2, error printed to stderr, no window.

- [ ] **Step 16.10: Commit**

```powershell
git add src/Spectacle/App.xaml* src/Spectacle/MainWindow.xaml* src/Spectacle/Program.cs src/Spectacle/Spectacle.csproj
git commit -m "feat: MainWindow composition + Program entry dispatching CLI commands"
```

---

## Task 17: Publish + README

**Files:**
- Create: `README.md`
- Create: `src/Spectacle/Properties/PublishProfiles/win-x64.pubxml`

- [ ] **Step 17.1: Write `src/Spectacle/Properties/PublishProfiles/win-x64.pubxml`**

```xml
<Project>
  <PropertyGroup>
    <Configuration>Release</Configuration>
    <Platform>Any CPU</Platform>
    <PublishDir>..\..\publish\win-x64\</PublishDir>
    <PublishProtocol>FileSystem</PublishProtocol>
    <TargetFramework>net8.0-windows</TargetFramework>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <SelfContained>false</SelfContained>
    <PublishSingleFile>true</PublishSingleFile>
    <PublishReadyToRun>false</PublishReadyToRun>
    <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
  </PropertyGroup>
</Project>
```

- [ ] **Step 17.2: Write `README.md`**

```markdown
# Spectacle

A Windows-only Markdown viewer. Renders `.md` / `.markdown` files with VS Code-preview fidelity.
Dark theme. WCAG-accessible. No editing.

## Install

1. `dotnet publish src/Spectacle -p:PublishProfile=win-x64`
2. Copy `publish/win-x64/Spectacle.exe` to `C:\Tools\Spectacle\`.
3. Run `C:\Tools\Spectacle\Spectacle.exe --register` to set as the default handler for `.md` / `.markdown` (per-user, no admin).
4. Optional PowerShell helper, in `$PROFILE`:
   ```powershell
   function spectacle { param([string]$Path) & 'C:\Tools\Spectacle\Spectacle.exe' $Path }
   ```

## Usage

```text
Spectacle.exe <file.md|file.markdown>   Open and render
Spectacle.exe --register                Register file association
Spectacle.exe --unregister              Remove file association
Spectacle.exe --help                    Show help
Spectacle.exe --version                 Show version
```

## Keyboard

| Keys | Action |
|---|---|
| Ctrl+R / F5 | Reload from disk |
| Ctrl+= / Ctrl+- / Ctrl+0 | Zoom in / out / reset |
| F11 | Fullscreen |
| Esc | Close window |

## Limits (v1)

- Read-only. No editing.
- Markdown only. Will refuse other extensions with exit code 2.
- No math, no Mermaid diagrams.
- Windows 11 only. Requires the WebView2 Evergreen Runtime (preinstalled on Win11).
```

- [ ] **Step 17.3: Publish**

Run:
```powershell
dotnet publish src/Spectacle -p:PublishProfile=win-x64
```
Expected: `Spectacle.exe` produced under `publish/win-x64/`.

- [ ] **Step 17.4: Run full test suite**

Run:
```powershell
dotnet test
```
Expected: all tests pass.

- [ ] **Step 17.5: Run end-to-end smoke**

1. `publish/win-x64/Spectacle.exe C:\Temp\spectacle-smoke.md` — window opens, renders.
2. `publish/win-x64/Spectacle.exe --register` — registers HKCU keys.
3. Double-click `C:\Temp\spectacle-smoke.md` in Explorer — Spectacle launches.
4. Enable Windows High Contrast (Settings → Accessibility → Contrast themes → pick any). Reload — confirm `hc.css` palette engages (yellow links on black).
5. Disable High Contrast. Confirm app stays dark (does **not** flip to light).
6. Run Narrator (`Ctrl+Win+Enter`); confirm headings, list items, table cells are announced.
7. `publish/win-x64/Spectacle.exe --unregister` — keys removed.

- [ ] **Step 17.6: Commit**

```powershell
git add README.md src/Spectacle/Properties/PublishProfiles
git commit -m "chore: publish profile, README, end-to-end smoke checklist"
```

---

## Self-Review Notes

Re-read the spec § by § and confirm coverage:

- **§1 Problem/Goal** — Tasks 16–17 deliver the full viewer + file-association install.
- **§3 Extensible-for-editing seams** — `Document` abstraction (Task 4), 3-column grid + EditorHost (Tasks 14, 16), reserved shortcuts (Task 16's `InputBindings` claim only the documented set).
- **§5.1 Units** — every unit has a task: `CliArgs` (Task 2), `FileGuard` (Task 3), `Document`/`FileDocument` (Task 4), `MdRenderer` (Task 5), `PreviewHtml` (Task 9), `PreviewPipeline` (Task 12), `WebViewHost` (Task 11), `EditorHost` (Task 14), `HighContrastWatcher` (Task 13), `FileAssocInstaller` (Task 15).
- **§6 Rendering parity** — Markdig pipeline matches spec; CSS variants in Task 7; Prism vendoring in Task 7.4; virtual host mapping in Task 11 implements local-image resolution.
- **§6.1 Accessibility** — `:focus-visible` outlines in `preview.css` (Task 7.1); `forced-colors` block in `preview.css`; `WcagContrast` + `PaletteContrastTests` (Tasks 6, 8); `<main role="main">` in `PreviewHtml` (Task 9); zoom in `MainWindow` (Task 16); no animations; semantic HTML preserved by Markdig.
- **§7 Keyboard shortcuts** — all bound in `MainWindow.xaml` (Task 16); reserved chords explicitly absent.
- **§9 CLI surface** — `Program.cs` dispatches all five modes with documented exit codes (Task 16).
- **§12 Testing strategy** — every unit test class listed in §12 exists in a task.
- **§14 Risks** — WebView2 missing: caught by `EnsureCoreWebView2Async` throwing; user sees a stack trace today (could be wrapped in a friendlier dialog in a follow-up — not in scope for v1). FileSystemWatcher debounce: 150ms + read-with-shared-access in Task 4.5. Prism size: curated language list in Task 7.4. File-assoc override: `--register` idempotency tested in Task 15.

No gaps. No placeholders. Type names are consistent across tasks (`Document`, `FileDocument`, `MdRenderer`, `PreviewHtml.Build`, `PreviewTheme`, `IPreviewSink`, `PreviewPipeline`, `WebViewHost`, `LinkInterceptor`, `NavDecision`, `HighContrastWatcher`, `FileAssocInstaller`).
