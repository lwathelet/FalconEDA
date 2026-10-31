using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using FalconCAD.App.ViewModels;

namespace FalconCAD.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        EditorView.CursorMoved += p => (DataContext as MainViewModel)?.UpdateCursor(p);

        // As in Eagle, the keyboard always goes to the command line, even after clicking in the drawing.
        Opened += (_, _) => CommandBox.Focus();
        EditorView.PointerReleased += (_, _) => Dispatcher.UIThread.Post(() => CommandBox.Focus());
    }

    private void OnExit(object? sender, RoutedEventArgs e) => Close();
}
