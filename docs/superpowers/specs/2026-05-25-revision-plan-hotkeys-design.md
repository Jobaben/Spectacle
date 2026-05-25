# Revision Plan Hotkeys

**Date:** 2026-05-25
**Status:** Approved

## Goal

Add keyboard shortcuts for the two existing revision-plan actions in `MainWindow`'s top bar so users can copy or export the revision plan without clicking the buttons.

## Background

`MainWindow.xaml` exposes a top bar (`MainWindow.xaml:28-40`) containing two buttons:

- **Copy revision plan** → `CopyRevisionPlanCommand`
- **Export revision plan…** → `ExportRevisionPlanCommand`

Both commands already exist on `MainWindow` (`MainWindow.xaml.cs:31-32, 56-57`) and call `BuildRevisionPlan()` followed by either `Clipboard.SetText` or a `SaveFileDialog` flow.

The top bar is hidden when there are zero matched + orphan comments (`MainWindow.xaml.cs:101-116`); the buttons are only reachable when at least one comment exists.

`MainWindow.xaml` already declares a `Window.InputBindings` block with hotkeys for reload, zoom, fullscreen, and close (`MainWindow.xaml:11-22`).

## Decisions

### Key bindings

| Action | Hotkey |
|---|---|
| Copy revision plan | `Ctrl+Shift+C` |
| Export revision plan | `Ctrl+Shift+E` |

- Mnemonic (C = Copy, E = Export).
- `Shift` modifier preserves the system `Ctrl+C` for selecting and copying text inside the WebView2 preview.
- No conflict with existing `InputBindings` (F5, Ctrl+R, Ctrl+±, Ctrl+0, F11, Escape).

### Gating

Hotkeys must only fire when at least one comment exists — matching the visibility rule of the top bar.

Implemented via `ICommand.CanExecute` rather than an inline guard inside the handler, so that:

- The same predicate disables both the button and the hotkey.
- Buttons visibly grey out instead of looking clickable but doing nothing.

## Implementation

### `src/Spectacle/MainWindow.xaml`

Add two `KeyBinding` entries to the existing `Window.InputBindings` block:

```xml
<KeyBinding Key="C" Modifiers="Control+Shift" Command="{Binding CopyRevisionPlanCommand}" />
<KeyBinding Key="E" Modifiers="Control+Shift" Command="{Binding ExportRevisionPlanCommand}" />
```

No changes to the top bar markup.

### `src/Spectacle/MainWindow.xaml.cs`

**Extend `RelayCommand`** with an optional `canExecute` predicate and a method to raise `CanExecuteChanged`:

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

**Wire the predicate** when constructing the two revision-plan commands:

```csharp
CopyRevisionPlanCommand = new RelayCommand(_ => CopyRevisionPlan(), HasComments);
ExportRevisionPlanCommand = new RelayCommand(_ => ExportRevisionPlan(), HasComments);
```

Where `HasComments` is a private helper:

```csharp
private bool HasComments()
    => _pipeline.SnapshotMatched().Count + _pipeline.SnapshotOrphans().Count > 0;
```

**Notify WPF** of the changed enabled state at the end of `UpdateTopBar()`:

```csharp
((RelayCommand)CopyRevisionPlanCommand).RaiseCanExecuteChanged();
((RelayCommand)ExportRevisionPlanCommand).RaiseCanExecuteChanged();
```

Because the properties are declared as `ICommand`, the cast is required (or the property types can be tightened to `RelayCommand` for the two affected commands — implementation may pick either).

`UpdateTopBar` is already invoked on every `_pipeline.Rendered` and on every host message (`MainWindow.xaml.cs:59-65`), which covers every event that can change the comment count.

## Out of Scope

- Tooltips on the buttons showing the shortcut (e.g. "Copy revision plan (Ctrl+Shift+C)").
- Menu bar or command palette entries.
- User-configurable key bindings.
- Discoverability/help affordances.

## Testing

This is a WPF UI binding change. No automated test covers `Window.InputBindings` today, and adding one requires an STA UI test host that is not currently set up in `Spectacle.Tests`. Verification:

- `dotnet build` must succeed.
- Manual IDE run: open a markdown file with at least one comment; verify `Ctrl+Shift+C` puts the revision plan on the clipboard and `Ctrl+Shift+E` opens the save dialog. Open a file with no comments; verify both buttons are disabled and the hotkeys are no-ops.

## Trade-offs

- **Ctrl+Shift+C and browser devtools.** WebView2 does not bind Ctrl+Shift+C by default. The WPF window receives the key event first via `InputBindings`, so there is no conflict.
- **Discoverability.** With no menu and no tooltip, the shortcuts are not visible to new users. Acceptable for a developer-facing tool. Revisit if/when non-technical users adopt it.
- **`CanExecute` complexity vs. inline guard.** Inline guards are two `if` statements; the `CanExecute` route is ~10 extra lines but produces correct WPF semantics (disabled buttons, no-op hotkeys) from a single source of truth.
