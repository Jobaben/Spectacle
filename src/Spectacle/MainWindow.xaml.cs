using System.Windows;
using System.Windows.Input;
using Spectacle.Documents;
using Spectacle.Render;
using Spectacle.Theme;

namespace Spectacle;

public partial class MainWindow : Window, IPreviewSink
{
    private readonly FileDocument _document;
    private readonly Spectacle.Annotations.AnnotationStore _store;
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
        _store = new Spectacle.Annotations.AnnotationStore(filePath);
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
#pragma warning disable CS0067 // Event required by ICommand but never raised (CanExecute always true)
    public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067
}
