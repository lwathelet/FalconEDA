using FalconCAD.Core;

namespace FalconCAD.Commands;

public sealed class CommandException(string message) : Exception(message);

/// <summary>Runs parsed commands against an <see cref="Editor"/>.</summary>
public static class CommandExecutor
{
    private static readonly Dictionary<string, Func<Editor, Command, string>> Handlers = new()
    {
        ["DELETE"] = Delete,
        ["EDIT"] = Edit,
        ["GRID"] = Grid,
        ["NET"] = Net,
        ["REDO"] = Redo,
        ["UNDO"] = Undo,
        ["WINDOW"] = Window,
        ["WIRE"] = Wire,
    };

    /// <summary>Runs one command and returns the message for the status bar.</summary>
    public static string Execute(Editor editor, Command command) => Handlers[Resolve(command.Name)](editor, command);

    // Like Eagle, a command can be shortened to any unique prefix: DEL for DELETE.
    private static string Resolve(string name)
    {
        if (Handlers.ContainsKey(name))
            return name;

        var matches = Handlers.Keys.Where(k => k.StartsWith(name, StringComparison.Ordinal)).ToList();
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new CommandException($"Unknown command: {name}"),
            _ => throw new CommandException($"Ambiguous command {name}: {string.Join(", ", matches)}"),
        };
    }

    private static string Grid(Editor editor, Command command)
    {
        var grid = editor.Grid;
        if (command.Arguments.Count == 0)
            grid.Visible = !grid.Visible;

        foreach (var argument in command.Arguments)
        {
            switch (argument)
            {
                case NumberArgument { Value: > 0 } n:
                    grid.Size = n.Value;
                    break;
                case WordArgument w when w.Value.Equals("ON", StringComparison.OrdinalIgnoreCase):
                    grid.Visible = true;
                    break;
                case WordArgument w when w.Value.Equals("OFF", StringComparison.OrdinalIgnoreCase):
                    grid.Visible = false;
                    break;
                case WordArgument w when Enum.TryParse<Unit>(w.Value, ignoreCase: true, out var unit):
                    // Keep the physical grid size; a number after the unit overrides it.
                    grid.Size = grid.SizeMm / unit.ToMm();
                    grid.Unit = unit;
                    break;
                default:
                    throw Unexpected("GRID", argument);
            }
        }

        editor.NotifyChanged();
        return $"Grid {grid.Size:0.####} {grid.Unit.Name()}" + (grid.Visible ? "" : " (hidden)");
    }

    private static string Net(Editor editor, Command command)
    {
        string? name = null;
        var points = new List<Point>();
        foreach (var argument in command.Arguments)
        {
            switch (argument)
            {
                case TextArgument t: name = t.Value; break;
                case WordArgument w: name = w.Value; break;
                case CoordinateArgument c: points.Add(ToMm(editor, c.Value)); break;
                default: throw Unexpected("NET", argument);
            }
        }

        void Commit(IReadOnlyList<Point> p)
        {
            editor.Checkpoint();
            editor.Sheet.AddNet(p, name: name);
        }
        var tool = new PolylineTool(editor, "NET", Sheet.NetLayer, Sheet.DefaultNetWidth, Commit, endOnNet: true);
        return Polyline(editor, points, Commit, tool);
    }

    private static string Wire(Editor editor, Command command)
    {
        var width = Sheet.DefaultNetWidth;
        var points = new List<Point>();
        foreach (var argument in command.Arguments)
        {
            switch (argument)
            {
                case NumberArgument { Value: > 0 } n: width = n.Value * editor.Grid.Unit.ToMm(); break;
                case CoordinateArgument c: points.Add(ToMm(editor, c.Value)); break;
                default: throw Unexpected("WIRE", argument);
            }
        }

        var layer = editor.ActiveLayer;
        void Commit(IReadOnlyList<Point> p)
        {
            editor.Checkpoint();
            editor.Sheet.AddWire(p, width, layer);
        }
        var tool = new PolylineTool(editor, "WIRE", layer, width, Commit, endOnNet: false);
        return Polyline(editor, points, Commit, tool);
    }

    private static string Delete(Editor editor, Command command)
    {
        if (command.Arguments.Count == 0)
        {
            var tool = new DeleteTool(editor);
            editor.SetTool(tool);
            return tool.Prompt;
        }

        var deleted = 0;
        foreach (var argument in command.Arguments)
        {
            if (argument is not CoordinateArgument c)
                throw Unexpected("DELETE", argument);
            if (editor.Sheet.FindNearest(ToMm(editor, c.Value), editor.Grid.SizeMm / 2) is not { } wire)
                continue;
            editor.Checkpoint();
            editor.Sheet.Delete(wire);
            deleted++;
        }

        editor.NotifyChanged();
        return $"Deleted {deleted} object(s)";
    }

    // EDIT .s2 switches to sheet 2, as in Eagle.
    private static string Edit(Editor editor, Command command)
    {
        if (command.Arguments is not [WordArgument { Value: var target }]
            || !target.StartsWith(".s", StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(target[2..], out var number))
            throw new CommandException("EDIT: use EDIT .s2 to go to sheet 2. Opening files is in the File menu.");

        editor.ShowSheet(number - 1);
        return $"Sheet {number}/{editor.Schematic.Sheets.Count}";
    }

    private static string Undo(Editor editor, Command command) => editor.Undo() ? "" : "Nothing to undo";

    private static string Redo(Editor editor, Command command) => editor.Redo() ? "" : "Nothing to redo";

    private static string Window(Editor editor, Command command)
    {
        foreach (var argument in command.Arguments)
        {
            switch (argument)
            {
                case WordArgument w when w.Value.Equals("FIT", StringComparison.OrdinalIgnoreCase):
                    editor.RequestZoom(null);
                    break;
                case NumberArgument { Value: > 0 } n:
                    editor.RequestZoom(n.Value);
                    break;
                default:
                    throw Unexpected("WINDOW", argument);
            }
        }
        return "";
    }

    /// <summary>Adds the polyline now if it is complete, otherwise starts the tool to finish it with the mouse.</summary>
    private static string Polyline(Editor editor, List<Point> points, Action<IReadOnlyList<Point>> commit, PolylineTool tool)
    {
        if (points.Count >= 2)
        {
            commit(points);
            editor.NotifyChanged();
            return "";
        }

        editor.SetTool(tool);
        if (points.Count == 1)
            tool.Click(points[0], 0, 1);
        return tool.Prompt;
    }

    // Coordinates on the command line are in the current grid unit.
    private static Point ToMm(Editor editor, Point p)
    {
        var factor = editor.Grid.Unit.ToMm();
        return new Point(p.X * factor, p.Y * factor);
    }

    private static CommandException Unexpected(string command, Argument argument) => new(
        $"{command}: unexpected argument {argument switch
        {
            WordArgument w => w.Value,
            NumberArgument n => n.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            TextArgument t => $"'{t.Value}'",
            CoordinateArgument c => $"({c.Value.X} {c.Value.Y})",
            _ => argument.ToString(),
        }}");
}
