using FalconEDA.Core;

namespace FalconEDA.Commands;

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

    /// <summary>The current group, shown highlighted; MOVE, COPY, CUT, ROTATE, MIRROR and Del act on it.</summary>
    public Selection Selection { get; } = new();

    /// <summary>What COPY or CUT put on the clipboard for PASTE.</summary>
    public ClipboardContent? Clipboard { get; set; }

    /// <summary>Raised when the drawing, grid or tool changes.</summary>
    public event Action? Changed;

    /// <summary>Raised by WRITE: a file path, or null to save under the current name.</summary>
    public event Action<string?>? SaveRequested;

    /// <summary>True when there are changes that have not been saved.</summary>
    public bool IsModified => Schematic.Sheets.Any(s => s.Modified);

    /// <summary>Raised to change the view: a zoom factor, or null to fit the drawing.</summary>
    public event Action<double?>? ZoomRequested;

    public void Load(Schematic schematic, string? filePath)
    {
        Schematic = schematic;
        FilePath = filePath;
        SheetIndex = 0;
        Tool = null;
        Selection.Clear();
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
        Selection.Clear();
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

    /// <summary>Throws away the changes since the last <see cref="Checkpoint"/>, e.g. when a move is cancelled.</summary>
    public void RevertCheckpoint()
    {
        if (_undo.Count == 0)
            return;
        var (sheet, snapshot) = _undo.Pop();
        SheetIndex = sheet;
        Sheet.Restore(snapshot);
        Selection.Clear();
        NotifyChanged();
    }

    public bool Undo() => Step(_undo, _redo);

    public bool Redo() => Step(_redo, _undo);

    public void RequestSave(string? path) => SaveRequested?.Invoke(path);

    /// <summary>Call after the schematic has been written to <paramref name="path"/>.</summary>
    public void MarkSaved(string path)
    {
        FilePath = path;
        NotifyChanged();
    }

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

        // Undo restores copies of the objects, so the old group no longer refers to anything on the sheet.
        Selection.Clear();
        NotifyChanged();
        return true;
    }
}
