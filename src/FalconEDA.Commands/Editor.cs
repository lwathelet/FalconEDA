using FalconEDA.Core;

namespace FalconEDA.Commands;

/// <summary>Editing state shared by the command line, the tools and the canvas.</summary>
public sealed class Editor
{
    private readonly Stack<UndoStep> _undo = new();
    private readonly Stack<UndoStep> _redo = new();

    /// <summary>One step of UNDO: the sheet shown, and the sheets it changed as they were before.</summary>
    private sealed record UndoStep(int ShownSheet, IReadOnlyList<(int Sheet, SheetSnapshot Snapshot)> Sheets);

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

    /// <summary>What SHOW points out (a net, a part), until the next command.</summary>
    public Selection Highlight { get; } = new();

    /// <summary>A message from a tool for the status bar (e.g. what SHOW found); taken by the app when shown.</summary>
    public string? Message { get; set; }

    /// <summary>Raised by NAME for a part or net: the app asks for the new name.</summary>
    public event Action<string>? RenameRequested;

    public void RequestRename(string name) => RenameRequested?.Invoke(name);

    /// <summary>What COPY or CUT put on the clipboard for PASTE.</summary>
    public ClipboardContent? Clipboard { get; set; }

    /// <summary>Where ADD finds library files.</summary>
    public ILibraryProvider? LibraryProvider { get; set; }

    /// <summary>Raised by ADD without a device: the app opens the library browser, optionally filtered.</summary>
    public event Action<string?>? AddDialogRequested;

    public void RequestAddDialog(string? filter) => AddDialogRequested?.Invoke(filter);

    /// <summary>Raised by VALUE for a part: the app shows the table of the part's values.</summary>
    public event Action<string>? ValueDialogRequested;

    public void RequestValueDialog(string part) => ValueDialogRequested?.Invoke(part);

    /// <summary>Raised when the drawing, grid or tool changes.</summary>
    public event Action? Changed;

    /// <summary>Raised by WRITE: a file path, or null to save under the current name.</summary>
    public event Action<string?>? SaveRequested;

    /// <summary>True when there are changes that have not been saved.</summary>
    public bool IsModified => Schematic.PartsModified || Schematic.Sheets.Any(s => s.Modified);

    /// <summary>Raised to change the view: a zoom factor, or null to fit the drawing.</summary>
    public event Action<double?>? ZoomRequested;

    public void Load(Schematic schematic, string? filePath)
    {
        Schematic = schematic;
        FilePath = filePath;
        SheetIndex = 0;
        Tool = null;
        Selection.Clear();
        Highlight.Clear();
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
        Highlight.Clear();
        SheetIndex = index;
        NotifyChanged();
        RequestZoom(null);
    }

    public void SetTool(ITool? tool)
    {
        Tool = tool;
        NotifyChanged();
    }

    /// <summary>
    /// Call before every change to the drawing, so it can be undone. Changes that reach other sheets too
    /// (renaming a part or net) pass <paramref name="allSheets"/>.
    /// </summary>
    public void Checkpoint(bool allSheets = false)
    {
        _undo.Push(Capture(allSheets ? Enumerable.Range(0, Schematic.Sheets.Count) : [SheetIndex]));
        _redo.Clear();
    }

    /// <summary>Throws away the changes since the last <see cref="Checkpoint"/>, e.g. when a move is cancelled.</summary>
    public void RevertCheckpoint()
    {
        if (_undo.Count == 0)
            return;
        Restore(_undo.Pop());
        NotifyChanged();
    }

    public void ClearHighlight()
    {
        if (Highlight.IsEmpty)
            return;
        Highlight.Clear();
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

    private bool Step(Stack<UndoStep> from, Stack<UndoStep> to)
    {
        if (from.Count == 0)
            return false;

        var step = from.Pop();
        to.Push(Capture(step.Sheets.Select(s => s.Sheet)) with { ShownSheet = step.ShownSheet });
        Restore(step);
        NotifyChanged();
        return true;
    }

    private UndoStep Capture(IEnumerable<int> sheets) =>
        new(SheetIndex, sheets.Select(i => (i, Schematic.Sheets[i].Capture())).ToList());

    private void Restore(UndoStep step)
    {
        foreach (var (sheet, snapshot) in step.Sheets)
            Schematic.Sheets[sheet].Restore(snapshot);
        SheetIndex = step.ShownSheet;

        // Undo restores copies of the objects, so the old group no longer refers to anything on the sheet.
        Selection.Clear();
        Highlight.Clear();
    }
}
