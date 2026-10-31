namespace FalconCAD.Core;

public enum HAlign { Left, Center, Right }

public enum VAlign { Bottom, Center, Top }

public readonly record struct Align(HAlign H, VAlign V)
{
    public static Align BottomLeft => new(HAlign.Left, VAlign.Bottom);

    /// <summary>Parses Eagle names such as "bottom-left", "center" or "top-right".</summary>
    public static Align Parse(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return BottomLeft;
        if (text == "center")
            return new Align(HAlign.Center, VAlign.Center);

        var parts = text.Split('-');
        var v = parts[0] switch { "top" => VAlign.Top, "center" => VAlign.Center, _ => VAlign.Bottom };
        var h = parts.Length > 1 ? parts[1] switch { "right" => HAlign.Right, "center" => HAlign.Center, _ => HAlign.Left } : HAlign.Left;
        return new Align(h, v);
    }

    public Align FlipH() => this with { H = H switch { HAlign.Left => HAlign.Right, HAlign.Right => HAlign.Left, _ => H } };

    public Align FlipV() => this with { V = V switch { VAlign.Bottom => VAlign.Top, VAlign.Top => VAlign.Bottom, _ => V } };
}

public sealed class Layer
{
    public required int Number { get; init; }
    public required string Name { get; init; }

    /// <summary>Index into the 16-colour palette, as stored in Eagle files.</summary>
    public int Color { get; init; } = 7;
    public bool Visible { get; set; } = true;

    public override string ToString() => $"{Number} {Name}";
}

public sealed class Wire
{
    public required Point From { get; init; }
    public required Point To { get; init; }
    public double Width { get; init; } = 0.15;
    public int Layer { get; init; }

    /// <summary>Arc angle in degrees; positive is counter-clockwise, 0 is a straight line.</summary>
    public double Curve { get; init; }

    public double DistanceTo(Point p) => GeometryMath.DistanceToSegment(p, From, To);
}

public sealed class Circle
{
    public required Point Center { get; init; }
    public required double Radius { get; init; }

    /// <summary>Line width; 0 means filled.</summary>
    public double Width { get; init; }
    public int Layer { get; init; }
}

public sealed class Polygon
{
    public required IReadOnlyList<Point> Vertices { get; init; }
    public double Width { get; init; }
    public int Layer { get; init; }
}

public sealed class Rectangle
{
    public required Point Min { get; init; }
    public required Point Max { get; init; }
    public int Layer { get; init; }
    public Rotation Rotation { get; init; }
}

public sealed class Text
{
    public required string Value { get; init; }
    public required Point Position { get; init; }
    public double Size { get; init; } = 1.778;
    public int Layer { get; init; }
    public Rotation Rotation { get; init; }
    public Align Align { get; init; } = Align.BottomLeft;
}

public sealed class Frame
{
    public required Point Min { get; init; }
    public required Point Max { get; init; }
    public int Columns { get; init; }
    public int Rows { get; init; }
    public int Layer { get; init; }
}

public enum PinLength { Point, Short, Middle, Long }

public enum PinVisible { Off, Pad, Pin, Both }

public sealed class Pin
{
    public required string Name { get; init; }

    /// <summary>The connection point; the pin line runs from here in the direction of <see cref="Rotation"/>.</summary>
    public required Point Position { get; init; }
    public Rotation Rotation { get; init; }
    public PinLength Length { get; init; } = PinLength.Long;
    public PinVisible Visible { get; init; } = PinVisible.Both;

    public double LengthMm => Length switch
    {
        PinLength.Point => 0,
        PinLength.Short => 2.54,
        PinLength.Middle => 5.08,
        _ => 7.62,
    };

    /// <summary>The name without Eagle's "@n" suffix used for pins that share a name.</summary>
    public string DisplayName => Name.Split('@')[0];
}

/// <summary>Plain graphics, as found in symbols and in a sheet's plain section.</summary>
public sealed class Graphics
{
    public List<Wire> Wires { get; } = new();
    public List<Circle> Circles { get; } = new();
    public List<Polygon> Polygons { get; } = new();
    public List<Rectangle> Rectangles { get; } = new();
    public List<Text> Texts { get; } = new();
    public List<Frame> Frames { get; } = new();
}
