namespace FalconCAD.Core;

public sealed class Layer
{
    public required int Number { get; init; }
    public required string Name { get; init; }
    public bool Visible { get; set; } = true;
}

public sealed class Wire
{
    public required Point From { get; init; }
    public required Point To { get; init; }
    public double Width { get; init; } = 0.15;
    public int Layer { get; init; }
}

public sealed class Net
{
    public required string Name { get; set; }
    public List<Wire> Wires { get; } = new();
}

/// <summary>A schematic sheet or a board: the thing an editor window shows.</summary>
public sealed class Drawing
{
    public List<Layer> Layers { get; } = new();
    public List<Net> Nets { get; } = new();
}
