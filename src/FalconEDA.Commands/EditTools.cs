using FalconEDA.Core;

namespace FalconEDA.Commands;

/// <summary>
/// MOVE: picks up the group, or the object clicked, and carries it with the mouse until the next click.
/// Right-click rotates it while carried. Nets on the pins of moved parts stretch along.
/// </summary>
public sealed class MoveTool : ITool
{
    private readonly Editor _editor;
    private Selection? _carried;
    private Point _from;

    public MoveTool(Editor editor) => _editor = editor;

    /// <summary>Starts with <paramref name="carried"/> already attached to the mouse at <paramref name="from"/> (MOVE R1).</summary>
    public MoveTool(Editor editor, Selection carried, Point from) : this(editor)
    {
        _editor.Checkpoint();
        _carried = carried;
        _from = from;
    }

    public string Prompt => _carried is not null
        ? "MOVE: left-click to drop, right-click to rotate. Esc cancels."
        : _editor.Selection.IsEmpty
            ? "MOVE: left-click an object to pick it up. Esc ends."
            : "MOVE: left-click to pick up the group. Esc ends.";

    public bool SnapToGrid => true;
    public IReadOnlyList<Wire> Preview => Array.Empty<Wire>();

    public void Click(Point p, double pickRadius, int clickCount)
    {
        if (_carried is not null)
        {
            _editor.Sheet.RefreshPins();
            _carried = null;
            _editor.NotifyChanged();
            return;
        }

        var selection = _editor.Selection.IsEmpty ? _editor.Sheet.SelectForMove(p, pickRadius) : _editor.Selection;
        if (selection.IsEmpty)
            return;

        _editor.Checkpoint();
        _carried = selection;
        _from = p;
        _editor.NotifyChanged();
    }

    // The drawing itself changes while carrying, so what you see is exactly what you get.
    public void Move(Point p)
    {
        if (_carried is null || GeometryMath.SamePoint(p, _from))
            return;
        _editor.Sheet.Move(_carried, p - _from);
        _from = p;
        _editor.NotifyChanged();
    }

    public void RightClick(Point p)
    {
        if (_carried is null)
            return;
        _editor.Sheet.Rotate90(_carried, _from);
        _editor.NotifyChanged();
    }

    public void Cancel()
    {
        if (_carried is null)
        {
            _editor.SetTool(null);
            return;
        }
        _carried = null;
        _editor.RevertCheckpoint();
    }
}

/// <summary>ROTATE and MIRROR: a click turns the group around the clicked point, or the clicked part around its origin.</summary>
public sealed class TurnTool : ITool
{
    private readonly Editor _editor;
    private readonly string _name;
    private readonly Action<Sheet, Selection, Point> _turn;

    public TurnTool(Editor editor, string name, Action<Sheet, Selection, Point> turn)
    {
        _editor = editor;
        _name = name;
        _turn = turn;
    }

    public string Prompt => _editor.Selection.IsEmpty
        ? $"{_name}: left-click a part or label. Esc ends."
        : $"{_name}: left-click the point to turn the group around. Esc ends.";

    public bool SnapToGrid => true;
    public IReadOnlyList<Wire> Preview => Array.Empty<Wire>();

    public void Click(Point p, double pickRadius, int clickCount)
    {
        Selection selection;
        Point pivot;
        if (!_editor.Selection.IsEmpty)
        {
            selection = _editor.Selection;
            pivot = p;
        }
        else if (_editor.Sheet.TextAt(p, pickRadius) is Label label)
        {
            selection = new Selection();
            selection.Labels.Add(label);
            pivot = label.Position;
        }
        else if (_editor.Sheet.TextAt(p, pickRadius) is PlacedAttribute { Position: { } position } attribute)
        {
            selection = new Selection();
            selection.Attributes.Add(attribute);
            pivot = position;
        }
        else if (_editor.Sheet.InstanceAt(p, pickRadius) is { } instance)
        {
            selection = new Selection();
            selection.Instances.Add(instance);
            pivot = instance.Position;
        }
        else
        {
            return;
        }

        _editor.Checkpoint();
        _turn(_editor.Sheet, selection, pivot);
        _editor.Sheet.RefreshPins();
        _editor.NotifyChanged();
    }

    public void Move(Point p) { }

    public void Cancel() => _editor.SetTool(null);
}

/// <summary>
/// Places copied objects: shows them at the mouse and drops them on a click.
/// PASTE places the clipboard once. COPY without a group works as in Eagle: click an object,
/// place its copy, then click the next object to copy.
/// </summary>
public sealed class PlaceTool : ITool
{
    private readonly Editor _editor;
    private readonly bool _pickEachTime;
    private ClipboardContent? _content;
    private Point _cursor;

    private PlaceTool(Editor editor, ClipboardContent? content, bool pickEachTime)
    {
        _editor = editor;
        _content = content;
        _pickEachTime = pickEachTime;
    }

    public static PlaceTool Paste(Editor editor, ClipboardContent content) => new(editor, content, pickEachTime: false);

    public static PlaceTool CopyObjects(Editor editor) => new(editor, null, pickEachTime: true);

    public string Prompt => _content is null
        ? "COPY: left-click the object to copy. Esc ends."
        : _pickEachTime
            ? "COPY: left-click to place the copy. Esc cancels."
            : "PASTE: left-click to place. Esc cancels.";

    public bool SnapToGrid => true;

    public IReadOnlyList<Wire> Preview => _content?.Outline(_cursor).ToList() ?? (IReadOnlyList<Wire>)Array.Empty<Wire>();

    public void Click(Point p, double pickRadius, int clickCount)
    {
        if (_content is null)
        {
            var selection = _editor.Sheet.SelectAt(p, pickRadius);
            if (selection.IsEmpty)
                return;
            _content = _editor.Sheet.Copy(selection, p);
            _cursor = p;
            _editor.NotifyChanged();
            return;
        }

        _editor.Checkpoint();
        var pasted = _editor.Sheet.Paste(_content, p);

        // What was just placed becomes the group, so it can be moved or turned right away.
        _editor.Selection.Clear();
        _editor.Selection.UnionWith(pasted);

        if (_pickEachTime)
        {
            _content = null;
            _editor.NotifyChanged();
        }
        else
        {
            _editor.SetTool(null);
        }
    }

    public void Move(Point p) => _cursor = p;

    public void Cancel()
    {
        if (_pickEachTime && _content is not null)
        {
            _content = null;
            _editor.NotifyChanged();
        }
        else
        {
            _editor.SetTool(null);
        }
    }
}
