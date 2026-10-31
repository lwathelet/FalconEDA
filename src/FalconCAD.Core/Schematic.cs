namespace FalconCAD.Core;

public sealed class Schematic
{
    public string Name { get; set; } = "untitled";
    public DateTime LastModified { get; set; } = DateTime.Now;

    public List<Layer> Layers { get; } = new();

    /// <summary>Libraries by <see cref="Library.Key"/>.</summary>
    public Dictionary<string, Library> Libraries { get; } = new();
    public Dictionary<string, Part> Parts { get; } = new();

    /// <summary>Global attributes, used by text placeholders such as &gt;AUTHOR.</summary>
    public Dictionary<string, string> Attributes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<Sheet> Sheets { get; } = new();

    public static Schematic CreateNew()
    {
        var schematic = new Schematic();
        schematic.Layers.AddRange(DefaultLayers());
        schematic.AddSheet();
        return schematic;
    }

    public static IEnumerable<Layer> DefaultLayers() => new[]
    {
        new Layer { Number = 91, Name = "Nets", Color = 2 },
        new Layer { Number = 92, Name = "Busses", Color = 1 },
        new Layer { Number = 94, Name = "Symbols", Color = 4 },
        new Layer { Number = 95, Name = "Names", Color = 7 },
        new Layer { Number = 96, Name = "Values", Color = 7 },
        new Layer { Number = 97, Name = "Info", Color = 7 },
        new Layer { Number = 98, Name = "Guide", Color = 6 },
    };

    public Sheet AddSheet()
    {
        var sheet = new Sheet(this);
        Sheets.Add(sheet);
        return sheet;
    }

    /// <summary>The next free automatic net name (N$1, N$2...), unique across all sheets.</summary>
    public string NextNetName()
    {
        var used = Sheets.SelectMany(s => s.Nets).Select(n => n.Name).ToHashSet();
        var i = 1;
        while (used.Contains($"N${i}"))
            i++;
        return $"N${i}";
    }

    /// <summary>Finds the library objects behind an instance, or null if the file references something missing.</summary>
    public (Part Part, DeviceSet DeviceSet, Gate Gate, Symbol Symbol, Device? Device)? Resolve(Instance instance)
    {
        if (!Parts.TryGetValue(instance.Part, out var part)
            || !Libraries.TryGetValue(part.LibraryKey, out var library)
            || !library.DeviceSets.TryGetValue(part.DeviceSet, out var deviceSet)
            || !deviceSet.Gates.TryGetValue(instance.Gate, out var gate)
            || !library.Symbols.TryGetValue(gate.Symbol, out var symbol))
            return null;

        return (part, deviceSet, gate, symbol, deviceSet.Devices.GetValueOrDefault(part.Device));
    }

    /// <summary>The part value shown on the sheet; without a user value Eagle shows the device name.</summary>
    public string ValueOf(Part part) =>
        part.Value ?? part.DeviceSet.Replace("*", part.Technology) + part.Device;
}
