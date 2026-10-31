using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FalconCAD.Commands;
using FalconCAD.Core;

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
    private Layer? _activeLayer;

    public MainViewModel()
    {
        ActiveLayer = Layers.First(l => l.Number == Editor.ActiveLayer);
        Editor.Changed += OnEditorChanged;
        UpdateCoordinates();
    }

    public Editor Editor { get; } = new();

    public IReadOnlyList<Layer> Layers => Editor.Drawing.Layers;

    partial void OnActiveLayerChanged(Layer? value)
    {
        if (value is not null)
            Editor.ActiveLayer = value.Number;
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
