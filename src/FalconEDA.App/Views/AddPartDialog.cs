using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using FalconEDA.App.Controls;
using FalconEDA.App.Services;
using FalconEDA.Core;
using FalconEDA.IO;

namespace FalconEDA.App.Views;

/// <summary>
/// The library browser behind ADD: search all devices by name, library, package or description,
/// see the symbol, and choose one. Returns the ADD argument, e.g. R-EU_R0402@rcl, or null.
/// </summary>
public sealed class AddPartDialog : Window
{
    private const int MaxResults = 1000;

    private readonly LibraryCatalog _catalog;
    private readonly TextBox _search = new() { PlaceholderText = "Search: name, library, package or description, e.g. R-EU 0402" };
    private readonly ListBox _results = new();
    private readonly TextBlock _count = new() { Margin = new Thickness(0, 4) };
    private readonly SymbolPreview _preview = new() { Height = 220 };
    private readonly TextBlock _details = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly DispatcherTimer _searchDelay = new() { Interval = TimeSpan.FromMilliseconds(200) };

    public AddPartDialog(LibraryCatalog catalog, string? filter)
    {
        _catalog = catalog;
        Title = "ADD";
        Width = 900;
        Height = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _results.ItemTemplate = new FuncDataTemplate<DeviceEntry>((entry, _) =>
            new TextBlock { Text = entry is null ? "" : $"{entry.FullName}    {entry.Library}" });
        _results.SelectionChanged += (_, _) => ShowSelected();
        _results.DoubleTapped += (_, _) => Choose();

        _searchDelay.Tick += (_, _) =>
        {
            _searchDelay.Stop();
            Search();
        };
        _search.TextChanged += (_, _) =>
        {
            _searchDelay.Stop();
            _searchDelay.Start();
        };
        _search.Text = filter ?? "";

        var ok = new Button { Content = "OK", MinWidth = 90, IsDefault = true };
        ok.Click += (_, _) => Choose();
        var cancel = new Button { Content = "Cancel", MinWidth = 90, IsCancel = true };
        cancel.Click += (_, _) => Close(null);

        var left = new DockPanel();
        DockPanel.SetDock(_search, Dock.Top);
        DockPanel.SetDock(_count, Dock.Bottom);
        left.Children.Add(_search);
        left.Children.Add(_count);
        left.Children.Add(_results);

        var right = new StackPanel { Spacing = 8, Margin = new Thickness(12, 0, 0, 0) };
        right.Children.Add(_preview);
        right.Children.Add(new ScrollViewer { Content = _details, MaxHeight = 260 });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), Margin = new Thickness(12) };
        grid.Children.Add(left);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);

        var root = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        buttons.Margin = new Thickness(12);
        root.Children.Add(buttons);
        root.Children.Add(grid);
        Content = root;

        // Up and Down in the search box move through the results.
        _search.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key is Key.Down or Key.Up && _results.ItemCount > 0)
            {
                var index = Math.Clamp(_results.SelectedIndex + (e.Key == Key.Down ? 1 : -1), 0, _results.ItemCount - 1);
                _results.SelectedIndex = index;
                _results.ScrollIntoView(index);
                e.Handled = true;
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        Opened += (_, _) => _search.Focus();
        if (!catalog.IsReady)
            catalog.Ready += () => Dispatcher.UIThread.Post(Search);
        Search();
    }

    private void Search()
    {
        if (!_catalog.IsReady)
        {
            _count.Text = "Reading the library folders...";
            return;
        }

        var text = _search.Text ?? "";
        List<DeviceEntry> found = text.Trim().Length == 0
            ? _catalog.Index.Entries.Take(MaxResults).ToList()
            : _catalog.Index.Search(text).Take(MaxResults + 1).ToList();
        var more = found.Count > MaxResults;
        _results.ItemsSource = found.Take(MaxResults).ToList();
        _count.Text = more
            ? $"More than {MaxResults} devices; type more to narrow the search."
            : $"{found.Count} device(s) in {_catalog.Index.Paths.Count} libraries";
        if (found.Count > 0)
            _results.SelectedIndex = 0;
    }

    private void ShowSelected()
    {
        if (_results.SelectedItem is not DeviceEntry entry)
        {
            _preview.Wires = Array.Empty<Wire>();
            _details.Text = "";
            return;
        }

        var library = _catalog.Load(entry.Library);
        var symbol = library is not null && library.DeviceSets.TryGetValue(entry.DeviceSet, out var deviceSet)
            ? deviceSet.Gates.Values.Select(g => library.Symbols.GetValueOrDefault(g.Symbol)).FirstOrDefault(s => s is not null)
            : null;
        _preview.Wires = symbol is null ? Array.Empty<Wire>() : Symbols.Outline(symbol, Transform.Identity).ToList();

        var gates = entry.Gates > 1 ? $"\nGates: {entry.Gates}" : "";
        _details.Text = $"{entry.FullName}\nLibrary: {entry.Library}\nPackage: {entry.Package}{gates}\nPrefix: {entry.Prefix}\n\n{entry.Description}\n\n{entry.LibraryPath}";
    }

    private void Choose()
    {
        if (_results.SelectedItem is DeviceEntry entry)
            Close(entry.AddName);
    }
}
