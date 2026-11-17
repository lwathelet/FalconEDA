namespace FalconEDA.Core;

/// <summary>
/// A group of objects on a sheet. Wires are selected per end, as in Eagle: moving a group
/// moves the selected ends, so a wire with only one end in the group stretches.
/// </summary>
public sealed class Selection
{
    public HashSet<Instance> Instances { get; } = new();

    /// <summary>Selected wire ends: <c>true</c> is the wire's <see cref="Wire.To"/> end, <c>false</c> its <see cref="Wire.From"/> end.</summary>
    public HashSet<(Wire Wire, bool End)> WireEnds { get; } = new();
    public HashSet<Label> Labels { get; } = new();

    /// <summary>Smashed texts (name, value...) moved on their own, without their part.</summary>
    public HashSet<PlacedAttribute> Attributes { get; } = new();

    public bool IsEmpty => Instances.Count == 0 && WireEnds.Count == 0 && Labels.Count == 0 && Attributes.Count == 0;

    public int Count => Instances.Count + Wires.Count() + Labels.Count + Attributes.Count;

    /// <summary>Wires with at least one end selected.</summary>
    public IEnumerable<Wire> Wires => WireEnds.Select(e => e.Wire).Distinct();

    /// <summary>Wires with both ends selected: these are copied and deleted with the group.</summary>
    public IEnumerable<Wire> WholeWires => Wires.Where(w => WireEnds.Contains((w, false)) && WireEnds.Contains((w, true)));

    public void AddWhole(Wire wire)
    {
        WireEnds.Add((wire, false));
        WireEnds.Add((wire, true));
    }

    public void UnionWith(Selection other)
    {
        Instances.UnionWith(other.Instances);
        WireEnds.UnionWith(other.WireEnds);
        Labels.UnionWith(other.Labels);
        Attributes.UnionWith(other.Attributes);
    }

    /// <summary>Adds what is not selected yet and removes what is (Ctrl+click).</summary>
    public void Toggle(Selection other)
    {
        Instances.SymmetricExceptWith(other.Instances);
        WireEnds.SymmetricExceptWith(other.WireEnds);
        Labels.SymmetricExceptWith(other.Labels);
        Attributes.SymmetricExceptWith(other.Attributes);
    }

    public void Clear()
    {
        Instances.Clear();
        WireEnds.Clear();
        Labels.Clear();
        Attributes.Clear();
    }
}

/// <summary>Objects copied from a sheet, ready to paste into this or another schematic.</summary>
public sealed class ClipboardContent
{
    public required Point Reference { get; init; }
    public List<Part> Parts { get; } = new();
    public Dictionary<string, Library> Libraries { get; } = new();
    public List<Instance> Instances { get; } = new();
    public List<Net> Nets { get; } = new();
    public List<Wire> Wires { get; } = new();

    public bool IsEmpty => Instances.Count == 0 && Nets.Count == 0 && Wires.Count == 0;

    /// <summary>A light outline of the content with its reference point at <paramref name="at"/>, to show while placing it.</summary>
    public IEnumerable<Wire> Outline(Point at)
    {
        var delta = at - Reference;
        Wire Moved(Wire w) => new() { From = w.From + delta, To = w.To + delta, Width = w.Width, Layer = w.Layer, Curve = w.Curve };

        foreach (var wire in Wires.Concat(Nets.SelectMany(n => n.Wires)))
            yield return Moved(wire);

        foreach (var instance in Instances)
        {
            var part = Parts.FirstOrDefault(p => p.Name == instance.Part);
            if (part is null || !Libraries.TryGetValue(part.LibraryKey, out var library)
                || !library.DeviceSets.TryGetValue(part.DeviceSet, out var deviceSet)
                || !deviceSet.Gates.TryGetValue(instance.Gate, out var gate)
                || !library.Symbols.TryGetValue(gate.Symbol, out var symbol))
                continue;
            foreach (var wire in Symbols.Outline(symbol, new Transform(instance.Position + delta, instance.Rotation)))
                yield return wire;
        }
    }
}

public static class Symbols
{
    /// <summary>The lines of a placed symbol: its wires, rectangle edges and pins. Used to show it highlighted or while moving.</summary>
    public static IEnumerable<Wire> Outline(Symbol symbol, Transform transform)
    {
        Wire Line(Point a, Point b, int layer) => new() { From = transform.Apply(a), To = transform.Apply(b), Width = 0.1524, Layer = layer };

        foreach (var w in symbol.Graphics.Wires)
            yield return Line(w.From, w.To, w.Layer);
        foreach (var r in symbol.Graphics.Rectangles)
        {
            yield return Line(r.Min, new Point(r.Max.X, r.Min.Y), r.Layer);
            yield return Line(new Point(r.Max.X, r.Min.Y), r.Max, r.Layer);
            yield return Line(r.Max, new Point(r.Min.X, r.Max.Y), r.Layer);
            yield return Line(new Point(r.Min.X, r.Max.Y), r.Min, r.Layer);
        }
        foreach (var p in symbol.Pins.Where(p => p.LengthMm > 0))
            yield return Line(p.Position, p.Position + Point.Direction(p.Rotation.Angle) * p.LengthMm, 94);
    }

    /// <summary>
    /// The attributes a newly placed part gets, as Eagle 9 places every part "smashed": one per text variable
    /// in the symbol (&gt;NAME, &gt;VALUE, &gt;VAL...), at the text's place on the sheet. Eagle shows a part's
    /// name and value from these, so a part without them would show neither.
    /// </summary>
    public static IEnumerable<PlacedAttribute> SmashedAttributes(Symbol symbol, Transform transform)
    {
        foreach (var text in symbol.Graphics.Texts.Where(t => t.Value.StartsWith('>') && t.Value.Length > 1)
                     .DistinctBy(t => t.Value.ToUpperInvariant()))
        {
            var rotation = text.Rotation;
            for (var i = 0; i < (int)Math.Round(transform.Rotation.Angle / 90) % 4; i++)
                rotation = rotation.Rotated90();
            if (transform.Rotation.Mirror)
                rotation = rotation.Mirrored();

            yield return new PlacedAttribute
            {
                Name = text.Value[1..].ToUpperInvariant(),
                Position = transform.Apply(text.Position),
                Size = text.Size,
                Layer = text.Layer,
                Rotation = rotation,
                Align = text.Align,
            };
        }
    }

    /// <summary>The area covered by a placed symbol, without its texts.</summary>
    public static Rect? Bounds(Symbol symbol, Transform transform)
    {
        var points = Outline(symbol, transform).SelectMany(w => new[] { w.From, w.To })
            .Concat(symbol.Graphics.Circles.SelectMany(c => new[]
            {
                transform.Apply(c.Center - new Point(c.Radius, c.Radius)),
                transform.Apply(c.Center + new Point(c.Radius, c.Radius)),
            }))
            .Concat(symbol.Graphics.Polygons.SelectMany(p => p.Vertices.Select(transform.Apply)))
            .Concat(symbol.Pins.Select(p => transform.Apply(p.Position)))
            .ToList();
        if (points.Count == 0)
            return null;
        return new Rect(
            new Point(points.Min(p => p.X), points.Min(p => p.Y)),
            new Point(points.Max(p => p.X), points.Max(p => p.Y)));
    }
}
