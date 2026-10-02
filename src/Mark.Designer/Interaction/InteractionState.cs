using Mark.Core.Geometry;
using Mark.Core.Interaction;

namespace Mark.Designer.Interaction;

/// <summary>What the pointer does in the drawing view. Each mode has one tool.</summary>
public enum InteractionMode
{
    /// <summary>Select, box-select, move, resize (the default).</summary>
    Select,

    /// <summary>Left-drag pans the view.</summary>
    Pan,

    /// <summary>Drag a rectangle to create a frame.</summary>
    CreateFrame,

    /// <summary>Click a glass panel to split it with a mullion.</summary>
    AddMullion,

    /// <summary>Click a glass panel to split it with a transom.</summary>
    AddTransom
}

/// <summary>
/// Transient, UI-session interaction state that the renderer draws on top of the committed model:
/// the current operation's preview and the selection rectangle. Nothing here is domain data and
/// nothing here is ever saved.
/// </summary>
public sealed class InteractionState
{
    public OperationPreview Preview { get; private set; } = OperationPreview.Empty;

    /// <summary>World rectangle of an in-progress box selection, or null.</summary>
    public Rectangle2D? SelectionBox { get; private set; }

    /// <summary>True for a crossing selection (dragged right → left).</summary>
    public bool IsCrossingSelection { get; private set; }

    /// <summary>World rectangle a library design would be applied to if dropped now (drag and drop), or null.</summary>
    public Rectangle2D? DropTarget { get; private set; }

    public void SetDropTarget(Rectangle2D? target)
    {
        if (DropTarget == target) return;
        DropTarget = target;
        Changed?.Invoke(false);
    }

    /// <summary>Raised on change; the flag says whether the preview (and so the drawn frames) changed.</summary>
    public event Action<bool>? Changed;

    public void SetPreview(OperationPreview preview)
    {
        Preview = preview ?? OperationPreview.Empty;
        Changed?.Invoke(true);
    }

    public void SetSelectionBox(Rectangle2D? box, bool crossing)
    {
        SelectionBox = box;
        IsCrossingSelection = crossing;
        Changed?.Invoke(false);
    }

    /// <summary>Removes every preview (cancel / commit / tool switch).</summary>
    public void Clear()
    {
        bool hadPreview = !Preview.IsEmpty;
        bool hadBox = SelectionBox is not null || DropTarget is not null;
        Preview = OperationPreview.Empty;
        SelectionBox = null;
        DropTarget = null;
        if (hadPreview || hadBox) Changed?.Invoke(hadPreview);
    }
}
