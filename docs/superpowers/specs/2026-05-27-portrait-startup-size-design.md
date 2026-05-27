# Portrait Startup Window Size — Design

**Status:** Draft
**Date:** 2026-05-27
**Scope:** Change the default startup geometry of `MainWindow` from landscape (1100 × 800) to a tall, portrait shape that fills the monitor's work area vertically.

---

## 1. Goal

When the user opens a `.md` file with Spectacle, the window appears as a tall, narrow column rather than a wide landscape rectangle. Specifically: **900 DIP wide × the monitor's full work-area height, centered horizontally** on the primary monitor's work area, with its top edge flush against the top of the work area.

Markdown documents are vertical by nature — more reading space per pixel comes from height, not width. Today's 1100 × 800 default optimizes for the opposite.

## 2. Non-goals (v1)

- Persisting last-used window size or position across launches.
- Multi-monitor-aware placement (uses the **primary** monitor's work area).
- A user-facing setting to change the default.
- Reacting to DPI changes or work-area changes after launch.
- Changing fullscreen / maximize behavior (`F11` and `WindowState.Maximized` are unchanged).

## 3. Current behavior

`src/Spectacle/MainWindow.xaml` declares `Width="1100" Height="800"` on the root `<Window>`. No code anywhere reads, writes, or persists window geometry — `MainWindow.xaml.cs` only manipulates `WindowState` for the `F11` fullscreen toggle. The window opens wherever WPF's default placement puts it (typically top-left or staggered).

## 4. Design

### 4.1 XAML change

Remove the two attributes from `MainWindow.xaml`:

- Remove `Width="1100"`
- Remove `Height="800"`

Leave `Title`, `Background`, `Foreground`, `UseLayoutRounding`, `InputBindings`, and the grid layout untouched.

### 4.2 Code-behind change

In `MainWindow.xaml.cs`, subscribe to `SourceInitialized` and apply startup geometry once. `SourceInitialized` fires after the HWND exists but before the first render — the correct seam for size/position changes that should not flicker.

```csharp
public MainWindow(string filePath)
{
    InitializeComponent();
    // … existing setup …
    SourceInitialized += ApplyStartupGeometry;
}

private void ApplyStartupGeometry(object? sender, EventArgs e)
{
    SourceInitialized -= ApplyStartupGeometry;   // run once

    const double startupWidth = 900;
    var workArea = SystemParameters.WorkArea;    // DIPs, taskbar excluded

    WindowStartupLocation = WindowStartupLocation.Manual;
    Width  = startupWidth;
    Height = workArea.Height;
    Left   = workArea.X + (workArea.Width - startupWidth) / 2;
    Top    = workArea.Y;
}
```

### 4.3 Why `SourceInitialized` and not the constructor

Setting `Left`/`Top` in the constructor works, but `WindowStartupLocation` defaults to `Manual` only if we set it; otherwise WPF can reapply default placement after `Loaded`. Doing it in `SourceInitialized` (and explicitly setting `WindowStartupLocation = Manual`) is the canonical seam and guarantees one paint at the correct geometry. It also keeps geometry concerns out of the constructor, which currently focuses on `DataContext`/binding setup.

### 4.4 DPI and units

WPF's `Width`, `Height`, `Left`, `Top` are device-independent pixels (DIPs). `SystemParameters.WorkArea` also returns DIPs. No conversion needed; same code works at 100% and 200% scaling.

### 4.5 User actions after launch

Once the startup handler unsubscribes itself, nothing else in the app touches `Width`/`Height`/`Left`/`Top`. The user can freely resize, drag, maximize, or `F11`-fullscreen the window. `F11` continues to toggle between `Maximized` and the previous `WindowState` — exactly as before.

## 5. Trade-offs and known limits

- **Primary monitor only.** `SystemParameters.WorkArea` is always the primary monitor. If the user typically works on a secondary monitor, the window still sizes/centers based on the primary. Cross-monitor placement would require `System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle)` and a WinForms reference — out of scope.
- **No persistence.** Every launch resets to the computed portrait geometry. If we later add "remember last size", the startup handler must short-circuit when persisted bounds exist.
- **Fixed 900 width.** Hard-coded rather than ratio-based. On a very narrow monitor (< 900 DIP wide work area, e.g., a portrait-mode tablet display), `Left` would be negative. Acceptable for v1 since Spectacle targets desktop monitors; if it becomes a real concern, clamp with `Math.Max(workArea.X, …)`.

## 6. Testing

WPF window sizing has no headless surface — no new automated tests.

Verification path (per `CLAUDE.md`: "ready for IDE verification"):

1. `dotnet build` — must succeed clean.
2. Existing `Spectacle.Tests` suite — must still pass.
3. Manual via IDE launch of a sample `.md`:
   - Window opens 900 wide, work-area-tall, centered horizontally, top flush with work-area top.
   - Window can be resized, dragged, maximized, restored.
   - `F11` toggles fullscreen and back without losing the portrait geometry on restore.
   - Repeat on a non-100% DPI display to confirm DIP math.

## 7. Affected files

- `src/Spectacle/MainWindow.xaml` — remove two attributes.
- `src/Spectacle/MainWindow.xaml.cs` — add `SourceInitialized` subscription + handler (~12 lines).

No other files, no new dependencies, no new public API.
