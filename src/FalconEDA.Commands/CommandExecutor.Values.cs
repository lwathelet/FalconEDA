using FalconEDA.Core;

namespace FalconEDA.Commands;

/// <summary>VALUE and CHANGE: a component's list of values and the part properties BOM and DNP (docs/design-decisions.md).</summary>
public static partial class CommandExecutor
{
    /// <summary>
    /// VALUE: click a part to choose its value from the list. VALUE R1 opens the list for R1.
    /// VALUE R1 100K picks 100K from the list; VALUE R1 100K 'manufacturer' 'reference' adds it to the list first.
    /// Parts without a list of values take the typed value, as in Eagle.
    /// </summary>
    private static string Value(Editor editor, Command command)
    {
        var words = command.Arguments.Select(a => a switch
        {
            WordArgument w => w.Value,
            TextArgument t => t.Value,
            NumberArgument n => n.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => throw Unexpected("VALUE", a),
        }).ToList();

        switch (words.Count)
        {
            case 0:
                return StartTool(editor, new PartClickTool(editor, "VALUE: left-click a part to choose its value. Esc ends.",
                    instance => editor.RequestValueDialog(instance.Part)));
            case 1:
                editor.RequestValueDialog(FindPart(editor, words[0]).Name);
                return "";
            case 2:
                return SetValue(editor, FindPart(editor, words[0]), words[1], null, null);
            case 4:
                return SetValue(editor, FindPart(editor, words[0]), words[1], words[2], words[3]);
            default:
                throw new CommandException("VALUE: use VALUE, VALUE R1, VALUE R1 100K or VALUE R1 100K 'manufacturer' 'reference'.");
        }
    }

    /// <summary>
    /// Gives a part a value from its component's list. With a manufacturer and reference, a value that is not in
    /// the list yet is added to it, in the library file and in the schematic.
    /// </summary>
    public static string SetValue(Editor editor, Part part, string val, string? mnf, string? reference)
    {
        var device = DeviceOf(editor.Schematic, part);
        var list = device is null ? [] : Values.Of(device);

        if (device is null || (list.Count == 0 && mnf is null))
        {
            // No list of values: a value typed in, as Eagle's VALUE does.
            Change(editor, part.WithValue(val));
            return "";
        }

        var option = list.FirstOrDefault(v => v.Val.Equals(val, StringComparison.OrdinalIgnoreCase) || v.Technology.Equals(val, StringComparison.OrdinalIgnoreCase));
        if (option is null)
        {
            if (mnf is null || reference is null)
                throw new CommandException($"VALUE: {val} is not in the list of {part.Name}. Add it with VALUE {part.Name} {val} 'manufacturer' 'reference'.");
            option = AddValueToList(editor, part, device, val, mnf, reference);
        }

        Change(editor, part.WithValue(option));
        return $"{part.Name}: {option.Val}" + (option.Mnf.Length > 0 ? $", {option.Mnf}" : "") + (option.Ref.Length > 0 ? $", {option.Ref}" : "");
    }

    /// <summary>Adds a value to the component's list in its library file (if there is one) and in the schematic's copy.</summary>
    private static ValueOption AddValueToList(Editor editor, Part part, Device device, string val, string mnf, string reference)
    {
        var library = editor.Schematic.Libraries[part.LibraryKey];
        var fileDevice = editor.LibraryProvider?.Load(library.Name)?.DeviceSets.GetValueOrDefault(part.DeviceSet)?.Devices.GetValueOrDefault(part.Device);

        ValueOption option;
        if (fileDevice is not null)
        {
            option = Values.Add(fileDevice, val, mnf, reference);
            editor.LibraryProvider!.SaveValues(library.Name, part.DeviceSet, fileDevice);
            if (!ReferenceEquals(fileDevice, device))
                device.Technologies[option.Technology] = new Dictionary<string, string>(fileDevice.Technologies[option.Technology], StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            // The library file is not in the library folders: the value is kept in this schematic only.
            option = Values.Add(device, val, mnf, reference);
        }
        return option;
    }

    /// <summary>
    /// Adds a value to a device's list in its library file, before a part using it exists (ADD with a new value).
    /// <paramref name="library"/> is the library file as the library provider loaded it.
    /// </summary>
    public static ValueOption AddValueToLibrary(Editor editor, Library library, string deviceSet, Device device, string val, string mnf, string reference)
    {
        var option = Values.Add(device, val, mnf, reference);
        editor.LibraryProvider?.SaveValues(library.Name, deviceSet, device);
        return option;
    }

    /// <summary>
    /// CHANGE sets a part property, as Eagle's CHANGE: CHANGE BOM OFF leaves parts out of the bill of materials
    /// (BOM is on by default), CHANGE BOM ON puts them back; CHANGE DNP ON / OFF marks "do not populate".
    /// It applies to the parts named after it (CHANGE BOM OFF R1 R2), else to the group, else to the parts clicked.
    /// MNF and REF are not properties: they belong to the value chosen from the library list (VALUE).
    /// </summary>
    private static string ChangeCommand(Editor editor, Command command)
    {
        if (command.Arguments is not [WordArgument { Value: var property }, WordArgument { Value: var value }, ..])
            throw new CommandException("CHANGE: use CHANGE BOM ON|OFF or CHANGE DNP ON|OFF.");

        var isBom = property.ToUpperInvariant() switch
        {
            "BOM" => true,
            "DNP" => false,
            _ => throw new CommandException($"CHANGE: unknown property {property}; FalconEDA has BOM and DNP."),
        };
        var on = value.ToUpperInvariant() switch
        {
            "ON" => true,
            "OFF" => false,
            _ => throw new CommandException($"CHANGE {property.ToUpperInvariant()}: use ON or OFF."),
        };

        // BOM is kept the other way round: a part out of the BOM carries NO_BOM.
        var attribute = isBom ? Values.NoBom : Values.Dnp;
        string? stored = on != isBom ? "yes" : null;
        var shown = $"{property.ToUpperInvariant()} {value.ToUpperInvariant()}";

        void Apply(IEnumerable<Part> parts)
        {
            editor.Checkpoint();
            foreach (var part in parts)
                editor.Schematic.Parts[part.Name] = part.WithAttribute(attribute, stored);
            editor.Schematic.PartsModified = true;
            editor.NotifyChanged();
        }

        var names = command.Arguments.Skip(2).Select(a => a is WordArgument w ? w.Value : throw Unexpected("CHANGE", a)).ToList();
        if (names.Count > 0)
        {
            Apply(names.Select(name => FindPart(editor, name)).ToList());
            return $"{shown}: {string.Join(", ", names)}";
        }

        var groupParts = editor.Selection.Instances.Select(i => i.Part).Distinct().ToList();
        if (groupParts.Count > 0)
        {
            Apply(groupParts.Select(n => editor.Schematic.Parts[n]).ToList());
            return $"{shown} for {groupParts.Count} part(s)";
        }

        return StartTool(editor, new PartClickTool(editor, $"CHANGE {shown}: left-click the parts. Esc ends.",
            instance => Apply([editor.Schematic.Parts[instance.Part]])));
    }

    private static void Change(Editor editor, Part changed)
    {
        editor.Checkpoint();
        editor.Schematic.Parts[changed.Name] = changed;
        editor.Schematic.PartsModified = true;
        editor.NotifyChanged();
    }

    private static Part FindPart(Editor editor, string name) =>
        editor.Schematic.Parts.Values.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
        ?? throw new CommandException($"There is no part {name}.");

    /// <summary>The device a part uses, from the schematic's copy of its library.</summary>
    public static Device? DeviceOf(Schematic schematic, Part part) =>
        schematic.Libraries.GetValueOrDefault(part.LibraryKey)?.DeviceSets.GetValueOrDefault(part.DeviceSet)?.Devices.GetValueOrDefault(part.Device);
}

/// <summary>A tool that does something with each part clicked: VALUE, CHANGE.</summary>
public sealed class PartClickTool(Editor editor, string prompt, Action<Instance> onClick) : ITool
{
    public string Prompt => prompt;
    public bool SnapToGrid => false;
    public IReadOnlyList<Wire> Preview => Array.Empty<Wire>();

    public void Click(Point p, double pickRadius, int clickCount)
    {
        if (editor.Sheet.InstanceAt(p, pickRadius) is { } instance)
            onClick(instance);
    }

    public void Move(Point p) { }

    public void Cancel() => editor.SetTool(null);
}
