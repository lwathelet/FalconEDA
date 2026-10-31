using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FalconCAD.App.ViewModels;

namespace FalconCAD.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        EditorView.CursorMoved += p => ViewModel?.UpdateCursor(p);

        // As in Eagle, the keyboard always goes to the command line, even after clicking in the drawing.
        Opened += (_, _) => CommandBox.Focus();
        EditorView.PointerReleased += (_, _) => Dispatcher.UIThread.Post(() => CommandBox.Focus());

        // Tunnel so these win over the command line's own text undo.
        AddHandler(KeyDownEvent, OnShortcut, RoutingStrategies.Tunnel);
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void OnShortcut(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.Control || ViewModel is not { } vm)
            return;

        switch (e.Key)
        {
            case Key.Z:
                vm.RunTextCommand.Execute("UNDO");
                e.Handled = true;
                break;
            case Key.Y:
                vm.RunTextCommand.Execute("REDO");
                e.Handled = true;
                break;
            case Key.O:
                OnOpen(this, new RoutedEventArgs());
                e.Handled = true;
                break;
        }
    }

    private async void OnOpen(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open schematic",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Eagle schematic") { Patterns = new[] { "*.sch" } },
                FilePickerFileTypes.All,
            },
        });

        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path)
            ViewModel?.OpenFile(path);
        CommandBox.Focus();
    }

    private void OnExit(object? sender, RoutedEventArgs e) => Close();
}
