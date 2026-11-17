using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FalconEDA.App.ViewModels;

namespace FalconEDA.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        EditorView.CursorMoved += p => ViewModel?.UpdateCursor(p);

        // As in Eagle, the keyboard always goes to the command line, even after clicking in the drawing.
        Opened += (_, _) => CommandBox.Focus();
        Activated += (_, _) => CommandBox.Focus();
        EditorView.PointerReleased += (_, _) => Dispatcher.UIThread.Post(() => CommandBox.Focus());

        // Tunnel so these win over the command line's own text undo.
        AddHandler(KeyDownEvent, OnShortcut, RoutingStrategies.Tunnel);

        DataContextChanged += (_, _) =>
        {
            if (ViewModel is { } vm)
                vm.SaveAsRequested += async () => await SaveAsAsync();
        };

        Closing += OnClosing;
    }

    private bool _closeConfirmed;

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void OnShortcut(object? sender, KeyEventArgs e)
    {
        if (ViewModel is not { } vm)
            return;

        // Esc leaves the menu bar (entered by tapping Alt) or cancels the command, and the keyboard goes back to the command line.
        if (e.Key == Key.Escape)
        {
            var inMenu = MainMenu.IsOpen || (FocusManager?.GetFocusedElement() is Visual focused && MainMenu.IsVisualAncestorOf(focused));
            if (inMenu)
                MainMenu.Close();
            else
                vm.CancelCommand.Execute(null);
            CommandBox.Focus();
            e.Handled = true;
            return;
        }

        // Del deletes the group, unless there is text on the command line to edit.
        if (e.Key == Key.Delete && e.KeyModifiers == KeyModifiers.None
            && string.IsNullOrEmpty(CommandBox.Text) && !vm.Editor.Selection.IsEmpty)
        {
            vm.DeleteSelectionCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift) && e.Key == Key.S)
        {
            OnSaveAs(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }
        if (e.KeyModifiers != KeyModifiers.Control)
            return;

        // Ctrl+C, X and V act on the drawing, except while working with text on the command line.
        var editingText = !string.IsNullOrEmpty(CommandBox.Text);
        switch (e.Key)
        {
            case Key.C or Key.X when !editingText && !vm.Editor.Selection.IsEmpty:
                vm.RunTextCommand.Execute(e.Key == Key.C ? "COPY" : "CUT");
                e.Handled = true;
                return;
            case Key.V when !editingText && vm.Editor.Clipboard is { IsEmpty: false }:
                vm.RunTextCommand.Execute("PASTE");
                e.Handled = true;
                return;
        }

        switch (e.Key)
        {
            case Key.S:
                vm.RunTextCommand.Execute("WRITE");
                e.Handled = true;
                break;
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
        if (!await ConfirmDiscardAsync())
            return;

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

    private async void OnSaveAs(object? sender, RoutedEventArgs e)
    {
        await SaveAsAsync();
        CommandBox.Focus();
    }

    /// <summary>Asks for a file name and saves; false if the user cancelled or saving failed.</summary>
    private async Task<bool> SaveAsAsync()
    {
        var current = ViewModel?.Editor.FilePath;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save schematic as",
            SuggestedFileName = current is null ? "untitled.sch" : System.IO.Path.GetFileName(current),
            DefaultExtension = "sch",
            FileTypeChoices = new[] { new FilePickerFileType("Eagle schematic") { Patterns = new[] { "*.sch" } } },
        });

        return file?.TryGetLocalPath() is { } path && ViewModel?.SaveFile(path) == true;
    }

    /// <summary>
    /// Before the drawing is closed or replaced: asks whether to save unsaved changes.
    /// Returns false if the user cancels, or chose Save and saving did not happen.
    /// </summary>
    private async Task<bool> ConfirmDiscardAsync()
    {
        if (ViewModel is not { Editor.IsModified: true } vm)
            return true;

        var name = vm.Editor.FilePath is { } path ? System.IO.Path.GetFileName(path) : "untitled";
        var choice = await new SaveChangesDialog(name).ShowDialog<SaveChoice>(this);
        var proceed = choice switch
        {
            SaveChoice.Discard => true,
            SaveChoice.Save when vm.Editor.FilePath is { } current => vm.SaveFile(current),
            SaveChoice.Save => await SaveAsAsync(),
            _ => false,
        };
        CommandBox.Focus();
        return proceed;
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeConfirmed || ViewModel?.Editor.IsModified != true)
            return;

        // Closing cannot wait for the dialog, so stop it and close again once the user has answered.
        e.Cancel = true;
        if (await ConfirmDiscardAsync())
        {
            _closeConfirmed = true;
            Close();
        }
    }

    private void OnExit(object? sender, RoutedEventArgs e) => Close();
}
