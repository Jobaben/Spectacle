# Portrait Startup Window Size Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace Spectacle's default landscape window size (1100 × 800) with a portrait shape — 900 DIP wide, work-area-tall, horizontally centered on the primary monitor's work area, top edge flush with the work area top.

**Architecture:** Drop the hardcoded `Width`/`Height` attributes from `MainWindow.xaml`. Subscribe to `MainWindow.SourceInitialized` in the constructor; the handler reads `SystemParameters.WorkArea` and applies `Width`, `Height`, `Left`, `Top` exactly once before the first paint. No new files, no new public API, no behavior change after launch — the user keeps full freedom to resize/maximize/F11.

**Tech Stack:** WPF on .NET 8 (Windows). `System.Windows.SystemParameters` for the work-area rectangle; values are in device-independent pixels so no DPI conversion is needed.

**Spec:** [`docs/superpowers/specs/2026-05-27-portrait-startup-size-design.md`](../specs/2026-05-27-portrait-startup-size-design.md)

---

## File Map

| Path | Status | Responsibility |
|---|---|---|
| `src/Spectacle/MainWindow.xaml` | modify | Strip the `Width="1100" Height="800"` attributes so the runtime no longer applies a hardcoded landscape default. |
| `src/Spectacle/MainWindow.xaml.cs` | modify | Subscribe `SourceInitialized` in the constructor and add a private `ApplyStartupGeometry` handler that sets `Width`/`Height`/`Left`/`Top` once. |

No new files. No new dependencies. No new public API. No new tests (WPF window sizing has no headless surface; the spec defines verification as build + regression + IDE launch).

---

## Task 1: Apply portrait startup geometry

**Files:**
- Modify: `src/Spectacle/MainWindow.xaml:7` (remove `Width="1100" Height="800"`)
- Modify: `src/Spectacle/MainWindow.xaml.cs:34-75` (constructor + new private method)

### - [ ] Step 1: Remove hardcoded size from MainWindow.xaml

Open `src/Spectacle/MainWindow.xaml`. The current `<Window>` opening tag (lines 1–10) reads:

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
```

Delete the entire `Width="1100" Height="800"` line. The opening tag should become:

```xml
<Window x:Class="Spectacle.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:web="clr-namespace:Spectacle.Web"
        xmlns:editor="clr-namespace:Spectacle.Editor"
        Title="Spectacle"
        Background="#1e1e1e"
        Foreground="#d4d4d4"
        UseLayoutRounding="True">
```

Leave every other line of the file (input bindings, grid layout, columns, content) untouched.

### - [ ] Step 2: Subscribe `SourceInitialized` in the constructor

Open `src/Spectacle/MainWindow.xaml.cs`. The constructor currently begins (lines 34–42):

```csharp
public MainWindow(string filePath)
{
    InitializeComponent();

    _sourcePath = Path.GetFullPath(filePath);
    _document = FileDocument.Open(filePath);
    _store = new AnnotationStore(filePath);
    Title = $"{System.IO.Path.GetFileName(filePath)} — Spectacle";
    Web.SetVirtualFolder(_document.BaseDirectory);
```

Insert one line directly after `InitializeComponent();` so it becomes:

```csharp
public MainWindow(string filePath)
{
    InitializeComponent();
    SourceInitialized += ApplyStartupGeometry;

    _sourcePath = Path.GetFullPath(filePath);
    _document = FileDocument.Open(filePath);
    _store = new AnnotationStore(filePath);
    Title = $"{System.IO.Path.GetFileName(filePath)} — Spectacle";
    Web.SetVirtualFolder(_document.BaseDirectory);
```

Do not touch any other line in the constructor. Do not add any `using` directive — `System.Windows` (which contains `SystemParameters` and `WindowStartupLocation`) is already imported at line 4.

### - [ ] Step 3: Add the `ApplyStartupGeometry` private method

In the same file, the `Push` method appears at lines 77–78:

```csharp
public void Push(string html) => Dispatcher.Invoke(() => Web.SetHtml(html));

private void SetZoom(double factor)
```

Insert the new private method directly between `Push` and `SetZoom` so it reads:

```csharp
public void Push(string html) => Dispatcher.Invoke(() => Web.SetHtml(html));

private void ApplyStartupGeometry(object? sender, EventArgs e)
{
    SourceInitialized -= ApplyStartupGeometry;

    const double startupWidth = 900;
    var workArea = SystemParameters.WorkArea;

    WindowStartupLocation = WindowStartupLocation.Manual;
    Width  = startupWidth;
    Height = workArea.Height;
    Left   = workArea.X + (workArea.Width - startupWidth) / 2;
    Top    = workArea.Y;
}

private void SetZoom(double factor)
```

Notes:
- The handler unsubscribes itself on the first invocation so subsequent `SourceInitialized` activity (none expected for a singleton `MainWindow`, but cheap insurance) is a no-op.
- `WindowStartupLocation.Manual` is already WPF's default; setting it explicitly is documentation and a guard against future XAML changes that might add `WindowStartupLocation="CenterScreen"`.
- All four values are in DIPs; `SystemParameters.WorkArea` returns DIPs. No DPI math.

### - [ ] Step 4: Build to confirm compilation

Run from the repo root:

```powershell
dotnet build C:\GIT\Spectacle\Spectacle.sln
```

Expected: `Build succeeded` with `0 Error(s)`. Warnings count must not increase versus pre-change baseline (a quick mental check, not a grep).

If the build fails, common causes:
- The XAML edit accidentally removed a quote or attribute — re-check the closing `>` of the `<Window>` tag is still present and there is no orphan whitespace where `Width="1100" Height="800"` used to live.
- The constructor edit broke a brace — verify the inserted line ends with `;` and that the existing `_sourcePath = …` line is still intact directly below.

### - [ ] Step 5: Run the existing test suite to confirm no regressions

```powershell
dotnet test C:\GIT\Spectacle\Spectacle.sln
```

Expected: All existing tests pass (the suite covers `CliArgs`, `FileAssocInstaller`, `FileDocument`, `FileGuard`, `PaletteContrast`, `Smoke`, `WcagContrast`, `LinkInterceptor`, `AnnotationMatcher`, `AnnotationStore`, `BlockAnchor`, `BlockTagger`, `MdRenderer`, `PreviewPipeline`, `RevisionPlanExporter`, `PreviewHtml`). None of them touch `MainWindow`, so a failure here is unrelated to this change and must be investigated before continuing.

### - [ ] Step 6: Manual IDE verification (per CLAUDE.md "ready for IDE verification")

This step cannot be automated — Spectacle requires WebView2 + a real `.md` to open. Launch from the IDE (not `dotnet run`, which is broken for this project's service stack).

Pick or create any small `.md` file (e.g. the spec itself, `docs/superpowers/specs/2026-05-27-portrait-startup-size-design.md`) and open Spectacle on it. Verify:

1. **Shape.** The window is roughly 900 DIP wide. It is clearly taller than wide (portrait), not the prior landscape default.
2. **Vertical extent.** The top of the window sits flush against the top of the work area; the bottom sits at the top edge of the Windows taskbar. No vertical gap above or below.
3. **Horizontal centering.** Equal whitespace to the left and right of the window on the primary monitor.
4. **Resize.** Drag a corner — the window resizes freely (the startup handler only runs once).
5. **Maximize / restore.** Double-click the title bar or use the maximize button — window goes full work-area; restore returns to the 900 × work-area-height portrait shape.
6. **F11 fullscreen.** Press `F11` — chrome disappears, window covers the whole monitor. Press `F11` again — chrome returns, window restores to the portrait shape.
7. **Esc.** Pressing `Escape` still closes the window (no regression in the existing key bindings).
8. **DPI sanity (optional).** If a non-100% display is available, repeat on it. The portrait shape and centering must still hold; only the pixel measurements scale.

If any of 1–7 fails, stop and re-diagnose before committing. Do not "fix forward" by adding more code — re-read the steps above and confirm the edits exactly match.

### - [ ] Step 7: Commit

```powershell
git -C C:\GIT\Spectacle add src\Spectacle\MainWindow.xaml src\Spectacle\MainWindow.xaml.cs
git -C C:\GIT\Spectacle commit -m "feat(shell): portrait startup geometry, 900 x work-area"
```

Commit message body (multi-line, optional but encouraged):

```
Window now opens 900 DIP wide x the monitor's work-area height,
horizontally centered on the primary monitor's work area.  Sizing
is applied in SourceInitialized, before the first paint, so the
prior hardcoded 1100x800 landscape default is gone.

Spec: docs/superpowers/specs/2026-05-27-portrait-startup-size-design.md
```

---

## Self-Review Notes

**Spec coverage.** §1 Goal → Step 1 removes the old landscape default and Steps 2–3 install the portrait geometry. §4.1 XAML change → Step 1. §4.2 Code-behind → Steps 2–3 (full code shown, not paraphrased). §4.3 `SourceInitialized` rationale → encoded in Step 3 ("before the first paint"). §4.4 DIP units → Step 3 notes. §4.5 user actions after launch → Step 6 verification points 4–7. §6 Testing path → Steps 4–6. §7 Affected files → File Map matches exactly.

**Placeholder scan.** No `TBD`/`TODO`. All code is concrete and complete. Commands are exact. No "implement appropriate X" hand-waves.

**Type / name consistency.** `ApplyStartupGeometry` is the only new identifier and is referenced identically in Step 2 (`SourceInitialized += ApplyStartupGeometry`), Step 3 (method declaration and self-unsubscribe), and nowhere else. `startupWidth` is a local `const` used twice in the same method.

**Known limits acknowledged.** Per spec §5: primary monitor only; no persistence; fixed 900 width may clip on narrow displays. These are documented in the spec, not in this plan — the plan implements the spec as written.
