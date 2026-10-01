using Fenestration.Designer.Interaction;
using Fenestration.Designer.ViewModels;

namespace Fenestration.Designer.Tools;

/// <summary>
/// Shared tool behaviour. Keys are handled consistently across tools:
/// <list type="bullet">
///   <item>Esc: cancel the operation in progress; if nothing is in progress, <see cref="OnEscapeIdle"/>
///         (the Select tool clears the selection, other tools return to Select mode).</item>
///   <item>Delete: delete the selection (when no operation is in progress).</item>
///   <item>Ctrl+A: select everything (when no operation is in progress).</item>
/// </list>
/// </summary>
public abstract class DesignerToolBase : IViewportTool
{
    protected DesignerToolBase(MainViewModel host) => Host = host ?? throw new ArgumentNullException(nameof(host));

    protected MainViewModel Host { get; }

    public abstract bool IsCapturing { get; }

    public virtual void OnPointerDown(ViewportPointerEventArgs e) { }

    public virtual void OnPointerMove(ViewportPointerEventArgs e) { }

    public virtual void OnPointerUp(ViewportPointerEventArgs e) { }

    public abstract void Cancel();

    public virtual bool OnKey(ViewportKey key)
    {
        switch (key)
        {
            case ViewportKey.Escape when IsCapturing || !Host.Interaction.Preview.IsEmpty:
                Cancel();
                return true;
            case ViewportKey.Escape:
                return OnEscapeIdle();
            case ViewportKey.Delete when !IsCapturing:
                Host.DeleteSelection();
                return true;
            case ViewportKey.SelectAll when !IsCapturing:
                Host.SelectAll();
                return true;
            default:
                return false;
        }
    }

    /// <summary>Esc with nothing in progress. Default: go back to the Select tool.</summary>
    protected virtual bool OnEscapeIdle()
    {
        Host.Mode = InteractionMode.Select;
        return true;
    }

    /// <summary>Converts an on-screen distance to world mm at the current zoom.</summary>
    protected double PixelsToMm(double pixels) => Host.Canvas.ScreenToWorldDistance(pixels);
}
