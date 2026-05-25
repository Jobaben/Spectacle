# Revision Plan Hotkeys Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `Ctrl+Shift+C` and `Ctrl+Shift+E` hotkeys for the existing Copy / Export revision-plan commands in `MainWindow`, gated so they only fire when at least one comment exists.

**Architecture:** Two new `KeyBinding` entries in the existing `Window.InputBindings` block bind to the existing `CopyRevisionPlanCommand` / `ExportRevisionPlanCommand`. Gating is enforced via `ICommand.CanExecute` driven by the same condition that already shows/hides the top bar, so buttons and hotkeys share a single source of truth. `RelayCommand` is extended in place with an optional `canExecute` predicate and a `RaiseCanExecuteChanged()` method that `UpdateTopBar()` invokes on every render and host message.

**Tech Stack:** WPF (.NET 8, net8.0-windows), C#, `ICommand` / `KeyBinding`. No new dependencies.

**Spec:** `docs/superpowers/specs/2026-05-25-revision-plan-hotkeys-design.md`

---

## File Structure

- **Modify:** `src/Spectacle/MainWindow.xaml.cs`
  - Extend the existing `internal sealed class RelayCommand` (currently lines 146-155) with an optional `Func<bool>? canExecute` constructor parameter and a `RaiseCanExecuteChanged()` method.
  - Add a `HasComments()` private helper to `MainWindow`.
  - Pass `HasComments` as the predicate when constructing `CopyRevisionPlanCommand` and `ExportRevisionPlanCommand` (currently lines 56-57).
  - In `UpdateTopBar()` (currently lines 101-116), call `RaiseCanExecuteChanged()` on both revision-plan commands after computing the visibility.
- **Modify:** `src/Spectacle/MainWindow.xaml`
  - Add two `KeyBinding` entries to the existing `Window.InputBindings` block (currently lines 11-22).

No new files. No test project changes — verification is manual per the spec (WPF `InputBindings` are not exercised by the existing xunit suite, and the test project has no STA UI host).

---

## Task 1: Extend `RelayCommand` and wire the predicates

**Files:**
- Modify: `src/Spectacle/MainWindow.xaml.cs` (the `RelayCommand` class at lines 146-155, the command construction at lines 56-57, and `UpdateTopBar` at lines 101-116)

- [ ] **Step 1: Replace the `RelayCommand` class body**

In `src/Spectacle/MainWindow.xaml.cs`, replace the existing `RelayCommand` class (currently lines 146-155) with:

```csharp
internal sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _exec;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action<object?> exec, Func<bool>? canExecute = null)
    {
        _exec = exec;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? p) => _canExecute?.Invoke() ?? true;
    public void Execute(object? p) => _exec(p);

    public event EventHandler? CanExecuteChanged;
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
```

Note: the existing `#pragma warning disable CS0067` / `#pragma warning restore CS0067` lines are removed — the event is now raised.

- [ ] **Step 2: Add a `HasComments` helper method**

In `src/Spectacle/MainWindow.xaml.cs`, inside the `MainWindow` class, add the following private helper. Place it immediately above `BuildRevisionPlan` (currently line 118):

```csharp
private bool HasComments()
    => _pipeline.SnapshotMatched().Count + _pipeline.SnapshotOrphans().Count > 0;
```

- [ ] **Step 3: Pass the predicate to the two revision-plan commands**

In `src/Spectacle/MainWindow.xaml.cs`, in the `MainWindow` constructor, replace the existing lines:

```csharp
CopyRevisionPlanCommand = new RelayCommand(_ => CopyRevisionPlan());
ExportRevisionPlanCommand = new RelayCommand(_ => ExportRevisionPlan());
```

with:

```csharp
CopyRevisionPlanCommand = new RelayCommand(_ => CopyRevisionPlan(), HasComments);
ExportRevisionPlanCommand = new RelayCommand(_ => ExportRevisionPlan(), HasComments);
```

- [ ] **Step 4: Raise `CanExecuteChanged` from `UpdateTopBar`**

In `src/Spectacle/MainWindow.xaml.cs`, modify `UpdateTopBar()`. The existing method body (lines 101-116) ends like this:

```csharp
TopBar.Visibility = System.Windows.Visibility.Visible;
StatusText.Text = orphanCount > 0
    ? $"{matchedCount} comment(s) • {orphanCount} orphaned"
    : $"{matchedCount} comment(s)";
```

Append, AFTER the existing closing of the early-return `if` block but as the final statements of the method (i.e. they must run on both the visible and collapsed branches), the following. Restructure so both branches fall through to a shared notification block:

```csharp
private void UpdateTopBar()
{
    var matchedCount = _pipeline.SnapshotMatched().Count;
    var orphanCount = _pipeline.SnapshotOrphans().Count;

    if (matchedCount + orphanCount == 0)
    {
        TopBar.Visibility = System.Windows.Visibility.Collapsed;
        StatusText.Text = "";
    }
    else
    {
        TopBar.Visibility = System.Windows.Visibility.Visible;
        StatusText.Text = orphanCount > 0
            ? $"{matchedCount} comment(s) • {orphanCount} orphaned"
            : $"{matchedCount} comment(s)";
    }

    ((RelayCommand)CopyRevisionPlanCommand).RaiseCanExecuteChanged();
    ((RelayCommand)ExportRevisionPlanCommand).RaiseCanExecuteChanged();
}
```

The casts are safe because the same file constructs both commands as `RelayCommand` instances.

- [ ] **Step 5: Build**

Run:

```powershell
dotnet build C:\GIT\Spectacle\Spectacle.sln
```

Expected: build succeeds with no errors. The previously-suppressed `CS0067` warning should no longer be present (and no new warnings should appear).

- [ ] **Step 6: Commit**

```bash
git add src/Spectacle/MainWindow.xaml.cs
git commit -m "feat(window): gate revision-plan commands on comment presence

Extend RelayCommand with an optional CanExecute predicate and raise
CanExecuteChanged whenever the top bar refreshes. The Copy / Export
revision-plan commands now disable when there are zero comments."
```

---

## Task 2: Add the `KeyBinding` entries

**Files:**
- Modify: `src/Spectacle/MainWindow.xaml` (the `Window.InputBindings` block, currently lines 11-22)

- [ ] **Step 1: Add two `KeyBinding` entries to `Window.InputBindings`**

In `src/Spectacle/MainWindow.xaml`, locate the existing `Window.InputBindings` block (currently lines 11-22). It ends with `<KeyBinding Key="Escape" Command="{Binding CloseCommand}" />`. Immediately before the closing `</Window.InputBindings>` tag, add:

```xml
<KeyBinding Key="C" Modifiers="Control+Shift" Command="{Binding CopyRevisionPlanCommand}" />
<KeyBinding Key="E" Modifiers="Control+Shift" Command="{Binding ExportRevisionPlanCommand}" />
```

The final block should look like:

```xml
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
    <KeyBinding Key="C" Modifiers="Control+Shift" Command="{Binding CopyRevisionPlanCommand}" />
    <KeyBinding Key="E" Modifiers="Control+Shift" Command="{Binding ExportRevisionPlanCommand}" />
</Window.InputBindings>
```

- [ ] **Step 2: Build**

Run:

```powershell
dotnet build C:\GIT\Spectacle\Spectacle.sln
```

Expected: build succeeds with no errors. (XAML compilation will validate that `CopyRevisionPlanCommand` and `ExportRevisionPlanCommand` resolve on the bound `DataContext`, which is `MainWindow` itself per `MainWindow.xaml.cs:67`.)

- [ ] **Step 3: Commit**

```bash
git add src/Spectacle/MainWindow.xaml
git commit -m "feat(window): hotkeys for revision plan (Ctrl+Shift+C / Ctrl+Shift+E)

Bind the existing Copy / Export revision-plan commands to Ctrl+Shift+C
and Ctrl+Shift+E so the buttons aren't the only entry point."
```

---

## Task 3: Manual verification

Automated coverage of `Window.InputBindings` is not feasible in the current test project (no STA UI host; `Spectacle.Tests` is a pure xunit unit-test project). Manual smoke is the contract from the spec.

**Files:** None (verification only).

- [ ] **Step 1: Full solution build**

Run:

```powershell
dotnet build C:\GIT\Spectacle\Spectacle.sln
```

Expected: build succeeds with zero errors and no new warnings.

- [ ] **Step 2: Run existing unit tests**

Run:

```powershell
dotnet test C:\GIT\Spectacle\Spectacle.sln
```

Expected: all existing tests pass. (Confirms the `RelayCommand` and `MainWindow` changes did not regress unrelated code that exercises commands.)

- [ ] **Step 3: Manual IDE smoke — happy path**

Launch Spectacle via the IDE (per `~/.claude/CLAUDE.md`, `dotnet run` does not work for this project — start it from Visual Studio / Rider). Open a markdown file from `test/Spectacle.Tests/Fixtures` or a working file that already has at least one revision comment attached (i.e. the top bar is visible).

Verify:
- The top bar shows the `Copy revision plan` and `Export revision plan…` buttons.
- Press `Ctrl+Shift+C`. The revision plan markdown is on the clipboard (paste into a scratch buffer to confirm).
- Press `Ctrl+Shift+E`. The Save dialog opens with `<filename>.revisions.md` pre-filled and the same default directory as the source file.

- [ ] **Step 4: Manual IDE smoke — gating**

Open a markdown file that has NO comments (or close all comments first).

Verify:
- The top bar is collapsed (no buttons visible).
- Press `Ctrl+Shift+C`. Nothing happens — no clipboard change, no exception in the IDE output window.
- Press `Ctrl+Shift+E`. Nothing happens — no Save dialog, no exception.

- [ ] **Step 5: Manual IDE smoke — no regression of existing shortcuts**

In the same window, exercise existing hotkeys to confirm no interference:
- `F5` — reload.
- `Ctrl++` / `Ctrl+-` / `Ctrl+0` — zoom in / out / reset.
- `F11` — fullscreen toggle.
- `Escape` — close.
- Select text in the WebView2 preview and press `Ctrl+C`. The selected text (not the revision plan) goes to the clipboard. This confirms `Ctrl+Shift+C` did not accidentally shadow the system `Ctrl+C`.

- [ ] **Step 6: Mark the task complete**

If all checks pass, report "ready for IDE verification — manual smoke passed" or "manual smoke complete". If a check fails, re-open Task 1 or Task 2 to diagnose.

No commit for this task (verification only).

---

## Notes for the implementing engineer

- **WPF binding source.** `DataContext = this;` is set on the window (`MainWindow.xaml.cs:67`), so `{Binding CopyRevisionPlanCommand}` and `{Binding ExportRevisionPlanCommand}` resolve to the public `ICommand` properties on `MainWindow`. No `RelativeSource` or `ElementName` plumbing is needed.
- **WebView2 and Ctrl+Shift+C.** The WPF window receives the key event first via `Window.InputBindings`. WebView2 does not bind `Ctrl+Shift+C` by default, so there's no contention. `Ctrl+C` remains untouched — text selection inside the preview still copies normally.
- **Re-querying.** WPF's command requery (`CommandManager.RequerySuggested`) fires on focus changes and other UI events; that, plus our explicit `RaiseCanExecuteChanged()` from `UpdateTopBar`, is enough. No timer / poll required.
- **Don't introduce `InternalsVisibleTo`** for `RelayCommand`. The spec deliberately keeps testing manual; leave `RelayCommand` `internal sealed`.
