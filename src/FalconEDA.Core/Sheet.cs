namespace FalconEDA.Core;

public sealed partial class Sheet
{
    public const int NetLayer = 91;
    public const int BusLayer = 92;
    public const int SymbolLayer = 94;
    public const double DefaultNetWidth = 0.1524;

    private readonly Schematic _owner;

    internal Sheet(Schematic owner) => _owner = owner;

    public Graphics Plain { get; } = new();
    public List<Instance> Instances { get; } = new();
    public List<Net> Nets { get; } = new();
    public List<Net> Busses { get; } = new();

    /// <summary>True once the drawing on this sheet has changed; unchanged sheets are saved exactly as they were read.</summary>
    public bool Modified { get; set; }

    /// <summary>Graphic wires that are not part of a net.</summary>
    public List<Wire> Wires => Plain.Wires;

    public IEnumerable<Wire> AllWires() =>
        Wires.Concat(Nets.SelectMany(n => n.Wires)).Concat(Busses.SelectMany(b => b.Wires));

    /// <summary>Every pin of every part on this sheet, with where it connects.</summary>
    public IEnumerable<(PinRef Pin, Point Position)> Pins()
    {
        foreach (var instance in Instances)
        {
            if (_owner.Resolve(instance)?.Symbol is not { } symbol)
                continue;
            foreach (var pin in symbol.Pins)
                yield return (new PinRef(instance.Part, instance.Gate, pin.Name), instance.Transform.Apply(pin.Position));
        }
    }

    public IEnumerable<PinRef> PinsAt(Point p) =>
        Pins().Where(x => GeometryMath.SamePoint(x.Position, p)).Select(x => x.Pin);

    /// <summary>
    /// Adds a net polyline. If either end lands on an existing net, the new
    /// segments join that net, and nets touched at both ends are merged.
    /// Pins under any corner of the polyline are connected to the net.
    /// </summary>
    public Net AddNet(IReadOnlyList<Point> points, double width = DefaultNetWidth, string? name = null)
    {
        if (points.Count < 2)
            throw new ArgumentException("A net needs at least two points.", nameof(points));

        var pins = points.SelectMany(PinsAt).Distinct().ToList();

        // A pin that is already on a net brings that net in too, even where no wire touches.
        var touched = Nets.Where(n => n.Touches(points[0]) || n.Touches(points[^1]) || n.PinRefs.Any(pins.Contains)).ToList();
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
        target.PinRefs.AddRange(pins.Where(p => !target.PinRefs.Contains(p)));
        Modified = true;
        return target;
    }

    public void AddWire(IReadOnlyList<Point> points, double width, int layer)
    {
        if (points.Count < 2)
            throw new ArgumentException("A wire needs at least two points.", nameof(points));
        // Eagle rejects a file with plain wires on the Nets or Busses layer; those layers belong to nets and busses.
        if (layer is NetLayer or BusLayer)
            throw new ArgumentException($"Layer {layer} is only for nets and busses.", nameof(layer));
        Wires.AddRange(Segments(points, width, layer));
        Modified = true;
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
    /// Pins follow the piece they touch; a pin left without a wire is disconnected.
    /// </summary>
    public void Delete(Wire wire)
    {
        Modified = true;
        if (Wires.Remove(wire))
            return;

        var list = Busses.Any(b => b.Wires.Contains(wire)) ? Busses : Nets;
        var net = list.First(n => n.Wires.Contains(wire));
        var piece = Net.ConnectedGroups(net.Wires).First(g => g.Contains(wire));
        net.Wires.Remove(wire);
        piece.Remove(wire);

        var newNets = new List<Net>();
        if (list == Nets)
        {
            foreach (var group in Net.ConnectedGroups(piece).OrderByDescending(g => g.Count).Skip(1))
            {
                var newNet = new Net { Name = _owner.NextNetName(), Class = net.Class };
                foreach (var w in group)
                    net.Wires.Remove(w);
                newNet.Wires.AddRange(group);
                newNet.Labels.AddRange(net.Labels.Where(l => NearestGroupIs(group, net.Wires, l.Position)));
                net.Labels.RemoveAll(newNet.Labels.Contains);
                Nets.Add(newNet);
                newNets.Add(newNet);
            }
            ReassignPins(net, newNets);
        }

        // Two pins joined directly still make a net without any wire.
        if (net.Wires.Count == 0 && net.PinRefs.Count < 2)
            list.Remove(net);
    }

    private void ReassignPins(Net net, List<Net> newNets)
    {
        var pins = net.PinRefs.Select(r => (Ref: r, Position: _owner.PinPosition(this, r))).ToList();
        net.PinRefs.Clear();
        var candidates = newNets.Prepend(net).ToList();

        foreach (var (pinRef, position) in pins)
        {
            if (position is not { } p)
            {
                net.PinRefs.Add(pinRef);
                continue;
            }

            if (candidates.FirstOrDefault(n => n.Touches(p)) is { } owner)
                owner.PinRefs.Add(pinRef);
            else if (pins.Any(o => o.Ref != pinRef && o.Position is { } q && GeometryMath.SamePoint(p, q)))
                net.PinRefs.Add(pinRef);
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
        Wires.Select(w => w.Clone()).ToList(),
        Instances.Select(i => i.Clone()).ToList(),
        _owner.Parts.Values.ToList(),
        _owner.Libraries.ToList());

    public void Restore(SheetSnapshot snapshot)
    {
        Modified = true;
        Nets.Clear();
        Nets.AddRange(snapshot.Nets.Select(n => n.Clone()));
        Busses.Clear();
        Busses.AddRange(snapshot.Busses.Select(b => b.Clone()));
        Wires.Clear();
        Wires.AddRange(snapshot.Wires.Select(w => w.Clone()));
        Instances.Clear();
        Instances.AddRange(snapshot.Instances.Select(i => i.Clone()));

        // Parts and libraries belong to the whole schematic, but only change together with the sheet being edited.
        _owner.Parts.Clear();
        foreach (var part in snapshot.Parts)
            _owner.Parts[part.Name] = part;
        _owner.Libraries.Clear();
        foreach (var (key, library) in snapshot.Libraries)
            _owner.Libraries[key] = library;
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
public sealed record SheetSnapshot(
    IReadOnlyList<Net> Nets,
    IReadOnlyList<Net> Busses,
    IReadOnlyList<Wire> Wires,
    IReadOnlyList<Instance> Instances,
    IReadOnlyList<Part> Parts,
    IReadOnlyList<KeyValuePair<string, Library>> Libraries);
