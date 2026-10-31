using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FalconCAD.Commands;
using FalconCAD.Core;
using FalconCAD.IO;

namespace FalconCAD.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly List<string> _history = new();
    private int _historyIndex;
    private Point _cursor;

    [ObservableProperty]
    private string _commandText = "";

    [ObservableProperty]
    private string _status = "Type a command, or pick a tool on the left.";

    [ObservableProperty]
    private string _coordinates = "";

    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private IReadOnlyList<Layer> _layers = Array.Empty<Layer>();

    [ObservableProperty]
    private Layer? _activeLayer;

    [ObservableProperty]
    private IReadOnlyList<string> _sheetNames = Array.Empty<string>();

    [ObservableProperty]
    private int _sheetIndex;

    public MainViewModel()
    {
        Editor.Changed += OnEditorChanged;
        RefreshSchematic();
    }

    public Editor Editor { get; } = new();

    partial void OnActiveLayerChanged(Layer? value)
    {
        if (value is not null)
            Editor.ActiveLayer = value.Number;
    }

    partial void OnSheetIndexChanged(int value)
    {
        if (value >= 0 && value != Editor.SheetIndex)
            Execute($"EDIT .s{value + 1}");
    }

    public void OpenFile(string path)
    {
        try
        {
            Editor.Load(EagleSchematicReader.Read(path), path);
            RefreshSchematic();
            Status = $"Opened {path}";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Xml.XmlException or FormatException)
        {
            Status = $"Could not open {Path.GetFileName(path)}: {ex.Message}";
        }
    }

    /// <summary>Enter on the command line.</summary>
    [RelayCommand]
    private void RunCommand()
    {
        var text = CommandText.Trim();
        CommandText = "";
        if (text.Length == 0)
            return;

        _history.Add(text);
        _historyIndex = _history.Count;
        Execute(text);
    }

    /// <summary>Toolbar buttons and menus run the same text commands as the command line.</summary>
    [RelayCommand]
    private void RunText(string text) => Execute(text);

    [RelayCommand]
    private void Cancel()
    {
        Editor.Tool?.Cancel();
        if (Editor.Tool is null)
            Status = "";
    }

    [RelayCommand]
    private void HistoryPrevious()
    {
        if (_historyIndex > 0)
            CommandText = _history[--_historyIndex];
    }

    [RelayCommand]
    private void HistoryNext()
    {
        if (_historyIndex < _history.Count - 1)
        {
            CommandText = _history[++_historyIndex];
        }
        else
        {
            _historyIndex = _history.Count;
            CommandText = "";
        }
    }

    public void UpdateCursor(Point p)
    {
        _cursor = p;
        UpdateCoordinates();
    }

    private void Execute(string text)
    {
        try
        {
            foreach (var command in CommandParser.Parse(text))
                Status = CommandExecutor.Execute(Editor, command);
        }
        catch (Exception ex) when (ex is FormatException or CommandException)
        {
            Status = "Error: " + ex.Message;
        }
    }

    private void OnEditorChanged()
    {
        if (Editor.Tool is { } tool)
            Status = tool.Prompt;
        SheetIndex = Editor.SheetIndex;
        UpdateCoordinates();
    }

    /// <summary>Updates everything that depends on which schematic is loaded.</summary>
    private void RefreshSchematic()
    {
        var schematic = Editor.Schematic;

        // Layers below 90 belong to boards; a schematic only draws on the upper ones.
        Layers = schematic.Layers.Where(l => l.Number >= 90).ToList();
        ActiveLayer = Layers.FirstOrDefault(l => l.Number == Sheet.NetLayer) ?? Layers.FirstOrDefault();

        var count = schematic.Sheets.Count;
        SheetNames = Enumerable.Range(1, count).Select(i => $"{i}/{count}").ToList();
        SheetIndex = Editor.SheetIndex;

        Title = Editor.FilePath is { } path ? $"FalconCAD - {Path.GetFileName(path)}" : "FalconCAD - untitled";
        UpdateCoordinates();
    }

    // Same idea as Eagle's coordinate box: grid size, then the cursor in grid units.
    private void UpdateCoordinates()
    {
        var grid = Editor.Grid;
        var factor = grid.Unit.ToMm();
        Coordinates = $"{Format(grid.Size)} {grid.Unit.Name()} ({Format(_cursor.X / factor)} {Format(_cursor.Y / factor)})";
    }

    private static string Format(double value) => (Math.Round(value, 3) + 0.0).ToString("0.0##", CultureInfo.InvariantCulture);
}
