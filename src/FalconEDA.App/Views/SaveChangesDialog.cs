using Avalonia.Controls;
using Avalonia.Layout;

namespace FalconEDA.App.Views;

public enum SaveChoice
{
    Cancel,
    Save,
    Discard,
}

/// <summary>Asks whether to save unsaved changes. Closing the dialog counts as Cancel.</summary>
public sealed class SaveChangesDialog : Window
{
    public SaveChangesDialog(string fileName)
    {
        Title = "FalconEDA";
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        Button Choice(string text, SaveChoice choice, bool isDefault = false, bool isCancel = false)
        {
            var button = new Button { Content = text, MinWidth = 90, IsDefault = isDefault, IsCancel = isCancel };
            button.Click += (_, _) => Close(choice);
            return button;
        }

        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = $"Save changes to {fileName}?" },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children =
                    {
                        Choice("Save", SaveChoice.Save, isDefault: true),
                        Choice("Don't save", SaveChoice.Discard),
                        Choice("Cancel", SaveChoice.Cancel, isCancel: true),
                    },
                },
            },
        };
    }
}
