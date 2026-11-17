using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace FalconEDA.App.Views;

/// <summary>The text typed, and which of the choices was picked (0 when there were none).</summary>
public sealed record InputResult(string Text, int Choice);

/// <summary>
/// Asks for one line of text, e.g. a new name for NAME, optionally with a few choices to pick one from
/// (what to rename). Returns null when cancelled.
/// </summary>
public sealed class InputDialog : Window
{
    public InputDialog(string title, string prompt, string text, IReadOnlyList<string>? choices = null)
    {
        Title = title;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var input = new TextBox { Text = text };
        var options = (choices ?? []).Select((c, i) => new RadioButton { Content = c, GroupName = "choice", IsChecked = i == 0 }).ToList();

        var ok = new Button { Content = "OK", MinWidth = 90, IsDefault = true };
        ok.Click += (_, _) => Close(new InputResult(input.Text?.Trim() ?? "", options.FindIndex(o => o.IsChecked == true) is var i and >= 0 ? i : 0));
        var cancel = new Button { Content = "Cancel", MinWidth = 90, IsCancel = true };
        cancel.Click += (_, _) => Close(null);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var root = new StackPanel { Margin = new Thickness(16), Spacing = 10 };
        root.Children.Add(new TextBlock { Text = prompt });
        root.Children.Add(input);
        if (options.Count > 0)
        {
            var group = new StackPanel { Spacing = 4, Margin = new Thickness(0, 4, 0, 0) };
            group.Children.Add(new TextBlock { Text = "Rename:" });
            foreach (var option in options)
                group.Children.Add(option);
            root.Children.Add(group);
        }
        root.Children.Add(buttons);
        Content = root;

        Opened += (_, _) =>
        {
            input.Focus();
            input.SelectAll();
        };
    }
}
