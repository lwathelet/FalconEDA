using FalconCAD.Core;

namespace FalconCAD.Commands;

/// <summary>A command waiting for mouse input, such as NET without coordinates.</summary>
public interface ITool
{
    /// <summary>Hint shown in the status bar.</summary>
    string Prompt { get; }

    bool SnapToGrid { get; }

    /// <summary>Rubber-band segments to draw while the tool is in progress.</summary>
    IReadOnlyList<Wire> Preview { get; }

    void Click(Point p, double pickRadius, int clickCount);
    void Move(Point p);

    /// <summary>Esc: abandon the current operation, or leave the tool if nothing is in progress.</summary>
    void Cancel();
}

/// <summary>Draws a polyline click by click. Used by NET and WIRE.</summary>
public sealed class PolylineTool : ITool
{
    private readonly Editor _editor;
    private readonly int _layer;
    private readonly double _width;
    private readonly Action<IReadOnlyList<Point>> _commit;
    private readonly bool _endOnNet;
    private readonly List<Point> _points = new();
    private Point _cursor;

    public PolylineTool(Editor editor, string name, int layer, double width,
        Action<IReadOnlyList<Point>> commit, bool endOnNet)
    {
        _editor = editor;
        _layer = layer;
        _width = width;
        _commit = commit;
        _endOnNet = endOnNet;
        Prompt = $"{name}: left-click to start, click to bend, double-click to end. Esc cancels.";
    }

    public string Prompt { get; }
    public bool SnapToGrid => true;

    public IReadOnlyList<Wire> Preview
    {
        get
        {
            var preview = new List<Wire>();
            for (var i = 1; i < _points.Count; i++)
                preview.Add(Segment(_points[i - 1], _points[i]));
            if (_points.Count > 0)
                preview.Add(Segment(_points[^1], _cursor));
            return preview;
        }
    }

    public void Click(Point p, double pickRadius, int clickCount)
    {
        if (clickCount >= 2)
        {
            Finish();
            return;
        }

        if (_points.Count == 0 || !GeometryMath.SamePoint(_points[^1], p))
            _points.Add(p);

        // Like Eagle, a net ends by itself when it lands on another net.
        if (_endOnNet && _points.Count >= 2 && _editor.Sheet.Nets.Any(n => n.Touches(p)))
            Finish();

        _editor.NotifyChanged();
    }

    public void Move(Point p) => _cursor = p;

    public void Cancel()
    {
        if (_points.Count > 0)
        {
            _points.Clear();
            _editor.NotifyChanged();
        }
        else
        {
            _editor.SetTool(null);
        }
    }

    private void Finish()
    {
        if (_points.Count >= 2)
            _commit(_points.ToList());
        _points.Clear();
        _editor.NotifyChanged();
    }

    private Wire Segment(Point from, Point to) => new() { From = from, To = to, Width = _width, Layer = _layer };
}

public sealed class DeleteTool : ITool
{
    private readonly Editor _editor;

    public DeleteTool(Editor editor) => _editor = editor;

    public string Prompt => "DELETE: left-click an object to delete it. Esc ends.";
    public bool SnapToGrid => false;
    public IReadOnlyList<Wire> Preview => Array.Empty<Wire>();

    public void Click(Point p, double pickRadius, int clickCount)
    {
        if (_editor.Sheet.FindNearest(p, pickRadius) is not { } wire)
            return;

        _editor.Checkpoint();
        _editor.Sheet.Delete(wire);
        _editor.NotifyChanged();
    }

    public void Move(Point p) { }

    public void Cancel() => _editor.SetTool(null);
}
