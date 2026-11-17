using System.Text.RegularExpressions;
using FalconEDA.Core;

namespace FalconEDA.Commands;

/// <summary>Commands that select, move, turn, copy and paste. See docs/design-decisions.md for COPY, CUT and PASTE.</summary>
public static partial class CommandExecutor
{
    // An orientation such as R90, MR180 or SMR270, or =R90 for an absolute one.
    private static readonly Regex OrientationPattern = new(@"^=?S?M?R\d+(\.\d+)?$", RegexOptions.IgnoreCase);

    /// <summary>GROUP: drag a rectangle with the mouse. GROUP ALL selects everything on the sheet.</summary>
    private static string Group(Editor editor, Command command)
    {
        switch (command.Arguments)
        {
            case []:
                editor.SetTool(null);
                return "GROUP: drag a rectangle to select. Ctrl adds or removes. Esc clears the group.";
            case [WordArgument { Value: var all }] when all.Equals("ALL", StringComparison.OrdinalIgnoreCase):
                editor.SetTool(null);
                editor.Selection.Clear();
                editor.Selection.UnionWith(editor.Sheet.SelectAll());
                editor.NotifyChanged();
                return $"Group: {editor.Selection.Count} object(s)";
            default:
                throw new CommandException("GROUP: use GROUP, then drag a rectangle, or GROUP ALL.");
        }
    }

    /// <summary>
    /// MOVE: click an object (or the group) to pick it up, click again to drop it.
    /// MOVE R1 attaches R1 to the mouse; MOVE R1 (x y) moves it there; MOVE (x1 y1) (x2 y2) moves what is at the first point.
    /// </summary>
    private static string Move(Editor editor, Command command)
    {
        switch (command.Arguments)
        {
            case []:
            {
                var tool = new MoveTool(editor);
                editor.SetTool(tool);
                return tool.Prompt;
            }
            case [WordArgument name]:
            {
                var selection = PartSelection(editor, name.Value);
                var tool = new MoveTool(editor, selection, selection.Instances.First().Position);
                editor.SetTool(tool);
                return tool.Prompt;
            }
            case [WordArgument name, CoordinateArgument to]:
            {
                var selection = PartSelection(editor, name.Value);
                editor.Checkpoint();
                editor.Sheet.Move(selection, ToMm(editor, to.Value) - selection.Instances.First().Position);
                break;
            }
            case [CoordinateArgument from, CoordinateArgument to]:
            {
                var start = ToMm(editor, from.Value);
                var selection = editor.Sheet.SelectForMove(start, editor.Grid.SizeMm / 2);
                if (selection.IsEmpty)
                    throw new CommandException("MOVE: nothing at the first point.");
                editor.Checkpoint();
                editor.Sheet.Move(selection, ToMm(editor, to.Value) - start);
                break;
            }
            default:
                throw new CommandException("MOVE: use MOVE, MOVE R1, MOVE R1 (x y) or MOVE (x1 y1) (x2 y2).");
        }

        editor.Sheet.RefreshPins();
        editor.NotifyChanged();
        return "";
    }

    /// <summary>ROTATE: click a part, or the point to turn the group around. ROTATE R1, ROTATE R180 R1 or ROTATE =MR90 R1 by name.</summary>
    private static string Rotate(Editor editor, Command command)
    {
        if (command.Arguments.Count == 0)
            return StartTool(editor, new TurnTool(editor, "ROTATE", (sheet, selection, pivot) => sheet.Rotate90(selection, pivot)));

        var orientation = new Rotation(90);
        var absolute = false;
        var names = new List<string>();
        foreach (var argument in command.Arguments)
        {
            if (argument is not WordArgument { Value: var word })
                throw Unexpected("ROTATE", argument);

            // A word that looks like an orientation is one, unless a part has that name (a resistor called R90).
            if (OrientationPattern.IsMatch(word) && !editor.Schematic.Parts.ContainsKey(word))
            {
                absolute = word.StartsWith('=');
                orientation = Core.Rotation.Parse(word.TrimStart('=').ToUpperInvariant());
            }
            else
            {
                names.Add(word);
            }
        }
        if (names.Count == 0)
            throw new CommandException("ROTATE: name the part, e.g. ROTATE R1 or ROTATE R180 R1.");

        editor.Checkpoint();
        foreach (var instance in names.SelectMany(n => PartSelection(editor, n).Instances))
        {
            var selection = new Selection();
            selection.Instances.Add(instance);
            var target = absolute ? orientation : Combine(instance.Rotation, orientation);
            if (target.Mirror != instance.Rotation.Mirror)
                editor.Sheet.Mirror(selection, instance.Position);
            for (var i = 0; i < 4 && GeometryMath.NormalizeAngle(instance.Rotation.Angle - target.Angle) > GeometryMath.Epsilon; i++)
                editor.Sheet.Rotate90(selection, instance.Position);
        }
        editor.Sheet.RefreshPins();
        editor.NotifyChanged();
        return "";
    }

    /// <summary>The orientation reached by turning an object at <paramref name="current"/> further by <paramref name="by"/>.</summary>
    private static Rotation Combine(Rotation current, Rotation by)
    {
        var result = current;
        for (var i = 0; i < (int)Math.Round(by.Angle / 90) % 4; i++)
            result = result.Rotated90();
        return by.Mirror ? result.Mirrored() : result;
    }

    /// <summary>MIRROR: click a part, or the point to mirror the group around. MIRROR R1 by name.</summary>
    private static string Mirror(Editor editor, Command command)
    {
        if (command.Arguments.Count == 0)
            return StartTool(editor, new TurnTool(editor, "MIRROR", (sheet, selection, pivot) => sheet.Mirror(selection, pivot)));

        editor.Checkpoint();
        foreach (var argument in command.Arguments)
        {
            if (argument is not WordArgument name)
                throw Unexpected("MIRROR", argument);
            foreach (var instance in PartSelection(editor, name.Value).Instances)
            {
                var selection = new Selection();
                selection.Instances.Add(instance);
                editor.Sheet.Mirror(selection, instance.Position);
            }
        }
        editor.Sheet.RefreshPins();
        editor.NotifyChanged();
        return "";
    }

    /// <summary>COPY: with a group, copies it to the clipboard. Without, works as in Eagle: click an object and place its copy.</summary>
    private static string Copy(Editor editor, Command command)
    {
        if (editor.Selection.IsEmpty)
            return StartTool(editor, PlaceTool.CopyObjects(editor));

        editor.Clipboard = editor.Sheet.Copy(editor.Selection, SelectionCenter(editor));
        return $"Copied {editor.Selection.Count} object(s)";
    }

    /// <summary>CUT: copies the group to the clipboard and deletes it.</summary>
    private static string Cut(Editor editor, Command command)
    {
        if (editor.Selection.IsEmpty)
            throw new CommandException("CUT: select a group first (drag a rectangle).");

        var count = editor.Selection.Count;
        editor.Clipboard = editor.Sheet.Copy(editor.Selection, SelectionCenter(editor));
        DeleteSelection(editor);
        return $"Cut {count} object(s)";
    }

    /// <summary>PASTE: places the clipboard at the mouse; PASTE (x y) places it there.</summary>
    private static string Paste(Editor editor, Command command)
    {
        if (editor.Clipboard is not { IsEmpty: false } content)
            throw new CommandException("PASTE: the clipboard is empty. Select a group and use COPY or CUT first.");

        switch (command.Arguments)
        {
            case []:
                return StartTool(editor, PlaceTool.Paste(editor, content));
            case [CoordinateArgument at]:
                editor.Checkpoint();
                var pasted = editor.Sheet.Paste(content, ToMm(editor, at.Value));
                editor.Selection.Clear();
                editor.Selection.UnionWith(pasted);
                editor.NotifyChanged();
                return "";
            default:
                throw new CommandException("PASTE: use PASTE or PASTE (x y).");
        }
    }

    /// <summary>Deletes the group (the Del key).</summary>
    public static void DeleteSelection(Editor editor)
    {
        if (editor.Selection.IsEmpty)
            return;
        editor.Checkpoint();
        editor.Sheet.Delete(editor.Selection);
        editor.Selection.Clear();
        editor.NotifyChanged();
    }

    private static string StartTool(Editor editor, ITool tool)
    {
        editor.SetTool(tool);
        return tool.Prompt;
    }

    /// <summary>All gates of the named part on the current sheet.</summary>
    private static Selection PartSelection(Editor editor, string name)
    {
        var selection = new Selection();
        selection.Instances.UnionWith(editor.Sheet.Instances.Where(i => i.Part.Equals(name, StringComparison.OrdinalIgnoreCase)));
        if (selection.IsEmpty)
            throw new CommandException($"There is no part {name} on this sheet.");
        return selection;
    }

    /// <summary>The middle of the group, on the grid: the point that follows the mouse when pasting.</summary>
    private static Point SelectionCenter(Editor editor)
    {
        var selection = editor.Selection;
        var points = selection.Instances.Select(i => i.Position)
            .Concat(selection.WireEnds.Select(e => e.End ? e.Wire.To : e.Wire.From))
            .Concat(selection.Labels.Select(l => l.Position))
            .ToList();
        var center = new Point((points.Min(p => p.X) + points.Max(p => p.X)) / 2, (points.Min(p => p.Y) + points.Max(p => p.Y)) / 2);
        return editor.Grid.Snap(center);
    }
}
