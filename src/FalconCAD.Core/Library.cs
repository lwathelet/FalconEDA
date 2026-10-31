namespace FalconCAD.Core;

public sealed class Symbol
{
    public required string Name { get; init; }
    public Graphics Graphics { get; } = new();
    public List<Pin> Pins { get; } = new();
}

/// <summary>One symbol of a device set; a device set with several gates is, for example, a quad op-amp.</summary>
public sealed class Gate
{
    public required string Name { get; init; }
    public required string Symbol { get; init; }
}

public sealed class Device
{
    public required string Name { get; init; }

    /// <summary>Pad names by (gate, pin).</summary>
    public Dictionary<(string Gate, string Pin), string> Connects { get; } = new();

    /// <summary>Attributes per technology, e.g. VAL = "120R" for technology "120R_".</summary>
    public Dictionary<string, Dictionary<string, string>> Technologies { get; } = new();
}

public sealed class DeviceSet
{
    public required string Name { get; init; }
    public string Prefix { get; init; } = "";
    public bool UserValue { get; init; }
    public Dictionary<string, Gate> Gates { get; } = new();
    public Dictionary<string, Device> Devices { get; } = new();
}

public sealed class Library
{
    public required string Name { get; init; }
    public string Urn { get; init; } = "";
    public Dictionary<string, Symbol> Symbols { get; } = new();
    public Dictionary<string, DeviceSet> DeviceSets { get; } = new();

    /// <summary>Libraries in a schematic are identified by name and URN together.</summary>
    public static string Key(string name, string urn) => urn.Length == 0 ? name : $"{name}|{urn}";
}
