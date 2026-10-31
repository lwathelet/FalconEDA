using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using FalconCAD.Commands;
using CorePoint = FalconCAD.Core.Point;
using Wire = FalconCAD.Core.Wire;

namespace FalconCAD.App.Controls;

/// <summary>
/// Draws the drawing and forwards mouse clicks to the active tool.
/// Wheel zooms around the cursor, middle button pans.
/// </summary>
public sealed class EditorCanvas : Control
{
    public static readonly StyledProperty<Editor?> EditorProperty =
        AvaloniaProperty.Register<EditorCanvas, Editor?>(nameof(Editor));

    private static readonly IBrush GridBrush = new SolidColorBrush(Color.FromRgb(110, 110, 110));
    private static readonly IPen OriginPen = new Pen(Brushes.Gray, 1);

    private static readonly Dictionary<int, IBrush> LayerBrushes = new()
    {
        [91] = new SolidColorBrush(Color.FromRgb(60, 200, 60)),
        [92] = new SolidColorBrush(Color.FromRgb(70, 110, 230)),
        [94] = new SolidColorBrush(Color.FromRgb(190, 40, 40)),
        [95] = new SolidColorBrush(Color.FromRgb(170, 170, 170)),
        [96] = new SolidColorBrush(Color.FromRgb(170, 170, 170)),
        [97] = new SolidColorBrush(Color.FromRgb(190, 40, 40)),
        [98] = new SolidColorBrush(Color.FromRgb(120, 120, 120)),
    };

    // Screen = world * scale with Y pointing up; _offset is where world (0, 0) is on screen.
    private double _scale = 4;
    private Point _offset;
    private bool _viewInitialized;
    private Point? _panFrom;

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
            oldEditor.Changed -= InvalidateVisual;
            oldEditor.ZoomRequested -= OnZoomRequested;
        }
        if (change.NewValue is Editor newEditor)
        {
            newEditor.Changed += InvalidateVisual;
            newEditor.ZoomRequested += OnZoomRequested;
        }
        InvalidateVisual();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (!_viewInitialized && e.NewSize.Width > 0 && e.NewSize.Height > 0)
        {
            _viewInitialized = true;
            Fit();
        }
    }

    private Point ToScreen(CorePoint p) => new(p.X * _scale + _offset.X, -p.Y * _scale + _offset.Y);

    private CorePoint ToWorld(Point s) => new((s.X - _offset.X) / _scale, -(s.Y - _offset.Y) / _scale);

    private double PickRadius => 6 / _scale;

    private void OnZoomRequested(double? factor)
    {
        if (factor is { } f)
            ZoomAt(f, new Point(Bounds.Width / 2, Bounds.Height / 2));
        else
            Fit();
    }

    private void ZoomAt(double factor, Point anchor)
    {
        var world = ToWorld(anchor);
        _scale = Math.Clamp(_scale * factor, 0.05, 2000);
        _offset = new Point(anchor.X - world.X * _scale, anchor.Y + world.Y * _scale);
        InvalidateVisual();
    }

    private void Fit()
    {
        var wires = Editor?.Drawing.AllWires().ToList() ?? new List<Wire>();
        double minX = 0, minY = 0, maxX = 250, maxY = 180;
        if (wires.Count > 0)
        {
            var points = wires.SelectMany(w => new[] { w.From, w.To }).ToList();
            minX = points.Min(p => p.X);
            maxX = points.Max(p => p.X);
            minY = points.Min(p => p.Y);
            maxY = points.Max(p => p.Y);
        }

        var width = Math.Max(maxX - minX, 10);
        var height = Math.Max(maxY - minY, 10);
        _scale = Math.Min(Bounds.Width / width, Bounds.Height / height) * 0.9;
        var center = new CorePoint((minX + maxX) / 2, (minY + maxY) / 2);
        _offset = new Point(Bounds.Width / 2 - center.X * _scale, Bounds.Height / 2 + center.Y * _scale);
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        context.DrawRectangle(Brushes.Black, null, new Rect(Bounds.Size));
        if (Editor is not { } editor)
            return;

        if (editor.Grid.Visible)
            DrawGrid(context, editor);

        var origin = ToScreen(new CorePoint(0, 0));
        context.DrawLine(OriginPen, origin - new Vector(6, 0), origin + new Vector(6, 0));
        context.DrawLine(OriginPen, origin - new Vector(0, 6), origin + new Vector(0, 6));

        var visibleLayers = editor.Drawing.Layers.Where(l => l.Visible).Select(l => l.Number).ToHashSet();
        foreach (var wire in editor.Drawing.AllWires().Where(w => visibleLayers.Contains(w.Layer)))
            DrawWire(context, wire);

        if (visibleLayers.Contains(FalconCAD.Core.Drawing.NetLayer))
        {
            var radius = Math.Max(2.5, 0.5 * _scale);
            foreach (var junction in editor.Drawing.Nets.SelectMany(n => n.Junctions()))
                context.DrawEllipse(LayerBrush(FalconCAD.Core.Drawing.NetLayer), null, ToScreen(junction), radius, radius);
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
        context.DrawLine(pen, ToScreen(wire.From), ToScreen(wire.To));
    }

    private static IBrush LayerBrush(int layer) => LayerBrushes.GetValueOrDefault(layer, Brushes.White);

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
