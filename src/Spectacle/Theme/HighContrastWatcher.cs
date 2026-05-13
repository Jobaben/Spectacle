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
