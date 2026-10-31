using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FalconCAD.Commands;

namespace FalconCAD.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _commandText = "";

    public ObservableCollection<string> Log { get; } = new();

    [RelayCommand]
    private void RunCommand()
    {
        var text = CommandText;
        CommandText = "";
        if (string.IsNullOrWhiteSpace(text))
            return;

        Log.Add("> " + text);
        try
        {
            foreach (var command in CommandParser.Parse(text))
                Log.Add($"  {command.Name} ({command.Arguments.Count} argument(s))");
        }
        catch (FormatException ex)
        {
            Log.Add("  Error: " + ex.Message);
        }
    }
}
