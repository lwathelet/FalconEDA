using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace FalconEDA.App.Views;

/// <summary>Edits the library folders, one per line. Returns the new list, or null when cancelled.</summary>
public sealed class LibraryFoldersDialog : Window
{
    public LibraryFoldersDialog(IEnumerable<string> folders)
    {
        Title = "Library folders";
        Width = 640;
        Height = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var text = new TextBox
        {
            AcceptsReturn = true,
            Text = string.Join(Environment.NewLine, folders),
            FontFamily = new Avalonia.Media.FontFamily("Consolas"),
        };
        var ok = new Button { Content = "OK", MinWidth = 90, IsDefault = true };
        ok.Click += (_, _) => Close((text.Text ?? "")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList());
        var cancel = new Button { Content = "Cancel", MinWidth = 90, IsCancel = true };
        cancel.Click += (_, _) => Close(null);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var hint = new TextBlock { Text = "Folders with Eagle library files (.lbr), one per line. Only local folders are used." };
        var root = new DockPanel { Margin = new Thickness(12) };
        DockPanel.SetDock(hint, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        hint.Margin = new Thickness(0, 0, 0, 8);
        buttons.Margin = new Thickness(0, 8, 0, 0);
        root.Children.Add(hint);
        root.Children.Add(buttons);
        root.Children.Add(text);
        Content = root;
    }
}
