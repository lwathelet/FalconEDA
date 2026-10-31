namespace FalconCAD.Core;

public enum Unit
{
    Mic,
    Mm,
    Mil,
    Inch,
}

public static class Units
{
    public static double ToMm(this Unit unit) => unit switch
    {
        Unit.Mic => 0.001,
        Unit.Mm => 1,
        Unit.Mil => 0.0254,
        Unit.Inch => 25.4,
        _ => throw new ArgumentOutOfRangeException(nameof(unit)),
    };

    public static string Name(this Unit unit) => unit.ToString().ToLowerInvariant();
}

public sealed class GridSettings
{
    /// <summary>Grid size, expressed in <see cref="Unit"/>.</summary>
    public double Size { get; set; } = 0.1;
    public Unit Unit { get; set; } = Unit.Inch;
    public bool Visible { get; set; } = true;

    public double SizeMm => Size * Unit.ToMm();

    public Point Snap(Point p)
    {
        var s = SizeMm;
        return new Point(Math.Round(p.X / s) * s, Math.Round(p.Y / s) * s);
    }
}
