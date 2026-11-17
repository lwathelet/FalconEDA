using System.Text.RegularExpressions;

namespace FalconEDA.Core;

/// <summary>One value a component can have: its technology, and the value, manufacturer and reference that go with it.</summary>
public sealed record ValueOption(string Technology, string Val, string Mnf, string Ref);

/// <summary>
/// A component's list of values, kept in the library as Eagle technologies with the attributes VAL, MNF and REF
/// (see docs/design-decisions.md), and the DNP and NO_BOM flags of a part.
/// </summary>
public static class Values
{
    public const string Val = "VAL";
    public const string Mnf = "MNF";
    public const string Ref = "REF";
    public const string Dnp = "DNP";
    public const string NoBom = "NO_BOM";

    /// <summary>The values of a device: every technology with a VAL.</summary>
    public static IReadOnlyList<ValueOption> Of(Device device) => device.Technologies
        .Select(t => new ValueOption(t.Key,
            t.Value.GetValueOrDefault(Val, ""), t.Value.GetValueOrDefault(Mnf, ""), t.Value.GetValueOrDefault(Ref, "")))
        .Where(v => v.Val.Length > 0)
        .ToList();

    /// <summary>The technology name for a new value: the value without characters Eagle does not take in names, then "_" (100K becomes 100K_).</summary>
    public static string TechnologyName(Device device, string val)
    {
        var name = Regex.Replace(val.ToUpperInvariant(), @"[^A-Z0-9.,+\-]", "") + "_";
        var unique = name;
        for (var i = 2; device.Technologies.ContainsKey(unique); i++)
            unique = $"{name}{i}";
        return unique;
    }

    /// <summary>
    /// Adds a value to a device and returns its technology. The new technology has the same attributes as the
    /// device's others (PACKAGE, BREF...), with VAL, MNF and REF filled in.
    /// </summary>
    public static ValueOption Add(Device device, string val, string mnf, string reference)
    {
        if (Of(device).Any(v => v.Val.Equals(val, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"The value {val} is already in the list.", nameof(val));

        var template = device.Technologies.GetValueOrDefault("") ?? device.Technologies.Values.FirstOrDefault();
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in template ?? new Dictionary<string, string>())
            attributes[name] = name.Equals("PACKAGE", StringComparison.OrdinalIgnoreCase) ? value : "";
        attributes[Val] = val;
        attributes[Mnf] = mnf;
        attributes[Ref] = reference;

        var technology = TechnologyName(device, val);
        device.Technologies[technology] = attributes;
        return new ValueOption(technology, val, mnf, reference);
    }

    public static bool IsDnp(Part part) => IsSet(part, Dnp);

    public static bool IsNoBom(Part part) => IsSet(part, NoBom);

    private static bool IsSet(Part part, string flag) =>
        part.Attributes.TryGetValue(flag, out var value) && value.Equals("yes", StringComparison.OrdinalIgnoreCase);
}
