namespace FalconCAD.Core;

/// <summary>A point in board/sheet space. Units are millimetres.</summary>
public readonly record struct Point(double X, double Y);

public readonly record struct Rect(Point Min, Point Max)
{
    public double Width => Max.X - Min.X;
    public double Height => Max.Y - Min.Y;
}
