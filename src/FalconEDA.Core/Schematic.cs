namespace FalconEDA.Core;

public sealed partial class Schematic
{
    public string Name { get; set; } = "untitled";
    public DateTime LastModified { get; set; } = DateTime.Now;

    public List<Layer> Layers { get; } = new();

    /// <summary>True when a part changed (its value, DNP, BOM) without anything on a sheet changing.</summary>
    public bool PartsModified { get; set; }

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

    /// <summary>Where a pin connects on the given sheet, or null if its instance is not on that sheet.</summary>
    public Point? PinPosition(Sheet sheet, PinRef pinRef)
    {
        var instance = sheet.Instances.FirstOrDefault(i => i.Part == pinRef.Part && i.Gate == pinRef.Gate);
        if (instance is null || Resolve(instance)?.Symbol is not { } symbol)
            return null;
        var pin = symbol.Pins.FirstOrDefault(p => p.Name == pinRef.Pin);
        return pin is null ? null : instance.Transform.Apply(pin.Position);
    }

    /// <summary>
    /// Adds a new part using <paramref name="deviceSet"/> from a library file. Only the device set and its
    /// symbols are copied into the schematic's library of that name, as Eagle does; if the schematic already has
    /// that device set, its own version is used. Layers the library defines and the schematic lacks are added.
    /// </summary>
    public Part AddPart(Library from, DeviceSet deviceSet, string device, string technology, string? name = null)
    {
        var key = Library.Key(from.Name, from.Urn);
        if (!Libraries.TryGetValue(key, out var library))
        {
            library = new Library { Name = from.Name, Urn = from.Urn };
            Libraries[key] = library;
        }
        if (!library.DeviceSets.ContainsKey(deviceSet.Name))
        {
            library.DeviceSets[deviceSet.Name] = deviceSet;
            foreach (var gate in deviceSet.Gates.Values)
            {
                if (!library.Symbols.ContainsKey(gate.Symbol) && from.Symbols.TryGetValue(gate.Symbol, out var symbol))
                    library.Symbols[symbol.Name] = symbol;
            }
        }

        foreach (var layer in from.Layers.Where(l => Layers.All(o => o.Number != l.Number)))
        {
            // Modules, Pins, Guide and SpiceOrder are hidden in schematics, whatever the library file says.
            var copy = new Layer { Number = layer.Number, Name = layer.Name, Color = layer.Color, Visible = layer.Visible && layer.Number is not (90 or 93 or 98 or 99) };
            var index = Layers.FindIndex(o => o.Number > layer.Number);
            Layers.Insert(index < 0 ? Layers.Count : index, copy);
        }

        if (name is not null && Parts.ContainsKey(name))
            throw new ArgumentException($"There is already a part called {name}.", nameof(name));
        var part = new Part
        {
            Name = name ?? NextPartName(library.DeviceSets[deviceSet.Name].Prefix),
            LibraryKey = key,
            DeviceSet = deviceSet.Name,
            Device = device,
            Technology = technology,
        };
        Parts[part.Name] = part;
        return part;
    }

    /// <summary>The first free name with this prefix: R1, R2... Without a prefix, U$1, U$2...</summary>
    public string NextPartName(string prefix)
    {
        if (prefix.Length == 0)
            prefix = "U$";
        var i = 1;
        while (Parts.ContainsKey($"{prefix}{i}"))
            i++;
        return $"{prefix}{i}";
    }

    /// <summary>The part value shown on the sheet; without a user value Eagle shows the device name.</summary>
    public string ValueOf(Part part) =>
        part.Value ?? part.DeviceSet.Replace("*", part.Technology) + part.Device;
}
