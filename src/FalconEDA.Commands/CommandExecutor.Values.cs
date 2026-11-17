using FalconEDA.Core;

namespace FalconEDA.Commands;

/// <summary>VALUE, DNP and NO_BOM: a component's list of values and the part flags (docs/design-decisions.md).</summary>
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

    /// <summary>DNP R1 switches "do not populate" for R1 (DNP R1 ON / OFF sets it); DNP alone: click the parts.</summary>
    private static string Dnp(Editor editor, Command command) => Flag(editor, command, Values.Dnp);

    /// <summary>NO_BOM R1 leaves R1 out of the bill of materials, or puts it back; NO_BOM alone: click the parts.</summary>
    private static string NoBom(Editor editor, Command command) => Flag(editor, command, Values.NoBom);

    private static string Flag(Editor editor, Command command, string flag)
    {
        var groupParts = editor.Selection.Instances.Select(i => i.Part).Distinct().ToList();
        if (command.Arguments.Count == 0 && groupParts.Count == 0)
        {
            return StartTool(editor, new PartClickTool(editor, $"{flag}: left-click a part to switch it on or off. Esc ends.",
                instance => SetFlag(editor, editor.Schematic.Parts[instance.Part], flag, null)));
        }

        bool? state = null;
        var names = new List<string>();
        foreach (var argument in command.Arguments)
        {
            switch (argument)
            {
                case WordArgument { Value: var onWord } when onWord.Equals("ON", StringComparison.OrdinalIgnoreCase):
                    state = true;
                    break;
                case WordArgument { Value: var offWord } when offWord.Equals("OFF", StringComparison.OrdinalIgnoreCase):
                    state = false;
                    break;
                case WordArgument w:
                    names.Add(w.Value);
                    break;
                default:
                    throw Unexpected(flag, argument);
            }
        }
        if (names.Count > 0)
            return string.Join(", ", names.Select(n => SetFlag(editor, FindPart(editor, n), flag, state)));

        // No names: the parts of the group. Switching turns them all on, or all off if they all have it already.
        var parts = groupParts.Select(n => editor.Schematic.Parts[n]).ToList();
        var on = state ?? !parts.All(p => p.Attributes.TryGetValue(flag, out var v) && v.Equals("yes", StringComparison.OrdinalIgnoreCase));
        editor.Checkpoint();
        foreach (var part in parts)
            editor.Schematic.Parts[part.Name] = part.WithAttribute(flag, on ? "yes" : null);
        editor.Schematic.PartsModified = true;
        editor.NotifyChanged();
        return $"{flag} {(on ? "on" : "off")} for {parts.Count} part(s)";
    }

    /// <summary>Sets or clears a flag on a part; with <paramref name="state"/> null it switches it.</summary>
    private static string SetFlag(Editor editor, Part part, string flag, bool? state)
    {
        var on = state ?? !(part.Attributes.TryGetValue(flag, out var value) && value.Equals("yes", StringComparison.OrdinalIgnoreCase));
        Change(editor, part.WithAttribute(flag, on ? "yes" : null));
        return $"{part.Name}: {flag} {(on ? "on" : "off")}";
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

/// <summary>A tool that does something with each part clicked: VALUE, DNP, NO_BOM.</summary>
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
