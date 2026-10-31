namespace FalconCAD.Core;

public sealed class Sheet
{
    public const int NetLayer = 91;
    public const int BusLayer = 92;
    public const double DefaultNetWidth = 0.1524;

    private readonly Schematic _owner;

    internal Sheet(Schematic owner) => _owner = owner;

    public Graphics Plain { get; } = new();
    public List<Instance> Instances { get; } = new();
    public List<Net> Nets { get; } = new();
    public List<Net> Busses { get; } = new();

    /// <summary>Graphic wires that are not part of a net.</summary>
    public List<Wire> Wires => Plain.Wires;

    public IEnumerable<Wire> AllWires() =>
        Wires.Concat(Nets.SelectMany(n => n.Wires)).Concat(Busses.SelectMany(b => b.Wires));

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
            target = new Net { Name = name ?? _owner.NextNetName() };
            Nets.Add(target);
        }

        foreach (var other in touched.Where(n => n != target))
        {
            target.Wires.AddRange(other.Wires);
            target.Labels.AddRange(other.Labels);
            target.Junctions.AddRange(other.Junctions);
            target.PinRefs.AddRange(other.PinRefs);
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

    /// <summary>The wire segment nearest to <paramref name="p"/> within <paramref name="radius"/>, if any.</summary>
    public Wire? FindNearest(Point p, double radius) => AllWires()
        .Select(w => (Wire: w, Distance: w.DistanceTo(p)))
        .Where(x => x.Distance <= radius)
        .OrderBy(x => x.Distance)
        .Select(x => x.Wire)
        .FirstOrDefault();

    /// <summary>
    /// Deletes a wire. If that cuts a net in two, the largest piece keeps the
    /// name and the others become new nets, so they are no longer connected.
    /// </summary>
    public void Delete(Wire wire)
    {
        if (Wires.Remove(wire))
            return;

        var list = Busses.Any(b => b.Wires.Contains(wire)) ? Busses : Nets;
        var net = list.First(n => n.Wires.Contains(wire));
        var piece = Net.ConnectedGroups(net.Wires).First(g => g.Contains(wire));
        net.Wires.Remove(wire);
        piece.Remove(wire);

        if (net.Wires.Count == 0)
        {
            list.Remove(net);
            return;
        }

        var split = Net.ConnectedGroups(piece);
        if (split.Count <= 1 || list == Busses)
            return;

        foreach (var group in split.OrderByDescending(g => g.Count).Skip(1))
        {
            var newNet = new Net { Name = _owner.NextNetName() };
            foreach (var w in group)
                net.Wires.Remove(w);
            newNet.Wires.AddRange(group);
            newNet.Labels.AddRange(net.Labels.Where(l => NearestGroupIs(group, net.Wires, l.Position)));
            net.Labels.RemoveAll(newNet.Labels.Contains);
            Nets.Add(newNet);
        }
    }

    public bool DeleteNearest(Point p, double radius)
    {
        var wire = FindNearest(p, radius);
        if (wire is null)
            return false;
        Delete(wire);
        return true;
    }

    public SheetSnapshot Capture() => new(
        Nets.Select(n => n.Clone()).ToList(),
        Busses.Select(b => b.Clone()).ToList(),
        Wires.ToList());

    public void Restore(SheetSnapshot snapshot)
    {
        Nets.Clear();
        Nets.AddRange(snapshot.Nets.Select(n => n.Clone()));
        Busses.Clear();
        Busses.AddRange(snapshot.Busses.Select(b => b.Clone()));
        Wires.Clear();
        Wires.AddRange(snapshot.Wires);
    }

    private static bool NearestGroupIs(List<Wire> group, List<Wire> others, Point p) =>
        group.Min(w => w.DistanceTo(p)) < others.Min(w => w.DistanceTo(p));

    private static IEnumerable<Wire> Segments(IReadOnlyList<Point> points, double width, int layer)
    {
        for (var i = 1; i < points.Count; i++)
        {
            if (!GeometryMath.SamePoint(points[i - 1], points[i]))
                yield return new Wire { From = points[i - 1], To = points[i], Width = width, Layer = layer };
        }
    }
}

/// <summary>The editable content of a sheet, for undo.</summary>
public sealed record SheetSnapshot(IReadOnlyList<Net> Nets, IReadOnlyList<Net> Busses, IReadOnlyList<Wire> Wires);
