using System.Globalization;

namespace FalconCAD.Core;

/// <summary>A point in board/sheet space. Units are millimetres.</summary>
public readonly record struct Point(double X, double Y)
{
    public static Point operator +(Point a, Point b) => new(a.X + b.X, a.Y + b.Y);
    public static Point operator -(Point a, Point b) => new(a.X - b.X, a.Y - b.Y);
    public static Point operator *(Point a, double f) => new(a.X * f, a.Y * f);

    public double DistanceTo(Point other) => Math.Sqrt((X - other.X) * (X - other.X) + (Y - other.Y) * (Y - other.Y));

    /// <summary>Unit vector at <paramref name="degrees"/>, exact for multiples of 90.</summary>
    public static Point Direction(double degrees)
    {
        var a = ((degrees % 360) + 360) % 360;
        return a switch
        {
            0 => new Point(1, 0),
            90 => new Point(0, 1),
            180 => new Point(-1, 0),
            270 => new Point(0, -1),
            _ => new Point(Math.Cos(a * Math.PI / 180), Math.Sin(a * Math.PI / 180)),
        };
    }
}

public readonly record struct Rect(Point Min, Point Max)
{
    public double Width => Max.X - Min.X;
    public double Height => Max.Y - Min.Y;
}

/// <summary>
/// An Eagle-style orientation such as R90 or MR180: an angle in degrees,
/// an optional mirror (applied after the rotation, flips X) and the "spin" flag that keeps text upside down.
/// </summary>
public readonly record struct Rotation(double Angle, bool Mirror = false, bool Spin = false)
{
    public static Rotation Parse(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return default;

        var mirror = false;
        var spin = false;
        var i = 0;
        for (; i < text.Length && char.IsLetter(text[i]); i++)
        {
            if (text[i] == 'M') mirror = true;
            else if (text[i] == 'S') spin = true;
        }
        var angle = i < text.Length ? double.Parse(text[i..], CultureInfo.InvariantCulture) : 0;
        return new Rotation(angle, mirror, spin);
    }

    public override string ToString() =>
        $"{(Spin ? "S" : "")}{(Mirror ? "M" : "")}R{Angle.ToString(CultureInfo.InvariantCulture)}";
}

/// <summary>Places local coordinates (a symbol) into the sheet: rotate, mirror, then move.</summary>
public readonly record struct Transform(Point Offset, Rotation Rotation)
{
    public static Transform Identity => new(new Point(0, 0), default);

    public Point Apply(Point p)
    {
        var d = Point.Direction(Rotation.Angle);
        var x = p.X * d.X - p.Y * d.Y;
        var y = p.X * d.Y + p.Y * d.X;
        return new Point(Offset.X + (Rotation.Mirror ? -x : x), Offset.Y + y);
    }
}

public static class GeometryMath
{
    /// <summary>Distances below this are treated as zero.</summary>
    public const double Epsilon = 1e-6;

    public static double DistanceToSegment(Point p, Point a, Point b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var lengthSquared = dx * dx + dy * dy;
        if (lengthSquared < Epsilon * Epsilon)
            return p.DistanceTo(a);

        var t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSquared, 0, 1);
        return p.DistanceTo(new Point(a.X + t * dx, a.Y + t * dy));
    }

    public static bool SamePoint(Point a, Point b) => a.DistanceTo(b) < Epsilon;

    public static double NormalizeAngle(double degrees) => ((degrees % 360) + 360) % 360;
}
