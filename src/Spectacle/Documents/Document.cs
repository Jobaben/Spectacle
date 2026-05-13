using System;

namespace Spectacle.Documents;

public abstract class Document : IDisposable
{
    public abstract string Text { get; }
    public abstract string BaseDirectory { get; }
    public event EventHandler? Changed;

    protected void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);

    public virtual void Dispose() => GC.SuppressFinalize(this);
}
