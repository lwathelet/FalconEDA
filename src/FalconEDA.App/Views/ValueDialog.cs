using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using FalconEDA.Core;

namespace FalconEDA.App.Views;

/// <summary>What was chosen in the value table.</summary>
public sealed record ValueChoice(ValueOption? Picked, string? NewVal, string? NewMnf, string? NewRef, string? FreeValue, bool Dnp, bool InBom);

/// <summary>
/// The table of a component's values (VAL, MNF, REF) to pick from, a row to add a new value to the list,
/// and the part's DNP and BOM properties. Without a list of values, a value can simply be typed in.
/// </summary>
public sealed class ValueDialog : Window
{
    private readonly IReadOnlyList<ValueOption> _values;
    private readonly TextBox _search = new() { PlaceholderText = "Search value, manufacturer or reference" };
    private readonly ListBox _list = new();
    private readonly TextBox _newVal = new() { PlaceholderText = "VAL, e.g. 47K" };
    private readonly TextBox _newMnf = new() { PlaceholderText = "MNF (manufacturer and part number)" };
    private readonly TextBox _newRef = new() { PlaceholderText = "REF" };
    private readonly CheckBox _dnp = new() { Content = "DNP (do not populate)" };
    private readonly CheckBox _inBom = new() { Content = "In the bill of materials (BOM)" };

    /// <param name="showFlags">False when adding a part: the flags belong to a part that does not exist yet.</param>
    public ValueDialog(string title, IReadOnlyList<ValueOption> values, string? currentTechnology, string? currentValue,
        bool dnp, bool inBom, bool showFlags)
    {
        _values = values;
        Title = title;
        Width = 760;
        Height = values.Count > 0 ? 560 : 260;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _dnp.IsChecked = dnp;
        _inBom.IsChecked = inBom;

        _list.ItemTemplate = new FuncDataTemplate<ValueOption>((v, _) => Row(v?.Val ?? "", v?.Mnf ?? "", v?.Ref ?? "", FontWeight.Normal));
        _list.DoubleTapped += (_, _) => Choose();
        _search.TextChanged += (_, _) => Filter();
        Filter();
        if (values.FirstOrDefault(v => v.Technology == currentTechnology) is { } current)
        {
            _list.SelectedItem = current;
            _list.ScrollIntoView(current);
        }
        if (values.Count == 0)
            _newVal.Text = currentValue;

        // Up and Down in the search box move through the list.
        _search.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key is Key.Down or Key.Up && _list.ItemCount > 0)
            {
                _list.SelectedIndex = Math.Clamp(_list.SelectedIndex + (e.Key == Key.Down ? 1 : -1), 0, _list.ItemCount - 1);
                _list.ScrollIntoView(_list.SelectedIndex);
                e.Handled = true;
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        var ok = new Button { Content = "OK", MinWidth = 90, IsDefault = true };
        ok.Click += (_, _) => Choose();
        var cancel = new Button { Content = "Cancel", MinWidth = 90, IsCancel = true };
        cancel.Click += (_, _) => Close(null);

        var root = new DockPanel { Margin = new Thickness(12) };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(0, 10, 0, 0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        if (showFlags)
        {
            var flags = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20, Margin = new Thickness(0, 10, 0, 0) };
            flags.Children.Add(_dnp);
            flags.Children.Add(_inBom);
            DockPanel.SetDock(flags, Dock.Bottom);
            root.Children.Add(flags);
        }

        // A new value: added to the component's list when there is one, otherwise just the part's value.
        var newRow = new Grid { ColumnDefinitions = new ColumnDefinitions("120,*,120"), Margin = new Thickness(0, 10, 0, 0) };
        newRow.Children.Add(_newVal);
        if (values.Count > 0)
        {
            Grid.SetColumn(_newMnf, 1);
            Grid.SetColumn(_newRef, 2);
            _newMnf.Margin = _newRef.Margin = new Thickness(6, 0, 0, 0);
            newRow.Children.Add(_newMnf);
            newRow.Children.Add(_newRef);
        }
        else
        {
            Grid.SetColumnSpan(_newVal, 3);
        }
        var newLabel = new TextBlock
        {
            Text = values.Count > 0 ? "Or a new value, added to the list in the library:" : "This component has no list of values. Value:",
            Margin = new Thickness(0, 10, 0, 0),
        };
        var newPanel = new StackPanel();
        newPanel.Children.Add(newLabel);
        newPanel.Children.Add(newRow);
        DockPanel.SetDock(newPanel, Dock.Bottom);
        root.Children.Add(newPanel);

        if (values.Count > 0)
        {
            DockPanel.SetDock(_search, Dock.Top);
            root.Children.Add(_search);
            var header = Row("VAL", "MNF", "REF", FontWeight.Bold);
            header.Margin = new Thickness(12, 8, 12, 4);
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);
            root.Children.Add(_list);
        }
        Content = root;

        Opened += (_, _) => (values.Count > 0 ? _search : _newVal).Focus();
    }

    private static Grid Row(string val, string mnf, string reference, FontWeight weight)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("120,*,120") };
        var cells = new[] { val, mnf, reference };
        for (var i = 0; i < 3; i++)
        {
            var cell = new TextBlock { Text = cells[i], FontWeight = weight, TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(cell, i);
            row.Children.Add(cell);
        }
        return row;
    }

    private void Filter()
    {
        var words = (_search.Text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        _list.ItemsSource = _values.Where(v => words.All(w =>
            v.Val.Contains(w, StringComparison.OrdinalIgnoreCase)
            || v.Mnf.Contains(w, StringComparison.OrdinalIgnoreCase)
            || v.Ref.Contains(w, StringComparison.OrdinalIgnoreCase)))
            // "10K" should find 10K before 10K7: exact values first, then values starting with the text.
            .OrderBy(v => v.Val.Equals(_search.Text?.Trim(), StringComparison.OrdinalIgnoreCase) ? 0
                : v.Val.StartsWith(_search.Text?.Trim() ?? "", StringComparison.OrdinalIgnoreCase) ? 1 : 2)
            .ToList();

        // After typing, Enter takes the first match.
        if (words.Length > 0 && _list.ItemCount > 0)
            _list.SelectedIndex = 0;
    }

    private void Choose()
    {
        var newVal = _newVal.Text?.Trim();
        var dnp = _dnp.IsChecked == true;
        var inBom = _inBom.IsChecked == true;

        if (_values.Count == 0)
        {
            Close(new ValueChoice(null, null, null, null, string.IsNullOrEmpty(newVal) ? null : newVal, dnp, inBom));
            return;
        }
        if (!string.IsNullOrEmpty(newVal))
        {
            Close(new ValueChoice(null, newVal, _newMnf.Text?.Trim() ?? "", _newRef.Text?.Trim() ?? "", null, dnp, inBom));
            return;
        }
        Close(new ValueChoice(_list.SelectedItem as ValueOption, null, null, null, null, dnp, inBom));
    }
}
