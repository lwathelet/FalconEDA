namespace FalconEDA.Core;

public sealed class Part
{
    public required string Name { get; init; }

    /// <summary>What this part was read from (the file reader's XML), so it can be written back unchanged.</summary>
    public object? Source { get; set; }
    public required string LibraryKey { get; init; }
    public required string DeviceSet { get; init; }
    public string Device { get; init; } = "";
    public string Technology { get; init; } = "";
    public string? Value { get; init; }
    public Dictionary<string, string> Attributes { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A copy under another name, e.g. when pasting a part whose name is already taken.</summary>
    public Part Renamed(string name) => Copy(name, Technology, Value);

    /// <summary>
    /// A copy with another value from the component's list. Its VAL, MNF and REF then come from the chosen
    /// technology, so attributes the part itself had for them are dropped.
    /// </summary>
    public Part WithValue(ValueOption value)
    {
        var copy = Copy(Name, value.Technology, value.Val);
        foreach (var name in new[] { Values.Val, Values.Mnf, Values.Ref })
            copy.Attributes.Remove(name);
        return copy;
    }

    /// <summary>A copy with a value typed in, for parts without a list of values (as Eagle's VALUE).</summary>
    public Part WithValue(string value) => Copy(Name, Technology, value);

    /// <summary>A copy with an attribute set, or removed when <paramref name="value"/> is null.</summary>
    public Part WithAttribute(string name, string? value)
    {
        var copy = Copy(Name, Technology, Value);
        if (value is null)
            copy.Attributes.Remove(name);
        else
            copy.Attributes[name] = value;
        return copy;
    }

    // Parts are replaced rather than changed, so UNDO, which keeps the old part objects, restores them.
    private Part Copy(string name, string technology, string? value)
    {
        var copy = new Part
        {
            Name = name,
            LibraryKey = LibraryKey,
            DeviceSet = DeviceSet,
            Device = Device,
            Technology = technology,
            Value = value,
            Source = Source,
        };
        foreach (var (key, attribute) in Attributes)
            copy.Attributes[key] = attribute;
        return copy;
    }
}

public enum AttributeDisplay { Off, Value, Name, Both }

/// <summary>A part attribute placed on the sheet, used when an instance is "smashed".</summary>
public sealed class PlacedAttribute
{
    public required string Name { get; init; }
    public Point? Position { get; set; }
    public double Size { get; init; } = 1.778;
    public int Layer { get; init; }
    public Rotation Rotation { get; set; }
    public Align Align { get; init; } = Align.BottomLeft;
    public AttributeDisplay Display { get; init; } = AttributeDisplay.Value;

    public PlacedAttribute Clone() => (PlacedAttribute)MemberwiseClone();
}

/// <summary>One gate of a part placed on a sheet.</summary>
public sealed class Instance
{
    public required string Part { get; set; }
    public required string Gate { get; init; }
    public required Point Position { get; set; }
    public Rotation Rotation { get; set; }
    public bool Smashed { get; set; }
    public List<PlacedAttribute> Attributes { get; } = new();

    /// <summary>What this instance was read from (the file reader's XML), so it can be written back unchanged.</summary>
    public object? Source { get; set; }

    public Transform Transform => new(Position, Rotation);

    public Instance Clone()
    {
        var copy = new Instance { Part = Part, Gate = Gate, Position = Position, Rotation = Rotation, Smashed = Smashed, Source = Source };
        copy.Attributes.AddRange(Attributes.Select(a => a.Clone()));
        return copy;
    }
}

public sealed class Label
{
    public required Point Position { get; set; }
    public double Size { get; init; } = 1.778;
    public int Layer { get; init; } = 95;
    public Rotation Rotation { get; set; }
    public Align Align { get; init; } = Align.BottomLeft;
    public bool Xref { get; init; }

    /// <summary>What this label was read from (the file reader's XML), so a moved label can be updated in place.</summary>
    public object? Source { get; set; }

    public Label Clone() => (Label)MemberwiseClone();
}

public sealed record PinRef(string Part, string Gate, string Pin);

/// <summary>A net or a bus on one sheet. Wires with the same net name on other sheets are the same signal.</summary>
public sealed class Net
{
    public required string Name { get; set; }
    public int Class { get; set; }

    /// <summary>What this net was read from (the file reader's XML), so a renamed or edited net can be updated in place.</summary>
    public object? Source { get; set; }
    public List<Wire> Wires { get; } = new();
    public List<Label> Labels { get; } = new();

    /// <summary>Junction dots stored in the file.</summary>
    public List<Point> Junctions { get; } = new();
    public List<PinRef> PinRefs { get; } = new();

    public bool Touches(Point p) => Wires.Any(w => w.DistanceTo(p) < GeometryMath.Epsilon);

    /// <summary>
    /// Points where three or more wire ends meet, counting a wire that passes
    /// through a point as two ends. These get a junction dot.
    /// </summary>
    public IEnumerable<Point> FindJunctions()
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

    public Net Clone()
    {
        var copy = new Net { Name = Name, Class = Class, Source = Source };
        copy.Wires.AddRange(Wires.Select(w => w.Clone()));
        copy.Labels.AddRange(Labels.Select(l => l.Clone()));
        copy.Junctions.AddRange(Junctions);
        copy.PinRefs.AddRange(PinRefs);
        return copy;
    }

    /// <summary>Groups the wires into pieces that are connected to each other.</summary>
    public static List<List<Wire>> ConnectedGroups(IReadOnlyList<Wire> wires)
    {
        var parent = Enumerable.Range(0, wires.Count).ToArray();
        int Find(int i) => parent[i] == i ? i : parent[i] = Find(parent[i]);

        for (var i = 0; i < wires.Count; i++)
        {
            for (var j = i + 1; j < wires.Count; j++)
            {
                if (Connected(wires[i], wires[j]))
                    parent[Find(i)] = Find(j);
            }
        }

        return Enumerable.Range(0, wires.Count)
            .GroupBy(Find)
            .Select(g => g.Select(i => wires[i]).ToList())
            .ToList();
    }

    private static bool Connected(Wire a, Wire b) =>
        a.DistanceTo(b.From) < GeometryMath.Epsilon || a.DistanceTo(b.To) < GeometryMath.Epsilon
        || b.DistanceTo(a.From) < GeometryMath.Epsilon || b.DistanceTo(a.To) < GeometryMath.Epsilon;
}
