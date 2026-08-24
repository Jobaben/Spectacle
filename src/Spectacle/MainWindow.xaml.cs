using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using Spectacle.Ai;
using Spectacle.Annotations;
using Spectacle.Documents;
using Spectacle.Export;
using Spectacle.Files;
using Spectacle.Render;
using Spectacle.Theme;

namespace Spectacle;

public partial class MainWindow : Window, IPreviewSink
{
    private readonly FileDocument _document;
    private readonly AnnotationStore _store;
    private readonly PreviewPipeline _pipeline;
    private readonly HighContrastWatcher _hcWatcher = new();
    private readonly RecentFilesStore _recent = RecentFilesStore.Default();
    private readonly string _sourcePath;
    private PreviewTheme _userTheme = PreviewTheme.Dark;
    private double _zoom = 1.0;
    private WindowState _preFullScreenState;
    private WindowStyle _preFullScreenStyle;

    public ICommand ReloadCommand { get; }
    public ICommand ZoomInCommand { get; }
    public ICommand ZoomOutCommand { get; }
    public ICommand ZoomResetCommand { get; }
    public ICommand FullscreenCommand { get; }
    public ICommand ExportHtmlCommand { get; }
    public ICommand ToggleThemeCommand { get; }
    public ICommand OpenFileCommand { get; }
    public ICommand OpenRecentCommand { get; }

    public MainWindow(string filePath)
    {
        InitializeComponent();
        SourceInitialized += ApplyStartupGeometry;

        _sourcePath = Path.GetFullPath(filePath);
        _document = FileDocument.Open(filePath);
        _store = new AnnotationStore(filePath);
        Title = $"{System.IO.Path.GetFileName(filePath)} — Spectacle";
        Web.SetVirtualFolder(_document.BaseDirectory);
        _recent.Add(_sourcePath);

        _pipeline = new PreviewPipeline(_document, this, EffectiveTheme(), _store);
        _hcWatcher.Changed += (_, _) => Dispatcher.Invoke(() => _pipeline.SetTheme(EffectiveTheme()));

        ReloadCommand = new RelayCommand(_ => Web.Reload());
        ZoomInCommand = new RelayCommand(_ => SetZoom(_zoom + 0.1));
        ZoomOutCommand = new RelayCommand(_ => SetZoom(_zoom - 0.1));
        ZoomResetCommand = new RelayCommand(_ => SetZoom(1.0));
        FullscreenCommand = new RelayCommand(_ => ToggleFullscreen());

        ExportHtmlCommand = new RelayCommand(_ => ExportHtml());
        ToggleThemeCommand = new RelayCommand(_ => ToggleTheme());
        OpenFileCommand = new RelayCommand(_ => OpenFile());
        OpenRecentCommand = new RelayCommand(_ => OpenMostRecent());

        Web.HostMessageReceived += (_, json) => Dispatcher.Invoke(() =>
        {
            // Esc is hierarchical: each open preview layer closes itself in the page, and only
            // an idle preview escalates to closing the window — by asking the host.
            if (IsCloseWindowMessage(json)) { Close(); return; }
            _pipeline.HandleHostMessage(json);
            UpdateTopBar();
        });

        // The preview assembles every revision brief — the triaged fix brief and the
        // unresolved-comment brief alike; placing the text on the clipboard is the host's job.
        _pipeline.CopyTextRequested += (_, text) => Dispatcher.Invoke(() =>
            System.Windows.Clipboard.SetText(text));

        // Hands-free revision. With a Claude CLI installed on this machine the copy → paste → wait
        // round-trip is unnecessary: the triage panel's "a" hands the same brief to `claude -p` in
        // a background process, addressed at this exact file so the saves land in the open
        // document — where the watcher already turns each one into a loop iteration. No CLI, no
        // wiring: the panel then offers only the clipboard path.
        var claudeCli = ClaudeCliLocator.Detect();
        if (claudeCli is not null)
        {
            var runner = new ClaudeRevisionRunner(claudeCli);
            // The pipeline owns the run's whole account: the running chip, the live turn/edit
            // progress the stream reports, and the finished run's timeline record — including a
            // run that failed or saved nothing, which used to vanish without a trace.
            // What the chip says while the run gets going: normally nothing, but a run that could
            // not resolve a project root says so, because "which scope did this run get" is not
            // otherwise visible anywhere. The service sets it before the process spawns, so the
            // runner's Started event below can never overwrite a note that has not arrived yet.
            string? scopeNote = null;
            runner.Started += (_, _) => _pipeline.OnClaudeRunStarted(scopeNote);
            runner.Progress += (_, p) => _pipeline.OnClaudeRunProgress(p);
            runner.Completed += (_, r) => _pipeline.OnClaudeRunCompleted(r);
            // Every revision goes through the service, never straight to the runner: it is what
            // establishes the artifact-continuity invariant — the run starts in the document's own
            // Claude project root, so the project's instructions, settings, rules and hooks load
            // rather than the user-scope configuration alone.
            var revisions = new ClaudeArtifactRevisionService(runner);
            _pipeline.ClaudeReviseRequested += (_, brief) =>
                revisions.Revise(_sourcePath, _document.BaseDirectory, brief, note => scopeNote = note);
            _pipeline.SetClaudeStatus(ClaudeRevisionStatus.Idle);
        }

        _pipeline.Rendered += (_, _) => Dispatcher.Invoke(() =>
        {
            UpdateTopBar();
            UpdateStatsBar();
        });

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

    /// <summary>
    /// True for the preview's <c>closeWindow</c> message — the page's idle-Esc escalation. Any
    /// other payload (or non-JSON) belongs to the pipeline.
    /// </summary>
    private static bool IsCloseWindowMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("type", out var type)
                && type.GetString() == "closeWindow";
        }
        catch (JsonException)
        {
            return false;
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        // Esc closes the window only when the preview is idle, and while keyboard focus is in
        // the preview the page is the one that knows: an open layer (find, outline, gate, loop,
        // help, composer) takes the Esc itself, and an idle page posts closeWindow back to the
        // host. WebView2 re-raises Esc into WPF routing even when the browser has focus, so the
        // focus check is what keeps this handler from closing the window over an open panel.
        if (e.Key == Key.Escape && !Web.IsKeyboardFocusWithin)
        {
            e.Handled = true;
            Close();
            return;
        }
        base.OnPreviewKeyDown(e);
    }

    private void ApplyStartupGeometry(object? sender, EventArgs e)
    {
        SourceInitialized -= ApplyStartupGeometry;

        const double startupWidth = 915;
        var workArea = SystemParameters.WorkArea;

        WindowStartupLocation = WindowStartupLocation.Manual;
        Width  = startupWidth;
        Height = workArea.Height;
        Left   = workArea.X + (workArea.Width - startupWidth) / 2;
        Top    = workArea.Y;
    }

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
    }

    private void UpdateStatsBar()
    {
        var stats = DocumentStats.Compute(_document.Text);
        StatsText.Text = stats.Words == 0
            ? "Empty document"
            : $"{stats.Words:N0} words · ~{stats.ReadingTimeMinutes} min read · "
              + $"{stats.Headings:N0} headings · {stats.CodeBlocks:N0} code blocks"
              + GateStatus();
    }

    // The same verdict the badge shows, condensed for the status bar — so the gate state is
    // readable even with the preview scrolled somewhere the badge is not.
    private string GateStatus()
    {
        var v = _pipeline.SnapshotVerdict();
        if (v is null) return "";
        return v.Passed
            ? " · gate PASS"
            : $" · gate FAIL ({v.BlockingCount} blocking)";
    }

    // The OS high-contrast setting always wins; otherwise the user's Ctrl+T choice applies.
    private PreviewTheme EffectiveTheme() =>
        _hcWatcher.IsActive ? PreviewTheme.HighContrast : _userTheme;

    private void ToggleTheme()
    {
        // Ctrl+T flips the user preference between dark and light. While the OS forces
        // high contrast the preview stays high-contrast, but the preference still
        // toggles underneath so it takes effect the moment high contrast is turned off.
        _userTheme = _userTheme == PreviewTheme.Dark ? PreviewTheme.Light : PreviewTheme.Dark;
        _pipeline.SetTheme(EffectiveTheme());
    }

    private void OpenFile()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Markdown (*.md;*.markdown)|*.md;*.markdown|All files (*.*)|*.*",
            InitialDirectory = Path.GetDirectoryName(_sourcePath)
        };
        if (dlg.ShowDialog() == true)
            OpenInNewWindow(dlg.FileName);
    }

    private void OpenMostRecent()
    {
        // Reopen the newest still-present document other than the one already on screen —
        // a fast "back to my last file" without touching the mouse.
        var previous = _recent.LoadExisting()
            .FirstOrDefault(p => !string.Equals(p, _sourcePath, StringComparison.OrdinalIgnoreCase));
        if (previous is not null)
            OpenInNewWindow(previous);
    }

    private void OpenInNewWindow(string path)
    {
        if (!FileGuard.IsAllowed(path) || !File.Exists(path))
            return;

        // A fresh window mirrors how the OS launches Spectacle per file, keeping each
        // document's annotations, zoom and theme state independent.
        var window = new MainWindow(path);
        window.Show();
        window.Activate();
    }

    private void ExportHtml()
    {
        var theme = EffectiveTheme();
        var title = Path.GetFileNameWithoutExtension(_sourcePath) ?? "document";
        var html = HtmlExporter.FromMarkdown(_document.Text, theme, title);

        var dlg = new SaveFileDialog
        {
            FileName = title + ".html",
            Filter = "HTML (*.html)|*.html|All files (*.*)|*.*",
            InitialDirectory = Path.GetDirectoryName(_sourcePath)
        };
        if (dlg.ShowDialog() == true)
            File.WriteAllText(dlg.FileName, html);
    }
}

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
