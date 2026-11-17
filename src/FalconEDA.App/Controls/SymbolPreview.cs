using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Wire = FalconEDA.Core.Wire;

namespace FalconEDA.App.Controls;

/// <summary>Draws a symbol's outline, scaled to fit, for the library browser.</summary>
public sealed class SymbolPreview : Control
{
    private static readonly IPen SymbolPen = new Pen(new SolidColorBrush(Color.FromRgb(200, 50, 50)), 1.5);

    private IReadOnlyList<Wire> _wires = Array.Empty<Wire>();

    public IReadOnlyList<Wire> Wires
    {
        get => _wires;
        set
        {
            _wires = value;
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        context.DrawRectangle(Brushes.Black, null, new Rect(Bounds.Size));
        if (_wires.Count == 0)
            return;

        var points = _wires.SelectMany(w => new[] { w.From, w.To }).ToList();
        double minX = points.Min(p => p.X), maxX = points.Max(p => p.X);
        double minY = points.Min(p => p.Y), maxY = points.Max(p => p.Y);
        var scale = Math.Min(Bounds.Width / Math.Max(maxX - minX, 1), Bounds.Height / Math.Max(maxY - minY, 1)) * 0.85;
        var cx = (minX + maxX) / 2;
        var cy = (minY + maxY) / 2;

        Point ToScreen(FalconEDA.Core.Point p) =>
            new(Bounds.Width / 2 + (p.X - cx) * scale, Bounds.Height / 2 - (p.Y - cy) * scale);

        foreach (var wire in _wires)
            context.DrawLine(SymbolPen, ToScreen(wire.From), ToScreen(wire.To));
    }
}
