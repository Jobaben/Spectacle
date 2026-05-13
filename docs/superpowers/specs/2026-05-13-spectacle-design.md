# Spectacle — Markdown Viewer (Design Spec)

**Status:** Approved 2026-05-13 (revised 2026-05-13: dark-only theme with explicit accessibility commitments)
**Scope:** v1 — read-only Markdown viewer for Windows, architected so an editor can be added later without restructuring the shell.

## 1. Problem and Goal

PowerShell users on Windows have no native way to render `.md` files with the fidelity of VS Code's Markdown preview. Opening `README.md` from the terminal or Explorer falls back to a plain-text editor (Notepad, VS Code itself) and loses the rendered view.

**Goal:** Provide a single-purpose Windows app, `Spectacle.exe`, that:

1. Opens a `.md` / `.markdown` file passed as its argument.
2. Renders it with visual parity to VS Code's Markdown preview (GFM, code highlighting, tables, task lists, images).
3. Auto-reloads when the file changes on disk.
4. Can register itself as the per-user file handler for `.md` / `.markdown`.
5. Refuses non-Markdown files outright — no other format is supported, ever.

**Non-goal for v1:** Editing. The v1 product is view-only. The architecture leaves a clean seam for an editor to be added later without rewriting the shell.

## 2. Interpretation of the Original Request

"Default PowerShell editor" + "renders like VS Code preview" was interpreted as: a **read-only viewer** registered as the Windows file-handler for `.md`/`.markdown`, invokable from PowerShell. "Editor" here is the file-association sense — Spectacle is what launches when a `.md` file is opened. VS Code's preview is itself read-only.

The user later confirmed view-only, with extensibility for an editor later.

## 3. What "Extensible for Editing Later" Means Concretely

Extensibility is paid for in *structure* (where seams are drawn), not in speculative code. The constraints v1 must satisfy:

1. **Content source abstraction.** The renderer consumes `string markdown` + `baseDirectory`, not a file path. The file path is one possible source of that string.
2. **Two-column layout shell.** `MainWindow` uses a `Grid` with `[editor | splitter | preview]`. In v1 the editor column has `Width="0"` and the splitter is collapsed — the same XAML survives when the editor is added.
3. **Reserved shortcuts.** v1 does not claim `Ctrl+S`, `Ctrl+Z`, `Ctrl+Y`, `Ctrl+F`, `Ctrl+H`, `Ctrl+W`, `Ctrl+N`, `Ctrl+O`, `Tab`, or arrow-key combos. They belong to a future editor.
4. **Document lifecycle.** A `Document` object owns "current markdown text + base directory + change notifications". v1 ships one implementation backed by a file + watcher. A future `BufferDocument` will replace the watcher-driven source with editor-buffer changes; the preview pipeline downstream doesn't care which.
5. **No "viewer-only" assumptions in the shell.** Window chrome and command surface are designed for two modes; v1 hides the editor surface, doesn't omit it.

Explicitly **not** doing in v1: building unused code paths, declaring an `IEditor` interface with no implementation, or feature flags.

## 4. Approach

**WPF (.NET 8) + WebView2 + Markdig**, single-file framework-dependent executable.

| Option | Decision |
|---|---|
| **A. WPF + WebView2 + Markdig** | **Chosen.** Same Chromium engine VS Code uses → highest visual fidelity. Markdig is the de-facto .NET Markdown library with full GFM. Matches the user's daily stack (.NET). |
| B. Pure WPF + Markdig.Wpf | Rejected. XAML rendering cannot match Chromium typography, code highlighting, or table layout — will not look like VS Code. |
| C. Tauri / Electron | Rejected. Adds Rust/Node toolchains for a Windows-only utility — overkill. |

## 5. Architecture

```
┌──────────────────────────────────────────────────────────────┐
│ Spectacle.exe <file.md>                                      │
│                                                               │
│  ┌──────────┐    ┌────────────┐                              │
│  │ CliArgs  │───▶│ FileGuard  │                              │
│  └──────────┘    └─────┬──────┘                              │
│                        ▼                                      │
│                  ┌─────────────────────┐                      │
│                  │ Document (abstract) │                      │
│                  │  • Text             │                      │
│                  │  • BaseDirectory    │                      │
│                  │  • Changed event    │                      │
│                  └─────────┬───────────┘                      │
│                            │                                  │
│         ┌──────────────────┴──────────────────┐               │
│         │                                     │               │
│  ┌──────▼──────────┐                  ┌──────▼─────────┐     │
│  │ FileDocument    │  (v1)            │ BufferDocument │ (future)
│  │  + FileWatcher  │                  │  + Editor view │     │
│  └──────┬──────────┘                  └────────────────┘     │
│         │                                                     │
│         ▼                                                     │
│  ┌─────────────────────────────────────────────────┐         │
│  │ MainWindow                                       │         │
│  │  Grid: [EditorHost | Splitter | PreviewHost]    │         │
│  │   v1:    width 0       hidden      *             │         │
│  └────────────────────────┬─────────────────────────┘         │
│                           ▼                                   │
│                  ┌────────────────┐                           │
│                  │ PreviewPipeline│                           │
│                  │  MdRenderer ──▶│                           │
│                  │  PreviewHtml ─▶│                           │
│                  │  WebViewHost   │                           │
│                  └────────────────┘                           │
└──────────────────────────────────────────────────────────────┘
```

### 5.1 Units

| Unit | Responsibility |
|---|---|
| `CliArgs` | Parses argv. Modes: `<path>`, `--register`, `--unregister`, `--help`, `--version`. |
| `FileGuard` | Accepts only `*.md` / `*.markdown` (case-insensitive). Hard allowlist. Non-zero exit + clear message on rejection. |
| `Document` (abstract) | Exposes `Text`, `BaseDirectory`, `Changed` event. The single seam that lets editing slot in later. |
| `FileDocument : Document` | v1 implementation. Reads file on construct, owns a `FileSystemWatcher` (debounced 150 ms), re-reads + fires `Changed` on save. |
| `MdRenderer` | Markdig with `UseAdvancedExtensions()` + `UseEmojiAndSmiley()` + `UseAutoIdentifiers()`. Pure function: `string → string`. |
| `PreviewHtml` | Composes rendered body with embedded HTML shell: CSS, Prism.js, `<base href>` from `Document.BaseDirectory`. |
| `PreviewPipeline` | Subscribes to `Document.Changed`, runs render, pushes HTML into `WebViewHost`. Decoupled from file vs. buffer source. |
| `WebViewHost` | WPF `UserControl` wrapping `WebView2`. Intercepts navigation: in-page anchors stay; everything else opens in default browser via `ShellExecute`. |
| `EditorHost` | Empty `UserControl` placeholder in v1. Lives in the grid, width 0. Exists so adding an editor is a "fill this control" task, not a "rewrite the shell" task. |
| `HighContrastWatcher` | Detects Windows High Contrast / Contrast Themes (`SystemParameters.HighContrast`) on startup and on `WM_SETTINGCHANGE`. When active, swaps in the high-contrast CSS variant. Does **not** switch to light — Spectacle is dark-only. |
| `FileAssocInstaller` | `--register` / `--unregister` writes/removes per-user `HKCU\Software\Classes` entries for `.md` / `.markdown` → `Spectacle.MarkdownFile` ProgID → `Spectacle.exe "%1"`. No admin needed. |

## 6. Rendering Parity with VS Code Preview

- **Markdig pipeline:** `UseAdvancedExtensions()` (tables, task lists, autolinks, footnotes, pipe tables, emphasis extras) + `UseEmojiAndSmiley()` + `UseAutoIdentifiers()`. Soft-line-break-as-hard-line-break stays **off** to match VS Code's default.
- **CSS:** stripped-down port of VS Code's `markdown.css` using the **Dark+** palette only. No light theme exists. Two embedded variants:
  - `dark.css` (default) — VS Code Dark+ background `#1e1e1e` on foreground `#d4d4d4` (contrast ratio ≈ 11.6:1, exceeds WCAG AAA 7:1).
  - `hc.css` (high contrast) — pure black `#000000` on white `#ffffff`, used when Windows High Contrast / Contrast Themes is active.
- **Code highlighting:** Prism.js embedded (no CDN) with VS Code Dark+ token colors. Bundled languages: `cs`, `ps`, `js`, `ts`, `json`, `yaml`, `sql`, `bash`, `html`, `css`, `xml`, `md`. Covers ~95% of common cases without bloating the binary.
- **Local images:** resolved relative to the Markdown file via `<base href="file:///{BaseDirectory}/">`.
- **Math, Mermaid:** out of scope for v1.

## 6.1 Accessibility Commitments

Dark theme is the only theme, but accessibility is non-negotiable. v1 must satisfy:

| Requirement | How it's met |
|---|---|
| **WCAG 2.1 AA contrast** (4.5:1 body, 3:1 large/UI) | Dark palette computed contrast ≈ 11.6:1 for body, validated for every token color in `dark.css`. CI test (or commented manual check in `MdRendererTests`) asserts contrast pairs for the palette constants. |
| **WCAG AAA where feasible** (7:1) | Body text and code blocks meet 7:1 in the default palette. Inline links use `#4ea1ff` on `#1e1e1e` (≈ 7.0:1). |
| **Windows High Contrast / Contrast Themes** | `HighContrastWatcher` detects active state and swaps to `hc.css`. CSS also includes `@media (forced-colors: active)` rules as a defense-in-depth fallback inside WebView2. |
| **Visible focus** | `:focus-visible` rules apply a 2 px outline at `#7cb7ff` on every interactive element (links, the WebView itself). Native WPF focus visuals remain on app chrome. |
| **Scalable text** | Body font size `16px` baseline. Zoom keys (Ctrl+= / Ctrl+- / Ctrl+0) scale the whole document via WebView2's `ZoomFactor`. Last zoom level is persisted per session in window state. |
| **No motion sensitivity hazards** | No animations or transitions in CSS. If any are added later, gate them on `@media (prefers-reduced-motion: no-preference)`. |
| **Keyboard accessible** | Every command in §7 has a key binding. No mouse-only paths. Tab order in the WebView is the document's natural order. |
| **Screen reader compatible** | WebView2 exposes the rendered DOM to UIA / Narrator by default. Semantic HTML is preserved by Markdig (`<h1>`–`<h6>`, `<table>`, `<ul>`, `<code>`). Images keep their `alt` text. The main `<body>` is wrapped in `<main role="main">` for landmark navigation. |
| **No color-only meaning** | Syntax highlighting and task-list ticks use weight/shape in addition to color. Links are underlined, not color-only. |

## 7. Keyboard Shortcuts (v1)

| Keys | Action |
|---|---|
| `Ctrl+R` / `F5` | Reload from disk |
| `Ctrl+=` / `Ctrl+-` / `Ctrl+0` | Zoom in / out / reset |
| `F11` | Fullscreen toggle |
| `Esc` | Close window |
| `Ctrl+C` (on selection) | Copy as plain text (handled natively by WebView2) |

Reserved for the future editor — **not** claimed in v1: `Ctrl+S`, `Ctrl+Z`, `Ctrl+Y`, `Ctrl+F`, `Ctrl+H`, `Ctrl+W`, `Ctrl+N`, `Ctrl+O`, `Tab`, arrow-key chords.

## 8. PowerShell Integration

- **Primary:** file association. After `Spectacle.exe --register`, double-clicking a `.md` file or running `Start-Process .\README.md` (or simply `.\README.md`) launches Spectacle.
- **Convenience profile function** offered (not forced) on `--register`:
  ```powershell
  function spectacle { param([string]$Path) & 'C:\Tools\Spectacle\Spectacle.exe' $Path }
  ```
- **Not** wired to `$env:EDITOR` — that variable is for tools that need an interactive editor (e.g., `git commit`). A read-only viewer there would be wrong. Will be reconsidered when editing lands.

## 9. CLI Surface

| Invocation | Behavior |
|---|---|
| `Spectacle.exe <path>` | Open viewer on `<path>`. Exit 0 on clean close. Exit 2 if file rejected by `FileGuard`. Exit 1 on unexpected error. |
| `Spectacle.exe --register` | Register `.md` / `.markdown` association under `HKCU`. Idempotent. |
| `Spectacle.exe --unregister` | Remove association. Idempotent. |
| `Spectacle.exe --help` / `-h` | Print usage to stdout, exit 0. |
| `Spectacle.exe --version` | Print version, exit 0. |

## 10. Project Layout

```
Spectacle/
  src/
    Spectacle/                         # WPF app
      App.xaml(.cs)
      MainWindow.xaml(.cs)
      Cli/CliArgs.cs
      Files/FileGuard.cs
      Documents/Document.cs            # abstract
      Documents/FileDocument.cs
      Documents/FileWatcher.cs
      Render/MdRenderer.cs
      Render/PreviewHtml.cs
      Render/PreviewPipeline.cs
      Render/Assets/{preview.css, dark.css, hc.css, prism.min.js}
      Web/WebViewHost.xaml(.cs)
      Web/LinkInterceptor.cs
      Editor/EditorHost.xaml(.cs)      # empty placeholder, v1
      Theme/HighContrastWatcher.cs
      Install/FileAssocInstaller.cs
      Spectacle.csproj
  test/
    Spectacle.Tests/                   # xUnit
      CliArgsTests.cs
      FileGuardTests.cs
      MdRendererTests.cs               # HTML snapshot tests
      FileDocumentTests.cs
      FileAssocInstallerTests.cs       # against a temp HKCU subkey
  docs/
    superpowers/specs/2026-05-13-spectacle-design.md   # this file
  Spectacle.sln
  README.md
  .gitignore
```

## 11. Build & Distribution

- `.NET 8`, `<TargetFramework>net8.0-windows</TargetFramework>`, `<UseWPF>true</UseWPF>`.
- WebView2: `Microsoft.Web.WebView2` NuGet. Relies on the Evergreen Runtime (preinstalled on Windows 11).
- Markdown: `Markdig` NuGet.
- Publish: `dotnet publish -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true` → one `Spectacle.exe` (~10–20 MB framework-dependent).
- No installer for v1 — drop into `C:\Tools\Spectacle\`, run `Spectacle.exe --register`.

## 12. Testing Strategy

- **Unit:** `CliArgs` (argv parsing), `FileGuard` (allowlist), `MdRenderer` (snapshot tests against curated fixtures: tables, code blocks, task lists, nested lists, blockquotes, footnotes, images).
- **Unit (accessibility):** `PaletteContrastTests` — asserts every foreground/background pair in `dark.css` and `hc.css` meets WCAG AA (4.5:1) and body+code meet AAA (7:1). Uses the WCAG relative-luminance formula in test code.
- **Unit (filesystem):** `FileDocument` (temp-file roundtrip with the file watcher).
- **Unit (registry):** `FileAssocInstaller` against a temp `HKCU\Software\Classes\Spectacle.Tests.<guid>` subtree to avoid polluting real associations.
- **Manual smoke:**
  1. Launch on a sample doc covering all GFM features. Confirm dark theme renders.
  2. Toggle Windows light/dark and confirm the app **stays dark** (no theme switch).
  3. Enable a Windows Contrast Theme (Settings → Accessibility → Contrast themes) and confirm `hc.css` engages.
  4. Tab through links and confirm visible focus outlines.
  5. Run Narrator and confirm headings, lists, and tables are announced with semantics.
  6. Save the file and observe auto-reload.
  7. Click an external link and confirm it opens in the default browser.
  8. Attempt to open a `.txt` file and confirm rejection with exit code 2.
  9. Run `--register`, double-click a `.md` from Explorer, confirm Spectacle launches.

## 13. Non-Goals (v1)

- Editing
- Export to PDF / HTML
- Tabs / multi-document
- User-defined themes
- Plugins / extensions
- Math (KaTeX)
- Mermaid diagrams
- Cross-platform support — Windows only
- MSI / installer

## 14. Risks & Open Items

| Risk | Mitigation |
|---|---|
| WebView2 Evergreen Runtime missing on a stripped Win11 install. | Detect on startup; show a one-shot error window with a link to the Microsoft download page; exit non-zero. |
| `FileSystemWatcher` debounce edge cases (editor "atomic save" temp-file dance). | Watch parent directory + filename filter, debounce 150 ms, re-read on any `Changed`/`Renamed`/`Created` event matching the filename. |
| Prism.js bundle size vs. language coverage. | Restrict to the curated set in §6. Add languages as user requests them. |
| Per-user file association silently overridden by another app. | `--register` is idempotent; document re-running it. |

## 15. Defaults

| Question | Default |
|---|---|
| Math / Mermaid in v1? | No |
| Installer (MSI) in v1? | No |
| Self-contained .exe? | No — framework-dependent (smaller) |
| Theme | **Dark only.** No light theme. Windows light/dark setting is ignored. |
| High Contrast respected? | Yes — `hc.css` engages when Windows High Contrast / Contrast Themes is active |
| Contrast target | WCAG AA minimum, AAA where feasible (body + code) |
| External links | Open in default browser |
| Convenience PS function name | `spectacle` |
| Allowed extensions | `.md`, `.markdown` (case-insensitive) |
