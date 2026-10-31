namespace FalconCAD.Core;

/// <summary>A point in board/sheet space. Units are millimetres.</summary>
public readonly record struct Point(double X, double Y)
{
    public static Point operator +(Point a, Point b) => new(a.X + b.X, a.Y + b.Y);
    public static Point operator -(Point a, Point b) => new(a.X - b.X, a.Y - b.Y);

    public double DistanceTo(Point other) => Math.Sqrt((X - other.X) * (X - other.X) + (Y - other.Y) * (Y - other.Y));
}

public readonly record struct Rect(Point Min, Point Max)
{
    public double Width => Max.X - Min.X;
    public double Height => Max.Y - Min.Y;
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
}
