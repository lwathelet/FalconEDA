using FalconEDA.Core;

namespace FalconEDA.Commands;

public static partial class CommandExecutor
{
    /// <summary>
    /// ADD R-EU_R0402@rcl: device set and device, then @ and the library file name.
    /// Optional, as in Eagle: a part name in quotes, an orientation (R90, MR180) and a position (x y),
    /// which places one part there. Without a library, the device is looked up in all libraries.
    /// ADD alone opens the library browser.
    /// </summary>
    private static string Add(Editor editor, Command command)
    {
        string? spec = null, name = null, value = null;
        var rotation = default(Rotation);
        Point? at = null;
        foreach (var argument in command.Arguments)
        {
            switch (argument)
            {
                // VALUE=100K chooses the value from the component's list right away.
                case WordArgument or TextArgument when ArgumentText(argument).StartsWith("VALUE=", StringComparison.OrdinalIgnoreCase):
                    value = ArgumentText(argument)["VALUE=".Length..];
                    break;
                case TextArgument t when t.Value.Contains('@'):
                    spec = t.Value;
                    break;
                case TextArgument t:
                    name = t.Value;
                    break;
                // R0402 looks like an orientation (R90) but is a device when a library has it.
                case WordArgument w when spec is null
                    && (w.Value.Contains('@') || editor.LibraryProvider?.LibrariesWith(w.Value).Count > 0):
                    spec = w.Value;
                    break;
                case WordArgument w when OrientationPattern.IsMatch(w.Value):
                    rotation = Core.Rotation.Parse(w.Value.TrimStart('=').ToUpperInvariant());
                    break;
                case WordArgument w when spec is null:
                    spec = w.Value;
                    break;
                case CoordinateArgument c:
                    at = ToMm(editor, c.Value);
                    break;
                default:
                    throw Unexpected("ADD", argument);
            }
        }

        if (spec is null)
        {
            editor.RequestAddDialog(null);
            return "ADD: choose a part.";
        }

        var (library, deviceSet, device, technology) = FindDevice(editor, spec);
        ValueOption? option = null;
        if (value is not null)
        {
            option = Values.Of(deviceSet.Devices[device]).FirstOrDefault(v => v.Val.Equals(value, StringComparison.OrdinalIgnoreCase))
                ?? throw new CommandException($"ADD: {value} is not in the list of values of {deviceSet.Name}{device}.");
        }
        var tool = new AddTool(editor, library, deviceSet, device, technology, name, rotation, option);
        if (at is { } point)
        {
            tool.Click(point, 0, 1);
            return "";
        }
        editor.SetTool(tool);
        return tool.Prompt;
    }

    private static string ArgumentText(Argument argument) => argument switch
    {
        WordArgument w => w.Value,
        TextArgument t => t.Value,
        _ => "",
    };

    /// <summary>The library, device set and device ADD means by R-EU_R0402@rcl; public so the app can show the value list first.</summary>
    public static (Library Library, DeviceSet DeviceSet, string Device, string Technology) FindDevice(Editor editor, string spec)
    {
        var at = spec.LastIndexOf('@');
        var fullName = at < 0 ? spec : spec[..at];
        var libraryName = at < 0 ? null : spec[(at + 1)..];

        if (libraryName is null)
        {
            var found = editor.LibraryProvider?.LibrariesWith(fullName) ?? [];
            libraryName = found.Count switch
            {
                1 => found[0],
                0 => throw new CommandException($"ADD: no device {fullName} in the libraries. Use ADD alone to search."),
                _ => throw new CommandException($"ADD: {fullName} is in {found.Count} libraries ({string.Join(", ", found.Take(5))}). Use ADD {fullName}@library."),
            };
        }

        // The library file first; a library only found inside the schematic still offers what it has.
        var library = editor.LibraryProvider?.Load(libraryName)
            ?? editor.Schematic.Libraries.Values.FirstOrDefault(l => l.Name.Equals(libraryName, StringComparison.OrdinalIgnoreCase))
            ?? throw new CommandException($"ADD: there is no library {libraryName}.");

        // R-EU_R0402 is device set R-EU_ with device R0402; the longest matching device set name wins.
        var match = library.DeviceSets.Values
            .Where(d => fullName.StartsWith(d.Name, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(d => d.Name.Length)
            .Select(d => (DeviceSet: d, Device: DeviceOf(d, fullName[d.Name.Length..])))
            .FirstOrDefault(m => m.Device is not null);
        if (match.Device is null)
            throw new CommandException($"ADD: library {library.Name} has no device {fullName}.");

        var device = match.DeviceSet.Devices[match.Device];
        var technology = device.Technologies.Count == 0 || device.Technologies.ContainsKey("") ? "" : device.Technologies.Keys.First();
        return (library, match.DeviceSet, match.Device, technology);
    }

    /// <summary>The device of <paramref name="deviceSet"/> named <paramref name="rest"/>; with no name given, its only device.</summary>
    private static string? DeviceOf(DeviceSet deviceSet, string rest)
    {
        var device = deviceSet.Devices.Keys.FirstOrDefault(k => k.Equals(rest, StringComparison.OrdinalIgnoreCase));
        if (device is not null)
            return device;
        return rest.Length == 0 && deviceSet.Devices.Count == 1 ? deviceSet.Devices.Keys.First() : null;
    }
}
