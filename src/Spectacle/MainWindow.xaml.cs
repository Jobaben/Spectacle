using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using Spectacle.Annotations;
using Spectacle.Documents;
using Spectacle.Render;
using Spectacle.Theme;

namespace Spectacle;

public partial class MainWindow : Window, IPreviewSink
{
    private readonly FileDocument _document;
    private readonly AnnotationStore _store;
    private readonly PreviewPipeline _pipeline;
    private readonly HighContrastWatcher _hcWatcher = new();
    private readonly string _sourcePath;
    private double _zoom = 1.0;
    private WindowState _preFullScreenState;
    private WindowStyle _preFullScreenStyle;

    public ICommand ReloadCommand { get; }
    public ICommand ZoomInCommand { get; }
    public ICommand ZoomOutCommand { get; }
    public ICommand ZoomResetCommand { get; }
    public ICommand FullscreenCommand { get; }
    public ICommand CloseCommand { get; }
    public ICommand CopyRevisionPlanCommand { get; }
    public ICommand ExportRevisionPlanCommand { get; }

    public MainWindow(string filePath)
    {
        InitializeComponent();

        _sourcePath = Path.GetFullPath(filePath);
        _document = FileDocument.Open(filePath);
        _store = new AnnotationStore(filePath);
        Title = $"{System.IO.Path.GetFileName(filePath)} — Spectacle";
        Web.SetVirtualFolder(_document.BaseDirectory);

        var theme = _hcWatcher.IsActive ? PreviewTheme.HighContrast : PreviewTheme.Dark;
        _pipeline = new PreviewPipeline(_document, this, theme, _store);
        _hcWatcher.Changed += (_, _) => Dispatcher.Invoke(() =>
            _pipeline.SetTheme(_hcWatcher.IsActive ? PreviewTheme.HighContrast : PreviewTheme.Dark));

        ReloadCommand = new RelayCommand(_ => Web.Reload());
        ZoomInCommand = new RelayCommand(_ => SetZoom(_zoom + 0.1));
        ZoomOutCommand = new RelayCommand(_ => SetZoom(_zoom - 0.1));
        ZoomResetCommand = new RelayCommand(_ => SetZoom(1.0));
        FullscreenCommand = new RelayCommand(_ => ToggleFullscreen());
        CloseCommand = new RelayCommand(_ => Close());

        CopyRevisionPlanCommand = new RelayCommand(_ => CopyRevisionPlan(), HasComments);
        ExportRevisionPlanCommand = new RelayCommand(_ => ExportRevisionPlan(), HasComments);

        Web.HostMessageReceived += (_, json) => Dispatcher.Invoke(() =>
        {
            _pipeline.HandleHostMessage(json);
            UpdateTopBar();
        });

        _pipeline.Rendered += (_, _) => Dispatcher.Invoke(UpdateTopBar);

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

    private bool HasComments()
        => _pipeline.SnapshotMatched().Count + _pipeline.SnapshotOrphans().Count > 0;

    private string BuildRevisionPlan()
    {
        var matched = _pipeline.SnapshotMatched();
        var content = File.ReadAllText(_sourcePath);
        var sha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
        return RevisionPlanExporter.Build(_sourcePath, sha, DateTime.UtcNow, matched);
    }

    private void CopyRevisionPlan()
    {
        var text = BuildRevisionPlan();
        System.Windows.Clipboard.SetText(text);
    }

    private void ExportRevisionPlan()
    {
        var text = BuildRevisionPlan();
        var dlg = new SaveFileDialog
        {
            FileName = Path.GetFileNameWithoutExtension(_sourcePath) + ".revisions.md",
            Filter = "Markdown (*.md)|*.md|All files (*.*)|*.*",
            InitialDirectory = Path.GetDirectoryName(_sourcePath)
        };
        if (dlg.ShowDialog() == true)
            File.WriteAllText(dlg.FileName, text);
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
