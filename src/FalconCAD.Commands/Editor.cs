using FalconCAD.Core;

namespace FalconCAD.Commands;

/// <summary>Editing state shared by the command line, the tools and the canvas.</summary>
public sealed class Editor
{
    private readonly Stack<(int Sheet, SheetSnapshot Snapshot)> _undo = new();
    private readonly Stack<(int Sheet, SheetSnapshot Snapshot)> _redo = new();

    public Schematic Schematic { get; private set; } = Schematic.CreateNew();
    public string? FilePath { get; private set; }
    public int SheetIndex { get; private set; }
    public Sheet Sheet => Schematic.Sheets[SheetIndex];

    public GridSettings Grid { get; } = new();
    public int ActiveLayer { get; set; } = Sheet.NetLayer;

    /// <summary>The interactive tool waiting for mouse clicks, if any.</summary>
    public ITool? Tool { get; private set; }

    /// <summary>Raised when the drawing, grid or tool changes.</summary>
    public event Action? Changed;

    /// <summary>Raised to change the view: a zoom factor, or null to fit the drawing.</summary>
    public event Action<double?>? ZoomRequested;

    public void Load(Schematic schematic, string? filePath)
    {
        Schematic = schematic;
        FilePath = filePath;
        SheetIndex = 0;
        Tool = null;
        _undo.Clear();
        _redo.Clear();
        NotifyChanged();
        RequestZoom(null);
    }

    public void ShowSheet(int index)
    {
        if (index < 0 || index >= Schematic.Sheets.Count)
            throw new CommandException($"There is no sheet {index + 1}; this schematic has {Schematic.Sheets.Count}.");
        if (index == SheetIndex)
            return;

        Tool?.Cancel();
        SheetIndex = index;
        NotifyChanged();
        RequestZoom(null);
    }

    public void SetTool(ITool? tool)
    {
        Tool = tool;
        NotifyChanged();
    }

    /// <summary>Call before every change to the drawing, so it can be undone.</summary>
    public void Checkpoint()
    {
        _undo.Push((SheetIndex, Sheet.Capture()));
        _redo.Clear();
    }

    public bool Undo() => Step(_undo, _redo);

    public bool Redo() => Step(_redo, _undo);

    public void NotifyChanged() => Changed?.Invoke();

    public void RequestZoom(double? factor) => ZoomRequested?.Invoke(factor);

    private bool Step(Stack<(int Sheet, SheetSnapshot Snapshot)> from, Stack<(int Sheet, SheetSnapshot Snapshot)> to)
    {
        if (from.Count == 0)
            return false;

        var (sheet, snapshot) = from.Pop();
        SheetIndex = sheet;
        to.Push((sheet, Sheet.Capture()));
        Sheet.Restore(snapshot);
        NotifyChanged();
        return true;
    }
}
