using System.Globalization;
using FalconEDA.Core;

namespace FalconEDA.Commands;

/// <summary>NAME, LABEL, JUNCTION, SMASH and SHOW.</summary>
public static partial class CommandExecutor
{
    /// <summary>
    /// NAME R7 R70 renames a part, NAME N$12 VBAT a net (on every sheet; a name already in use joins the two nets).
    /// NAME alone: click a part or net, then type the new name.
    /// </summary>
    private static string Name(Editor editor, Command command)
    {
        switch (command.Arguments)
        {
            case []:
                return StartTool(editor, new ClickTool(editor, "NAME: left-click a part or net to rename it. Esc ends.", false, (p, radius) =>
                {
                    if (ObjectNameAt(editor, p, radius) is { } name)
                        editor.RequestRename(name);
                }));
            case [WordArgument or TextArgument, WordArgument or TextArgument]:
                return Rename(editor, ArgumentText(command.Arguments[0]), ArgumentText(command.Arguments[1]));
            default:
                throw new CommandException("NAME: use NAME, or NAME old new (for a part or a net).");
        }
    }

    /// <summary>Renames a part, or else a net; public for the rename dialog.</summary>
    public static string Rename(Editor editor, string oldName, string newName)
    {
        if (newName.Length == 0 || newName.Any(char.IsWhiteSpace))
            throw new CommandException("NAME: a name cannot be empty or contain spaces.");

        if (editor.Schematic.Parts.Keys.FirstOrDefault(k => k.Equals(oldName, StringComparison.OrdinalIgnoreCase)) is { } part)
        {
            if (editor.Schematic.Parts.ContainsKey(newName))
                throw new CommandException($"NAME: there is already a part called {newName}.");
            editor.Checkpoint(allSheets: true);
            editor.Schematic.RenamePart(part, newName);
            editor.NotifyChanged();
            return $"Part {part} is now {newName}";
        }

        if (editor.Schematic.HasNet(oldName))
        {
            var joins = editor.Schematic.HasNet(newName);
            editor.Checkpoint(allSheets: true);
            editor.Schematic.RenameNet(oldName, newName);
            editor.NotifyChanged();
            return joins ? $"Net {oldName} joined to {newName}" : $"Net {oldName} is now {newName}";
        }

        throw new CommandException($"NAME: there is no part or net {oldName}.");
    }

    /// <summary>The part or net a click means: a smashed name or label first, then a wire, then a part.</summary>
    private static string? ObjectNameAt(Editor editor, Point p, double radius)
    {
        var sheet = editor.Sheet;
        switch (sheet.TextAt(p, radius))
        {
            case Label label:
                return sheet.Nets.Concat(sheet.Busses).FirstOrDefault(n => n.Labels.Contains(label))?.Name;
            case PlacedAttribute attribute:
                return sheet.Instances.FirstOrDefault(i => i.Attributes.Contains(attribute))?.Part;
        }
        return sheet.NetAt(p, radius)?.Name ?? sheet.InstanceAt(p, radius)?.Part;
    }

    /// <summary>LABEL: click a net, then place its name label; right-click turns it. LABEL (x1 y1) (x2 y2) does both at once.</summary>
    private static string LabelCommand(Editor editor, Command command)
    {
        switch (command.Arguments)
        {
            case []:
                return StartTool(editor, new LabelTool(editor));
            case [CoordinateArgument at, CoordinateArgument place]:
                var net = editor.Sheet.NetAt(ToMm(editor, at.Value), editor.Grid.SizeMm / 2)
                    ?? throw new CommandException("LABEL: there is no net at the first point.");
                editor.Checkpoint();
                editor.Sheet.AddLabel(net, new Label { Position = ToMm(editor, place.Value) });
                editor.NotifyChanged();
                return "";
            default:
                throw new CommandException("LABEL: use LABEL, or LABEL (net point) (label point).");
        }
    }

    /// <summary>JUNCTION: click where wires of a net meet to place a dot; JUNCTION (x y) places one there.</summary>
    private static string JunctionCommand(Editor editor, Command command)
    {
        switch (command.Arguments)
        {
            case []:
                return StartTool(editor, new ClickTool(editor, "JUNCTION: left-click a point on a net. Esc ends.", true, (p, _) => PlaceJunction(editor, p)));
            case [CoordinateArgument at]:
                if (!PlaceJunction(editor, ToMm(editor, at.Value)))
                    throw new CommandException("JUNCTION: there is no net at that point, or already a junction.");
                return "";
            default:
                throw new CommandException("JUNCTION: use JUNCTION or JUNCTION (x y).");
        }
    }

    private static bool PlaceJunction(Editor editor, Point p)
    {
        if (!editor.Sheet.Nets.Any(n => n.Touches(p)))
            return false;
        editor.Checkpoint();
        var placed = editor.Sheet.AddJunction(p);
        if (!placed)
            editor.RevertCheckpoint();
        editor.NotifyChanged();
        return placed;
    }

    /// <summary>
    /// SMASH R1 gives R1 its own name and value texts to move, or puts them back on the symbol; with a group,
    /// every part in it. SMASH alone: click the parts.
    /// </summary>
    private static string Smash(Editor editor, Command command)
    {
        List<Instance> instances;
        if (command.Arguments.Count > 0)
        {
            instances = command.Arguments.Select(ArgumentText)
                .SelectMany(n => PartSelection(editor, n).Instances).ToList();
        }
        else if (editor.Selection.Instances.Count > 0)
        {
            instances = editor.Selection.Instances.ToList();
        }
        else
        {
            return StartTool(editor, new ClickTool(editor, "SMASH: left-click a part to smash or unsmash it. Esc ends.", false, (p, radius) =>
            {
                if (editor.Sheet.InstanceAt(p, radius) is { } instance)
                {
                    editor.Checkpoint();
                    editor.Sheet.Smash(instance, !instance.Smashed);
                    editor.NotifyChanged();
                }
            }));
        }

        editor.Checkpoint();
        foreach (var instance in instances)
            editor.Sheet.Smash(instance, !instance.Smashed);
        editor.NotifyChanged();
        return $"{instances.Count(i => i.Smashed)} smashed, {instances.Count(i => !i.Smashed)} unsmashed";
    }

    /// <summary>
    /// SHOW GND points out a net (its wires and the pins on it), SHOW R1 a part, until the next command.
    /// SHOW alone: click a net or part.
    /// </summary>
    private static string Show(Editor editor, Command command)
    {
        if (command.Arguments.Count == 0)
        {
            return StartTool(editor, new ClickTool(editor, "SHOW: left-click a net or part. Esc ends.", false, (p, radius) =>
            {
                if (ObjectNameAt(editor, p, radius) is { } name)
                    editor.Message = ShowNames(editor, [name]);
            }));
        }
        return ShowNames(editor, command.Arguments.Select(ArgumentText).ToList());
    }

    private static string ShowNames(Editor editor, IReadOnlyList<string> names)
    {
        editor.Highlight.Clear();
        var messages = new List<string>();
        foreach (var name in names)
        {
            var instances = editor.Sheet.Instances.Where(i => i.Part.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (instances.Count > 0 && editor.Schematic.Parts.TryGetValue(instances[0].Part, out var part))
            {
                editor.Highlight.Instances.UnionWith(instances);
                messages.Add($"{part.Name}: {part.DeviceSet}{part.Device}, value {editor.Schematic.ValueOf(part)}");
                continue;
            }

            var net = editor.Sheet.Nets.Concat(editor.Sheet.Busses).FirstOrDefault(n => n.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (net is not null)
            {
                foreach (var wire in net.Wires)
                    editor.Highlight.AddWhole(wire);
                editor.Highlight.Labels.UnionWith(net.Labels);
                var pinned = net.PinRefs.Select(p => p.Part).ToHashSet();
                editor.Highlight.Instances.UnionWith(editor.Sheet.Instances.Where(i => pinned.Contains(i.Part)));

                // Where else the net goes: the same name on other sheets is the same signal.
                var sheets = editor.Schematic.Sheets.Select((s, i) => (s, i)).Where(x => x.s.Nets.Any(n => n.Name == net.Name)).Select(x => x.i + 1);
                var pins = editor.Schematic.Sheets.SelectMany(s => s.Nets.Where(n => n.Name == net.Name)).SelectMany(n => n.PinRefs)
                    .Select(p => $"{p.Part}.{p.Pin.Split('@')[0]}").Distinct().ToList();
                messages.Add($"{net.Name}: {pins.Count} pin(s) {string.Join(", ", pins.Take(12))}{(pins.Count > 12 ? ", ..." : "")}"
                    + $" (sheet {string.Join(", ", sheets)})");
                continue;
            }

            var elsewhere = editor.Schematic.Sheets.Select((s, i) => (s, i)).Where(x => x.s.Instances.Any(i => i.Part == name) || x.s.Nets.Any(n => n.Name == name)).Select(x => x.i + 1).ToList();
            messages.Add(elsewhere.Count > 0 ? $"{name} is on sheet {string.Join(", ", elsewhere)}" : $"There is no part or net {name}.");
        }
        editor.NotifyChanged();
        return string.Join("; ", messages);
    }
}

/// <summary>A tool that does something with each click: NAME, JUNCTION, SMASH, SHOW.</summary>
public sealed class ClickTool(Editor editor, string prompt, bool snap, Action<Point, double> onClick) : ITool
{
    public string Prompt => prompt;
    public bool SnapToGrid => snap;
    public IReadOnlyList<Wire> Preview => Array.Empty<Wire>();

    public void Click(Point p, double pickRadius, int clickCount) => onClick(p, pickRadius);

    public void Move(Point p) { }

    public void Cancel()
    {
        editor.ClearHighlight();
        editor.SetTool(null);
    }
}

/// <summary>LABEL: the first click picks the net, then its label follows the mouse until the second click places it.</summary>
public sealed class LabelTool(Editor editor) : ITool
{
    private const double Size = 1.778;

    private Net? _net;
    private Point _cursor;
    private Rotation _rotation;

    public string Prompt => _net is null
        ? "LABEL: left-click a net. Esc ends."
        : $"LABEL {_net.Name}: left-click to place, right-click to rotate. Esc cancels.";

    public bool SnapToGrid => true;

    /// <summary>An outline of where the label's text will be.</summary>
    public IReadOnlyList<Wire> Preview
    {
        get
        {
            if (_net is null)
                return Array.Empty<Wire>();
            var width = _net.Name.Length * Size * 0.8;
            var transform = new Transform(_cursor, _rotation);
            var corners = new[] { new Point(0, 0), new Point(width, 0), new Point(width, Size), new Point(0, Size) }.Select(transform.Apply).ToList();
            return corners.Select((c, i) => new Wire { From = c, To = corners[(i + 1) % 4], Width = 0.1, Layer = 95 }).ToList();
        }
    }

    public void Click(Point p, double pickRadius, int clickCount)
    {
        if (_net is null)
        {
            _net = editor.Sheet.NetAt(p, Math.Max(pickRadius, editor.Grid.SizeMm / 2));
            _cursor = p;
            editor.NotifyChanged();
            return;
        }

        editor.Checkpoint();
        editor.Sheet.AddLabel(_net, new Label { Position = p, Size = Size, Rotation = _rotation });
        _net = null;
        editor.NotifyChanged();
    }

    public void Move(Point p) => _cursor = p;

    public void RightClick(Point p)
    {
        _rotation = _rotation.Rotated90();
        editor.NotifyChanged();
    }

    public void Cancel()
    {
        if (_net is not null)
        {
            _net = null;
            editor.NotifyChanged();
        }
        else
        {
            editor.SetTool(null);
        }
    }
}
