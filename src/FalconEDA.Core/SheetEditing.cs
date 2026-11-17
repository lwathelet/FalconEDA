using System.Text.RegularExpressions;

namespace FalconEDA.Core;

/// <summary>Selecting, moving, rotating, mirroring, deleting, copying and pasting objects on a sheet.</summary>
public sealed partial class Sheet
{
    public Rect? Bounds(Instance instance) =>
        _owner.Resolve(instance)?.Symbol is { } symbol ? Symbols.Bounds(symbol, instance.Transform) : null;

    public IEnumerable<Wire> Outline(Instance instance) =>
        _owner.Resolve(instance)?.Symbol is { } symbol ? Symbols.Outline(symbol, instance.Transform) : [];

    /// <summary>The part under <paramref name="p"/>. Where symbols overlap (a part inside a drawing frame), the smallest wins.</summary>
    public Instance? InstanceAt(Point p, double radius) => Instances
        .Select(i => (Instance: i, Bounds: Bounds(i)))
        .Where(x => x.Bounds is { } b
            && p.X >= b.Min.X - radius && p.X <= b.Max.X + radius
            && p.Y >= b.Min.Y - radius && p.Y <= b.Max.Y + radius)
        .OrderBy(x => x.Bounds!.Value.Width * x.Bounds.Value.Height)
        .Select(x => x.Instance)
        .FirstOrDefault();

    public Label? LabelAt(Point p, double radius) => Nets.Concat(Busses)
        .SelectMany(n => n.Labels)
        .Where(l => l.Position.DistanceTo(p) <= Math.Max(radius, l.Size))
        .MinBy(l => l.Position.DistanceTo(p));

    /// <summary>The single object a click on <paramref name="p"/> means: a label, a wire, or a part.</summary>
    public Selection SelectAt(Point p, double radius)
    {
        var selection = new Selection();
        var text = TextAt(p, radius);
        if (text is Label label)
            selection.Labels.Add(label);
        else if (text is PlacedAttribute attribute)
            selection.Attributes.Add(attribute);
        else if (FindNearest(p, radius) is { } wire)
            selection.AddWhole(wire);
        else if (InstanceAt(p, radius) is { } instance)
            selection.Instances.Add(instance);
        return selection;
    }

    /// <summary>
    /// What MOVE picks up at <paramref name="p"/>. Near a wire end it takes that corner, elsewhere on a
    /// wire the whole segment; the wires joined at the moved corners follow, as in Eagle.
    /// </summary>
    public Selection SelectForMove(Point p, double radius)
    {
        var selection = new Selection();
        switch (TextAt(p, radius))
        {
            case Label label:
                selection.Labels.Add(label);
                return selection;
            case PlacedAttribute attribute:
                // A smashed name or value moves on its own, as in Eagle.
                selection.Attributes.Add(attribute);
                return selection;
        }
        if (FindNearest(p, radius) is { } wire)
        {
            var corners = wire.From.DistanceTo(p) <= radius ? new[] { wire.From }
                : wire.To.DistanceTo(p) <= radius ? new[] { wire.To }
                : new[] { wire.From, wire.To };
            foreach (var w in AllWires())
            {
                if (corners.Any(c => GeometryMath.SamePoint(c, w.From)))
                    selection.WireEnds.Add((w, false));
                if (corners.Any(c => GeometryMath.SamePoint(c, w.To)))
                    selection.WireEnds.Add((w, true));
            }
            return selection;
        }
        if (InstanceAt(p, radius) is { } instance)
            selection.Instances.Add(instance);
        return selection;
    }

    /// <summary>Everything inside the rectangle: parts by their origin, wires by their ends, labels by position.</summary>
    public Selection SelectIn(Rect rect)
    {
        bool Inside(Point p) => p.X >= rect.Min.X && p.X <= rect.Max.X && p.Y >= rect.Min.Y && p.Y <= rect.Max.Y;

        var selection = new Selection();
        selection.Instances.UnionWith(Instances.Where(i => Inside(i.Position)));
        foreach (var wire in AllWires())
        {
            if (Inside(wire.From))
                selection.WireEnds.Add((wire, false));
            if (Inside(wire.To))
                selection.WireEnds.Add((wire, true));
        }
        selection.Labels.UnionWith(Nets.Concat(Busses).SelectMany(n => n.Labels).Where(l => Inside(l.Position)));
        return selection;
    }

    public Selection SelectAll()
    {
        var selection = new Selection();
        selection.Instances.UnionWith(Instances);
        foreach (var wire in AllWires())
            selection.AddWhole(wire);
        selection.Labels.UnionWith(Nets.Concat(Busses).SelectMany(n => n.Labels));
        return selection;
    }

    public void Move(Selection selection, Point delta) =>
        Transform(selection, p => p + delta, r => r, mirror: false);

    public void Rotate90(Selection selection, Point pivot) =>
        Transform(selection, p => GeometryMath.Rotate90(p, pivot), r => r.Rotated90(), mirror: false);

    public void Mirror(Selection selection, Point pivot) =>
        Transform(selection, p => GeometryMath.MirrorX(p, pivot), r => r.Mirrored(), mirror: true);

    /// <summary>
    /// Applies a move, rotation or mirror to the selection. Net wires that end on a pin of a moved part
    /// but are not selected themselves stretch to follow the pin ("rubber band"), so connections are kept.
    /// </summary>
    private void Transform(Selection selection, Func<Point, Point> map, Func<Rotation, Rotation> rotate, bool mirror)
    {
        var pinsBefore = PinsOf(selection.Instances);
        var moving = selection.Instances.Select(i => (i.Part, i.Gate)).ToHashSet();
        var stationaryPins = Pins().Where(x => !moving.Contains((x.Pin.Part, x.Pin.Gate))).ToList();

        foreach (var instance in selection.Instances)
        {
            instance.Position = map(instance.Position);
            instance.Rotation = rotate(instance.Rotation);
            foreach (var attribute in instance.Attributes.Where(a => a.Position is not null))
            {
                attribute.Position = map(attribute.Position!.Value);
                attribute.Rotation = rotate(attribute.Rotation);
            }
        }

        var movedCorners = new List<(Point Before, Point After)>();
        foreach (var (wire, end) in selection.WireEnds)
        {
            var before = end ? wire.To : wire.From;
            var after = map(before);
            movedCorners.Add((before, after));
            if (end)
                wire.To = after;
            else
                wire.From = after;
        }
        if (mirror)
        {
            // Mirroring reverses the direction an arc turns.
            foreach (var wire in selection.WholeWires)
                wire.Curve = -wire.Curve;
        }

        foreach (var label in selection.Labels)
        {
            label.Position = map(label.Position);
            label.Rotation = rotate(label.Rotation);
        }

        // Texts picked on their own; those of selected parts already moved with them.
        var ofMovedParts = selection.Instances.SelectMany(i => i.Attributes).ToHashSet();
        foreach (var attribute in selection.Attributes.Where(a => !ofMovedParts.Contains(a) && a.Position is not null))
        {
            attribute.Position = map(attribute.Position!.Value);
            attribute.Rotation = rotate(attribute.Rotation);
        }

        // Only wires of a net the pin is connected to follow it; another net's wire that merely ends there stays.
        var pinsAfter = PinsOf(selection.Instances);
        foreach (var net in Nets)
        {
            var netPins = pinsBefore.Where(p => net.PinRefs.Contains(p.Key)).ToList();
            if (netPins.Count == 0)
                continue;
            foreach (var wire in net.Wires)
            {
            foreach (var end in new[] { false, true })
            {
                if (selection.WireEnds.Contains((wire, end)))
                    continue;
                var point = end ? wire.To : wire.From;
                foreach (var (pin, before) in netPins)
                {
                    if (!GeometryMath.SamePoint(point, before) || !pinsAfter.TryGetValue(pin, out var after))
                        continue;
                    if (end)
                        wire.To = after;
                    else
                        wire.From = after;
                    movedCorners.Add((before, after));
                    break;
                }
            }
            }
        }

        // A moved pin that touched a pin staying in place (a supply symbol on a chip's pin) would lose the
        // connection; as in Eagle, a net wire is drawn from where the pins met to where the moved pin is now.
        foreach (var (pin, before) in pinsBefore)
        {
            if (!pinsAfter.TryGetValue(pin, out var after) || GeometryMath.SamePoint(before, after)
                || Nets.FirstOrDefault(n => n.PinRefs.Contains(pin)) is not { } net
                || !stationaryPins.Any(s => net.PinRefs.Contains(s.Pin) && GeometryMath.SamePoint(s.Position, before)))
                continue;
            net.Wires.Add(new Wire { From = before, To = after, Width = DefaultNetWidth, Layer = NetLayer });
        }

        // Junction dots stored in the file move with the corner they sit on, unless wires still meet where it was.
        foreach (var net in Nets)
        {
            for (var i = 0; i < net.Junctions.Count; i++)
            {
                var junction = net.Junctions[i];
                var corner = movedCorners.FirstOrDefault(c => GeometryMath.SamePoint(c.Before, junction));
                if (corner != default
                    && !net.Wires.Any(w => GeometryMath.SamePoint(w.From, junction) || GeometryMath.SamePoint(w.To, junction)))
                    net.Junctions[i] = corner.After;
            }

            // A wire shrunk to nothing (a part moved back onto the pin it came from) is gone.
            net.Wires.RemoveAll(w => GeometryMath.SamePoint(w.From, w.To));
        }

        Modified = true;
    }

    private Dictionary<PinRef, Point> PinsOf(IEnumerable<Instance> instances)
    {
        var pins = new Dictionary<PinRef, Point>();
        foreach (var instance in instances)
        {
            if (_owner.Resolve(instance)?.Symbol is not { } symbol)
                continue;
            foreach (var pin in symbol.Pins)
                pins[new PinRef(instance.Part, instance.Gate, pin.Name)] = instance.Transform.Apply(pin.Position);
        }
        return pins;
    }

    /// <summary>
    /// Brings pin connections up to date: a pin no wire touches any more is disconnected. When placing parts
    /// (ADD, PASTE), a free pin at the end of a net wire is also connected to that net; moving, rotating and
    /// mirroring do not make new connections, as in Eagle.
    /// </summary>
    public void RefreshPins(bool connectFreePins = false)
    {
        var pins = Pins().ToList();
        var position = pins.ToDictionary(p => p.Pin, p => p.Position);

        foreach (var net in Nets)
        {
            var kept = net.PinRefs.Where(r => !position.TryGetValue(r, out var p)
                || net.Touches(p)
                || net.PinRefs.Any(o => o != r && position.TryGetValue(o, out var q) && GeometryMath.SamePoint(p, q))).ToList();
            net.PinRefs.Clear();
            net.PinRefs.AddRange(kept);
        }

        if (!connectFreePins)
            return;

        var connected = Nets.SelectMany(n => n.PinRefs).ToHashSet();
        foreach (var (pin, p) in pins.Where(x => !connected.Contains(x.Pin)))
        {
            var net = Nets.FirstOrDefault(n => n.Wires.Any(w => GeometryMath.SamePoint(w.From, p) || GeometryMath.SamePoint(w.To, p)));
            net?.PinRefs.Add(pin);
        }
    }

    /// <summary>Deletes the group: parts, labels and wires with both ends selected.</summary>
    public void Delete(Selection selection)
    {
        foreach (var wire in selection.WholeWires.ToList())
            Delete(wire);

        foreach (var label in selection.Labels)
            foreach (var net in Nets.Concat(Busses))
                net.Labels.Remove(label);

        foreach (var instance in selection.Instances)
        {
            Instances.Remove(instance);
            foreach (var net in Nets)
                net.PinRefs.RemoveAll(r => r.Part == instance.Part && r.Gate == instance.Gate);

            // A part with no gate left on any sheet is removed from the schematic.
            if (!_owner.Sheets.Any(s => s.Instances.Any(i => i.Part == instance.Part)))
                _owner.Parts.Remove(instance.Part);
        }

        Nets.RemoveAll(n => n.Wires.Count == 0 && n.PinRefs.Count < 2);
        Modified = true;
    }

    /// <summary>Copies the parts, the wires with both ends selected and their labels and pin connections.</summary>
    public ClipboardContent Copy(Selection selection, Point reference)
    {
        var content = new ClipboardContent { Reference = reference };

        foreach (var instance in selection.Instances)
        {
            content.Instances.Add(instance.Clone());
            if (_owner.Parts.TryGetValue(instance.Part, out var part) && !content.Parts.Contains(part))
            {
                content.Parts.Add(part);
                if (_owner.Libraries.TryGetValue(part.LibraryKey, out var library))
                    content.Libraries[part.LibraryKey] = library;
            }
        }

        var whole = selection.WholeWires.ToHashSet();
        var copiedGates = selection.Instances.Select(i => (i.Part, i.Gate)).ToHashSet();
        var pinPositions = PinsOf(selection.Instances);
        // Busses are not copied yet.
        foreach (var net in Nets)
        {
            var wires = net.Wires.Where(whole.Contains).Select(w => w.Clone()).ToList();
            if (wires.Count == 0)
                continue;
            var copy = new Net { Name = net.Name, Class = net.Class };
            copy.Wires.AddRange(wires);
            copy.Labels.AddRange(net.Labels.Where(selection.Labels.Contains).Select(l => l.Clone()));
            copy.PinRefs.AddRange(net.PinRefs.Where(r => copiedGates.Contains((r.Part, r.Gate))
                && pinPositions.TryGetValue(r, out var p) && copy.Touches(p)));
            content.Nets.Add(copy);
        }

        content.Wires.AddRange(Wires.Where(whole.Contains).Select(w => w.Clone()));
        return content;
    }

    /// <summary>
    /// Pastes with the content's reference point at <paramref name="at"/> and returns what was added.
    /// Parts whose names are taken are renumbered (R1 becomes the next free R), automatic net names
    /// (N$...) get new numbers, and named nets such as GND join the net of that name.
    /// </summary>
    public Selection Paste(ClipboardContent content, Point at)
    {
        var delta = at - content.Reference;
        var pasted = new Selection();

        foreach (var (key, library) in content.Libraries)
        {
            if (!_owner.Libraries.TryAdd(key, library))
                AddMissing(_owner.Libraries[key], library, content.Parts.Where(p => p.LibraryKey == key));
        }

        var names = new Dictionary<string, string>();
        foreach (var part in content.Parts)
        {
            var name = _owner.Parts.ContainsKey(part.Name) ? NextPartName(part) : part.Name;
            names[part.Name] = name;
            _owner.Parts[name] = part.Renamed(name);
        }

        foreach (var original in content.Instances)
        {
            var instance = original.Clone();
            instance.Part = names.GetValueOrDefault(original.Part, original.Part);
            instance.Position += delta;
            foreach (var attribute in instance.Attributes.Where(a => a.Position is not null))
                attribute.Position += delta;
            Instances.Add(instance);
            pasted.Instances.Add(instance);
        }

        foreach (var original in content.Nets)
        {
            var name = original.Name.StartsWith("N$", StringComparison.Ordinal) ? _owner.NextNetName() : original.Name;
            var net = Nets.FirstOrDefault(n => n.Name == name);
            if (net is null)
            {
                net = new Net { Name = name, Class = original.Class };
                Nets.Add(net);
            }
            foreach (var wire in original.Wires.Select(w => w.Clone()))
            {
                // A copy is a new wire: it has no XML of its own yet.
                wire.Source = null;
                wire.From += delta;
                wire.To += delta;
                net.Wires.Add(wire);
                pasted.AddWhole(wire);
            }
            foreach (var label in original.Labels.Select(l => l.Clone()))
            {
                label.Source = null;
                label.Position += delta;
                net.Labels.Add(label);
                pasted.Labels.Add(label);
            }
            net.PinRefs.AddRange(original.PinRefs.Select(r => r with { Part = names.GetValueOrDefault(r.Part, r.Part) }));
        }

        foreach (var wire in content.Wires.Select(w => w.Clone()))
        {
            wire.Source = null;
            wire.From += delta;
            wire.To += delta;
            Wires.Add(wire);
            pasted.AddWhole(wire);
        }

        RefreshPins(connectFreePins: true);
        Modified = true;
        return pasted;
    }

    /// <summary>
    /// The schematic already has a library of this name, maybe another version of it: adds the device sets
    /// and symbols the pasted parts need that it lacks. Where both have a device set, the schematic's own is kept.
    /// </summary>
    private static void AddMissing(Library target, Library source, IEnumerable<Part> parts)
    {
        foreach (var part in parts)
        {
            if (target.DeviceSets.ContainsKey(part.DeviceSet) || !source.DeviceSets.TryGetValue(part.DeviceSet, out var deviceSet))
                continue;
            target.DeviceSets[deviceSet.Name] = deviceSet;
            foreach (var gate in deviceSet.Gates.Values)
            {
                if (!target.Symbols.ContainsKey(gate.Symbol) && source.Symbols.TryGetValue(gate.Symbol, out var symbol))
                    target.Symbols[symbol.Name] = symbol;
            }
        }
    }

    private string NextPartName(Part part)
    {
        var prefix = _owner.Libraries.TryGetValue(part.LibraryKey, out var library)
            && library.DeviceSets.TryGetValue(part.DeviceSet, out var deviceSet) && deviceSet.Prefix.Length > 0
            ? deviceSet.Prefix
            : Regex.Match(part.Name, @"^\D*").Value;
        return _owner.NextPartName(prefix.Length > 0 ? prefix : part.Name + "_");
    }
}
