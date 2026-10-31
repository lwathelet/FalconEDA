namespace FalconCAD.Core;

public sealed class Layer
{
    public required int Number { get; init; }
    public required string Name { get; init; }
    public bool Visible { get; set; } = true;

    public override string ToString() => $"{Number} {Name}";
}

public sealed class Wire
{
    public required Point From { get; init; }
    public required Point To { get; init; }
    public double Width { get; init; } = 0.15;
    public int Layer { get; init; }

    public double DistanceTo(Point p) => GeometryMath.DistanceToSegment(p, From, To);
}

public sealed class Net
{
    public required string Name { get; set; }
    public List<Wire> Wires { get; } = new();

    public bool Touches(Point p) => Wires.Any(w => w.DistanceTo(p) < GeometryMath.Epsilon);

    /// <summary>
    /// Points where three or more wire ends meet, counting a wire that passes
    /// through a point as two ends. These get a junction dot.
    /// </summary>
    public IEnumerable<Point> Junctions()
    {
        var ends = Wires.SelectMany(w => new[] { w.From, w.To })
            .DistinctBy(p => (Math.Round(p.X, 6), Math.Round(p.Y, 6)));

        foreach (var p in ends)
        {
            var degree = 0;
            foreach (var w in Wires)
            {
                if (GeometryMath.SamePoint(w.From, p) || GeometryMath.SamePoint(w.To, p))
                    degree++;
                else if (w.DistanceTo(p) < GeometryMath.Epsilon)
                    degree += 2;
            }
            if (degree >= 3)
                yield return p;
        }
    }
}

/// <summary>A schematic sheet or a board: the thing an editor window shows.</summary>
public sealed class Drawing
{
    public const int NetLayer = 91;
    public const double DefaultNetWidth = 0.1524;

    public List<Layer> Layers { get; } = new();
    public List<Net> Nets { get; } = new();

    /// <summary>Graphic wires that are not part of a net.</summary>
    public List<Wire> Wires { get; } = new();

    public static Drawing CreateSchematic()
    {
        var drawing = new Drawing();
        drawing.Layers.AddRange(new[]
        {
            new Layer { Number = 91, Name = "Nets" },
            new Layer { Number = 92, Name = "Busses" },
            new Layer { Number = 94, Name = "Symbols" },
            new Layer { Number = 95, Name = "Names" },
            new Layer { Number = 96, Name = "Values" },
            new Layer { Number = 97, Name = "Info" },
            new Layer { Number = 98, Name = "Guide" },
        });
        return drawing;
    }

    public IEnumerable<Wire> AllWires() => Wires.Concat(Nets.SelectMany(n => n.Wires));

    public string NextNetName()
    {
        var i = 1;
        while (Nets.Any(n => n.Name == $"N${i}"))
            i++;
        return $"N${i}";
    }

    /// <summary>
    /// Adds a net polyline. If either end lands on an existing net, the new
    /// segments join that net, and nets touched at both ends are merged.
    /// </summary>
    public Net AddNet(IReadOnlyList<Point> points, double width = DefaultNetWidth, string? name = null)
    {
        if (points.Count < 2)
            throw new ArgumentException("A net needs at least two points.", nameof(points));

        var touched = Nets.Where(n => n.Touches(points[0]) || n.Touches(points[^1])).ToList();
        var target = (name is null ? null : Nets.FirstOrDefault(n => n.Name == name)) ?? touched.FirstOrDefault();
        if (target is null)
        {
            target = new Net { Name = name ?? NextNetName() };
            Nets.Add(target);
        }

        foreach (var other in touched.Where(n => n != target))
        {
            target.Wires.AddRange(other.Wires);
            Nets.Remove(other);
        }

        target.Wires.AddRange(Segments(points, width, NetLayer));
        return target;
    }

    public void AddWire(IReadOnlyList<Point> points, double width, int layer)
    {
        if (points.Count < 2)
            throw new ArgumentException("A wire needs at least two points.", nameof(points));
        Wires.AddRange(Segments(points, width, layer));
    }

    /// <summary>Deletes the wire segment nearest to <paramref name="p"/>, if one is within <paramref name="radius"/>.</summary>
    public bool DeleteNearest(Point p, double radius)
    {
        var nearest = AllWires()
            .Select(w => (Wire: w, Distance: w.DistanceTo(p)))
            .Where(x => x.Distance <= radius)
            .OrderBy(x => x.Distance)
            .Select(x => x.Wire)
            .FirstOrDefault();
        if (nearest is null)
            return false;

        if (!Wires.Remove(nearest))
        {
            var net = Nets.First(n => n.Wires.Contains(nearest));
            net.Wires.Remove(nearest);
            if (net.Wires.Count == 0)
                Nets.Remove(net);
        }
        return true;
    }

    private static IEnumerable<Wire> Segments(IReadOnlyList<Point> points, double width, int layer)
    {
        for (var i = 1; i < points.Count; i++)
        {
            if (!GeometryMath.SamePoint(points[i - 1], points[i]))
                yield return new Wire { From = points[i - 1], To = points[i], Width = width, Layer = layer };
        }
    }
}
