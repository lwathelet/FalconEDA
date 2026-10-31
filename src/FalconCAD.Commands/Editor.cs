using FalconCAD.Core;

namespace FalconCAD.Commands;

/// <summary>Editing state shared by the command line, the tools and the canvas.</summary>
public sealed class Editor
{
    public Drawing Drawing { get; } = Drawing.CreateSchematic();
    public GridSettings Grid { get; } = new();
    public int ActiveLayer { get; set; } = Drawing.NetLayer;

    /// <summary>The interactive tool waiting for mouse clicks, if any.</summary>
    public ITool? Tool { get; private set; }

    /// <summary>Raised when the drawing, grid or tool changes.</summary>
    public event Action? Changed;

    /// <summary>Raised to change the view: a zoom factor, or null to fit the drawing.</summary>
    public event Action<double?>? ZoomRequested;

    public void SetTool(ITool? tool)
    {
        Tool = tool;
        NotifyChanged();
    }

    public void NotifyChanged() => Changed?.Invoke();

    public void RequestZoom(double? factor) => ZoomRequested?.Invoke(factor);
}
