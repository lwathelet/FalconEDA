using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using FalconCAD.Commands;
using FalconCAD.Core;
using CorePoint = FalconCAD.Core.Point;
using Point = Avalonia.Point;
using Rect = Avalonia.Rect;

namespace FalconCAD.App.Controls;

/// <summary>
/// Draws the current sheet and forwards mouse clicks to the active tool.
/// Wheel zooms around the cursor, middle button pans.
/// </summary>
public sealed class EditorCanvas : Control
{
    public static readonly StyledProperty<Editor?> EditorProperty =
        AvaloniaProperty.Register<EditorCanvas, Editor?>(nameof(Editor));

    private static readonly IBrush GridBrush = new SolidColorBrush(Color.FromRgb(110, 110, 110));
    private static readonly IPen OriginPen = new Pen(Brushes.Gray, 1);
    private static readonly Typeface TextFace = new("Arial");

    // The usual 16-colour palette on a black background, indexed by the layer colour stored in Eagle files.
    private static readonly IBrush[] Palette = new[]
    {
        (0, 0, 0), (50, 50, 200), (50, 200, 50), (50, 200, 200), (200, 50, 50), (200, 50, 200), (200, 200, 50), (200, 200, 200),
        (80, 80, 80), (0, 0, 255), (0, 255, 0), (0, 255, 255), (255, 0, 0), (255, 0, 255), (255, 255, 0), (255, 255, 255),
    }.Select(c => (IBrush)new SolidColorBrush(Color.FromRgb((byte)c.Item1, (byte)c.Item2, (byte)c.Item3))).ToArray();

    // Screen = world * scale with Y pointing up; _offset is where world (0, 0) is on screen.
    private double _scale = 4;
    private Point _offset;
    // Until the user zooms or pans, keep the whole sheet in view while the window resizes.
    private bool _autoFit = true;
    private Point? _panFrom;

    // Rebuilt when the editor reports a change.
    private SheetView? _view;
    private Dictionary<int, Layer> _layers = new();

    public EditorCanvas()
    {
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Cross);
    }

    public Editor? Editor
    {
        get => GetValue(EditorProperty);
        set => SetValue(EditorProperty, value);
    }

    /// <summary>The cursor position in drawing space, snapped to the grid.</summary>
    public event Action<CorePoint>? CursorMoved;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != EditorProperty)
            return;

        if (change.OldValue is Editor oldEditor)
        {
            oldEditor.Changed -= OnEditorChanged;
            oldEditor.ZoomRequested -= OnZoomRequested;
        }
        if (change.NewValue is Editor newEditor)
        {
            newEditor.Changed += OnEditorChanged;
            newEditor.ZoomRequested += OnZoomRequested;
        }
        OnEditorChanged();
    }

    private void OnEditorChanged()
    {
        _view = null;
        InvalidateVisual();
    }

    private SheetView? View
    {
        get
        {
            if (_view is null && Editor is { } editor)
            {
                _view = SheetView.Build(editor.Schematic, editor.SheetIndex);
                _layers = editor.Schematic.Layers.ToDictionary(l => l.Number);
            }
            return _view;
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (_autoFit)
            Fit();
    }

    private Point ToScreen(CorePoint p) => new(p.X * _scale + _offset.X, -p.Y * _scale + _offset.Y);

    private CorePoint ToWorld(Point s) => new((s.X - _offset.X) / _scale, -(s.Y - _offset.Y) / _scale);

    private double PickRadius => 6 / _scale;

    private void OnZoomRequested(double? factor)
    {
        if (factor is { } f)
        {
            ZoomAt(f, new Point(Bounds.Width / 2, Bounds.Height / 2));
        }
        else
        {
            _autoFit = true;
            Fit();
        }
    }

    private void ZoomAt(double factor, Point anchor)
    {
        _autoFit = false;
        var world = ToWorld(anchor);
        _scale = Math.Clamp(_scale * factor, 0.05, 2000);
        _offset = new Point(anchor.X - world.X * _scale, anchor.Y + world.Y * _scale);
        InvalidateVisual();
    }

    private void Fit()
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        var bounds = View?.Bounds() ?? new FalconCAD.Core.Rect(new CorePoint(0, 0), new CorePoint(250, 180));
        var width = Math.Max(bounds.Width, 10);
        var height = Math.Max(bounds.Height, 10);
        _scale = Math.Min(Bounds.Width / width, Bounds.Height / height) * 0.9;
        var center = new CorePoint((bounds.Min.X + bounds.Max.X) / 2, (bounds.Min.Y + bounds.Max.Y) / 2);
        _offset = new Point(Bounds.Width / 2 - center.X * _scale, Bounds.Height / 2 + center.Y * _scale);
        InvalidateVisual();
    }

    private bool IsLayerVisible(int layer) => !_layers.TryGetValue(layer, out var l) || l.Visible;

    private IBrush LayerBrush(int layer) =>
        _layers.TryGetValue(layer, out var l) && l.Color >= 0 && l.Color < Palette.Length ? Palette[l.Color] : Brushes.White;

    public override void Render(DrawingContext context)
    {
        context.DrawRectangle(Brushes.Black, null, new Rect(Bounds.Size));
        if (Editor is not { } editor || View is not { } view)
            return;

        if (editor.Grid.Visible)
            DrawGrid(context, editor);

        var origin = ToScreen(new CorePoint(0, 0));
        context.DrawLine(OriginPen, origin - new Vector(6, 0), origin + new Vector(6, 0));
        context.DrawLine(OriginPen, origin - new Vector(0, 6), origin + new Vector(0, 6));

        foreach (var polygon in view.Polygons.Where(p => IsLayerVisible(p.Layer)))
            DrawPolygon(context, polygon);
        foreach (var circle in view.Circles.Where(c => IsLayerVisible(c.Layer)))
            DrawCircle(context, circle);
        foreach (var wire in view.Wires.Where(w => IsLayerVisible(w.Layer)))
            DrawWire(context, wire);
        foreach (var text in view.Texts.Where(t => IsLayerVisible(t.Layer)))
            DrawText(context, text);
        foreach (var o in view.Origins.Select(ToScreen))
        {
            context.DrawLine(OriginPen, o - new Vector(3, 0), o + new Vector(3, 0));
            context.DrawLine(OriginPen, o - new Vector(0, 3), o + new Vector(0, 3));
        }

        if (editor.Tool is { } tool)
        {
            foreach (var wire in tool.Preview)
                DrawWire(context, wire);
        }
    }

    private void DrawGrid(DrawingContext context, Editor editor)
    {
        // Show every grid point while they are far enough apart, otherwise thin them out.
        var step = editor.Grid.SizeMm;
        while (step * _scale < 10)
            step *= 2;

        var topLeft = ToWorld(new Point(0, 0));
        var bottomRight = ToWorld(new Point(Bounds.Width, Bounds.Height));
        for (var i = Math.Ceiling(topLeft.X / step); i * step <= bottomRight.X; i++)
        {
            for (var j = Math.Ceiling(bottomRight.Y / step); j * step <= topLeft.Y; j++)
            {
                var s = ToScreen(new CorePoint(i * step, j * step));
                context.DrawRectangle(GridBrush, null, new Rect(s.X, s.Y, 1, 1));
            }
        }
    }

    private void DrawWire(DrawingContext context, Wire wire)
    {
        var pen = new Pen(LayerBrush(wire.Layer), Math.Max(1, wire.Width * _scale), lineCap: PenLineCap.Round);
        var from = ToScreen(wire.From);
        var to = ToScreen(wire.To);
        if (wire.Curve == 0)
        {
            context.DrawLine(pen, from, to);
            return;
        }

        // Arc through both ends. The Y flip turns counter-clockwise in the drawing into clockwise on screen.
        var chord = Math.Sqrt((to.X - from.X) * (to.X - from.X) + (to.Y - from.Y) * (to.Y - from.Y));
        var radius = chord / (2 * Math.Sin(Math.Abs(wire.Curve) * Math.PI / 360));
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(from, false);
            g.ArcTo(to, new Size(radius, radius), 0, Math.Abs(wire.Curve) > 180,
                wire.Curve > 0 ? SweepDirection.Clockwise : SweepDirection.CounterClockwise);
            g.EndFigure(false);
        }
        context.DrawGeometry(null, pen, geometry);
    }

    private void DrawCircle(DrawingContext context, Circle circle)
    {
        var radius = circle.Radius * _scale;
        if (circle.Width == 0)
            context.DrawEllipse(LayerBrush(circle.Layer), null, ToScreen(circle.Center), Math.Max(radius, 1.5), Math.Max(radius, 1.5));
        else
            context.DrawEllipse(null, new Pen(LayerBrush(circle.Layer), Math.Max(1, circle.Width * _scale)), ToScreen(circle.Center), radius, radius);
    }

    private void DrawPolygon(DrawingContext context, Polygon polygon)
    {
        if (polygon.Vertices.Count < 3)
            return;

        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(ToScreen(polygon.Vertices[0]), true);
            foreach (var v in polygon.Vertices.Skip(1))
                g.LineTo(ToScreen(v));
            g.EndFigure(true);
        }
        var brush = LayerBrush(polygon.Layer);
        var pen = polygon.Width > 0 ? new Pen(brush, polygon.Width * _scale) : null;
        context.DrawGeometry(brush, pen, geometry);
    }

    private void DrawText(DrawingContext context, TextPrimitive text)
    {
        // Eagle's size is the height of a capital letter; a capital is about 0.72 em in common fonts.
        var capHeight = text.Size * _scale;
        if (capHeight < 2.5 || string.IsNullOrEmpty(text.Value))
            return;

        var formatted = new FormattedText(text.Value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            TextFace, capHeight / 0.72, LayerBrush(text.Layer));

        // Offset from the anchor to the top-left of the text box, in unrotated screen space.
        var x = text.Align.H switch
        {
            HAlign.Center => -formatted.Width / 2,
            HAlign.Right => -formatted.Width,
            _ => 0,
        };
        var lastBaseline = formatted.Height - (formatted.Height / Math.Max(1, text.Value.Split('\n').Length)) + formatted.Baseline;
        var y = text.Align.V switch
        {
            VAlign.Top => -(formatted.Baseline - capHeight),
            VAlign.Center => -(lastBaseline + formatted.Baseline - capHeight) / 2,
            _ => -lastBaseline,
        };

        var anchor = ToScreen(text.Position);
        var transform = Matrix.CreateRotation(-text.Angle * Math.PI / 180) * Matrix.CreateTranslation(anchor.X, anchor.Y);
        using (context.PushTransform(transform))
            context.DrawText(formatted, new Point(x, y));
    }

    private CorePoint Snapped(Point screen, bool snap)
    {
        var world = ToWorld(screen);
        return snap && Editor is { } editor ? editor.Grid.Snap(world) : world;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsMiddleButtonPressed)
        {
            _panFrom = point.Position;
            _autoFit = false;
            e.Pointer.Capture(this);
            e.Handled = true;
        }
        else if (point.Properties.IsLeftButtonPressed && Editor?.Tool is { } tool)
        {
            tool.Click(Snapped(point.Position, tool.SnapToGrid), PickRadius, e.ClickCount);
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var position = e.GetPosition(this);
        if (_panFrom is { } from)
        {
            _offset += position - from;
            _panFrom = position;
            InvalidateVisual();
        }

        if (Editor is not { } editor)
            return;

        var snapped = editor.Grid.Snap(ToWorld(position));
        CursorMoved?.Invoke(snapped);
        if (editor.Tool is { } tool)
        {
            tool.Move(Snapped(position, tool.SnapToGrid));
            InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.InitialPressMouseButton == MouseButton.Middle && _panFrom is not null)
        {
            _panFrom = null;
            e.Pointer.Capture(null);
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        ZoomAt(Math.Pow(1.2, e.Delta.Y), e.GetPosition(this));
        e.Handled = true;
    }
}
