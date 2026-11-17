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
    public Part Renamed(string name)
    {
        var copy = new Part
        {
            Name = name,
            LibraryKey = LibraryKey,
            DeviceSet = DeviceSet,
            Device = Device,
            Technology = Technology,
            Value = Value,
            Source = Source,
        };
        foreach (var (key, value) in Attributes)
            copy.Attributes[key] = value;
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
    public bool Smashed { get; init; }
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

    public Label Clone() => (Label)MemberwiseClone();
}

public sealed record PinRef(string Part, string Gate, string Pin);

/// <summary>A net or a bus on one sheet. Wires with the same net name on other sheets are the same signal.</summary>
public sealed class Net
{
    public required string Name { get; set; }
    public int Class { get; set; }
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
        var copy = new Net { Name = Name, Class = Class };
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
