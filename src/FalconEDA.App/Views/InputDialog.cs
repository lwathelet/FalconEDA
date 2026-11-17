using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace FalconEDA.App.Views;

/// <summary>Asks for one line of text, e.g. a new name for NAME. Returns the text, or null when cancelled.</summary>
public sealed class InputDialog : Window
{
    public InputDialog(string title, string prompt, string text)
    {
        Title = title;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var input = new TextBox { Text = text };
        var ok = new Button { Content = "OK", MinWidth = 90, IsDefault = true };
        ok.Click += (_, _) => Close(input.Text?.Trim());
        var cancel = new Button { Content = "Cancel", MinWidth = 90, IsCancel = true };
        cancel.Click += (_, _) => Close(null);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var root = new StackPanel { Margin = new Thickness(16), Spacing = 10 };
        root.Children.Add(new TextBlock { Text = prompt });
        root.Children.Add(input);
        root.Children.Add(buttons);
        Content = root;

        Opened += (_, _) =>
        {
            input.Focus();
            input.SelectAll();
        };
    }
}
