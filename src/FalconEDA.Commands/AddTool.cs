using FalconEDA.Core;

namespace FalconEDA.Commands;

/// <summary>Where ADD finds library files. The app provides it from the library folders and their index.</summary>
public interface ILibraryProvider
{
    /// <summary>Loads the library file with this name (without .lbr), or returns null if there is none.</summary>
    Library? Load(string name);

    /// <summary>The libraries that have a device with this full name (device set and device, e.g. R-EU_R0402).</summary>
    IReadOnlyList<string> LibrariesWith(string fullName);

    /// <summary>Writes the technologies (values) of <paramref name="device"/> that the library file lacks into it.</summary>
    void SaveValues(string library, string deviceSet, Device device);
}

/// <summary>
/// ADD: the new part follows the mouse; right-click turns it, a click places it. A part with several gates
/// places them one after another; then the next part of the same kind follows, until Esc.
/// </summary>
public sealed class AddTool : ITool
{
    private readonly Editor _editor;
    private readonly Library _library;
    private readonly DeviceSet _deviceSet;
    private readonly string _device;
    private readonly string _technology;
    private readonly ValueOption? _value;
    private readonly List<Gate> _gates;
    private string? _name;
    private Rotation _rotation;
    private Part? _part;
    private int _gate;
    private Point _cursor;

    public AddTool(Editor editor, Library library, DeviceSet deviceSet, string device, string technology, string? name, Rotation rotation,
        ValueOption? value = null)
    {
        _value = value;
        _editor = editor;
        _library = library;
        _deviceSet = deviceSet;
        _device = device;
        _technology = technology;
        _gates = deviceSet.Gates.Values.ToList();
        _name = name;
        _rotation = rotation;
    }

    public string Prompt
    {
        get
        {
            var gate = _gates.Count > 1 ? $" gate {_gates[_gate].Name}" : "";
            var value = _value is null ? "" : $" {_value.Val}";
            return $"ADD {_deviceSet.Name}{_device}@{_library.Name}{value}: left-click to place{gate}, right-click to rotate. Esc ends.";
        }
    }

    public bool SnapToGrid => true;

    public IReadOnlyList<Wire> Preview =>
        _gates.Count > 0 && _library.Symbols.TryGetValue(_gates[_gate].Symbol, out var symbol)
            ? Symbols.Outline(symbol, new Transform(_cursor, _rotation)).ToList()
            : Array.Empty<Wire>();

    public void Click(Point p, double pickRadius, int clickCount)
    {
        if (_gates.Count == 0)
            return;

        // Each placed gate is its own step for UNDO.
        _editor.Checkpoint();
        if (_part is null)
        {
            _part = _editor.Schematic.AddPart(_library, _deviceSet, _device, _value?.Technology ?? _technology, _name);
            if (_value is not null)
                _editor.Schematic.Parts[_part.Name] = _part = _part.WithValue(_value);
        }
        _name = null;

        var instance = new Instance { Part = _part.Name, Gate = _gates[_gate].Name, Position = p, Rotation = _rotation, Smashed = true };
        if (_library.Symbols.TryGetValue(_gates[_gate].Symbol, out var symbol))
            instance.Attributes.AddRange(Symbols.SmashedAttributes(symbol, instance.Transform));
        _editor.Sheet.Instances.Add(instance);
        _editor.Sheet.Modified = true;
        _editor.Sheet.RefreshPins(connectFreePins: true);

        if (++_gate >= _gates.Count)
        {
            _gate = 0;
            _part = null;
        }
        _editor.NotifyChanged();
    }

    public void Move(Point p) => _cursor = p;

    public void RightClick(Point p)
    {
        _rotation = _rotation.Rotated90();
        _editor.NotifyChanged();
    }

    public void Cancel() => _editor.SetTool(null);
}
