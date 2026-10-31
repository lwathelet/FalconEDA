using System.Globalization;

namespace FalconCAD.Core;

/// <summary>A text ready to draw: placeholders resolved, angle normalised so it reads left-to-right or bottom-to-top.</summary>
public sealed record TextPrimitive(string Value, Point Position, double Size, double Angle, Align Align, int Layer);

/// <summary>
/// Everything visible on one sheet, flattened into sheet coordinates.
/// Symbols are placed, pins drawn, and text placeholders replaced.
/// </summary>
public sealed class SheetView
{
    private const double PinNameSize = 1.778;
    private const double JunctionRadius = 0.5;
    private const int SymbolLayer = 94;
    private const int NameLayer = 95;

    private readonly Schematic _schematic;
    private readonly int _sheetIndex;

    private SheetView(Schematic schematic, int sheetIndex)
    {
        _schematic = schematic;
        _sheetIndex = sheetIndex;
    }

    public List<Wire> Wires { get; } = new();
    public List<Circle> Circles { get; } = new();
    public List<Polygon> Polygons { get; } = new();
    public List<TextPrimitive> Texts { get; } = new();

    /// <summary>Origins of parts and of their smashed texts, which Eagle marks with a small cross.</summary>
    public List<Point> Origins { get; } = new();

    public static SheetView Build(Schematic schematic, int sheetIndex)
    {
        var view = new SheetView(schematic, sheetIndex);
        var sheet = schematic.Sheets[sheetIndex];

        view.AddGraphics(sheet.Plain, Transform.Identity, text => view.ResolvePlaceholder(text, null, null));
        foreach (var instance in sheet.Instances)
            view.AddInstance(instance);
        foreach (var net in sheet.Nets.Concat(sheet.Busses))
            view.AddNet(net);

        return view;
    }

    public Rect? Bounds()
    {
        var points = Wires.SelectMany(w => new[] { w.From, w.To })
            .Concat(Polygons.SelectMany(p => p.Vertices))
            .Concat(Circles.Select(c => c.Center))
            .Concat(Texts.Select(t => t.Position))
            .ToList();
        if (points.Count == 0)
            return null;
        return new Rect(
            new Point(points.Min(p => p.X), points.Min(p => p.Y)),
            new Point(points.Max(p => p.X), points.Max(p => p.Y)));
    }

    private void AddInstance(Instance instance)
    {
        if (_schematic.Resolve(instance) is not var (part, _, gate, symbol, device))
            return;

        var transform = instance.Transform;
        Origins.Add(instance.Position);
        AddGraphics(symbol.Graphics, transform, text =>
        {
            // A smashed instance draws its name and value from its own placed attributes instead.
            if (instance.Smashed && text.StartsWith('>')
                && instance.Attributes.Any(a => a.Name.Equals(text[1..], StringComparison.OrdinalIgnoreCase)))
                return null;
            return ResolvePlaceholder(text, part, instance);
        });

        foreach (var pin in symbol.Pins)
            AddPin(pin, transform, device?.Connects.GetValueOrDefault((gate.Name, pin.Name)));

        foreach (var attribute in instance.Attributes)
        {
            if (attribute.Position is not { } position || attribute.Display == AttributeDisplay.Off)
                continue;
            Origins.Add(position);
            var value = ResolvePlaceholder(">" + attribute.Name, part, instance) ?? "";
            var shown = attribute.Display switch
            {
                AttributeDisplay.Name => attribute.Name,
                AttributeDisplay.Both => $"{attribute.Name} = {value}",
                _ => value,
            };
            AddText(shown, position, attribute.Size, attribute.Rotation, attribute.Align, attribute.Layer, Transform.Identity);
        }
    }

    private void AddPin(Pin pin, Transform transform, string? pad)
    {
        var direction = Point.Direction(pin.Rotation.Angle);
        var end = pin.Position + direction * pin.LengthMm;
        if (pin.LengthMm > 0)
            Wires.Add(new Wire { From = transform.Apply(pin.Position), To = transform.Apply(end), Width = 0.1524, Layer = SymbolLayer });

        if (pin.Visible is PinVisible.Pin or PinVisible.Both)
        {
            var namePosition = end + direction * 2.54;
            AddText(pin.DisplayName, namePosition, PinNameSize, pin.Rotation,
                new Align(HAlign.Left, VAlign.Center), NameLayer, transform);
        }

        if (pad is not null && pin.Visible is PinVisible.Pad or PinVisible.Both && pin.LengthMm > 0)
        {
            // Pad number sits just above the pin line, near the connection point.
            var normal = Point.Direction(pin.Rotation.Angle + 90);
            var padPosition = pin.Position + direction * 1.27 + normal * 0.3;
            AddText(pad, padPosition, PinNameSize * 0.85, pin.Rotation, Align.BottomLeft, NameLayer, transform);
        }
    }

    private void AddNet(Net net)
    {
        Wires.AddRange(net.Wires);

        var junctions = net.Junctions.Concat(net.FindJunctions())
            .DistinctBy(p => (Math.Round(p.X, 4), Math.Round(p.Y, 4)));
        foreach (var junction in junctions)
            Circles.Add(new Circle { Center = junction, Radius = JunctionRadius, Layer = Sheet.NetLayer });

        foreach (var label in net.Labels)
            AddText(net.Name, label.Position, label.Size, label.Rotation, label.Align, label.Layer, Transform.Identity);
    }

    private void AddGraphics(Graphics graphics, Transform transform, Func<string, string?> resolve)
    {
        foreach (var w in graphics.Wires)
        {
            Wires.Add(new Wire
            {
                From = transform.Apply(w.From),
                To = transform.Apply(w.To),
                Width = w.Width,
                Layer = w.Layer,
                Curve = transform.Rotation.Mirror ? -w.Curve : w.Curve,
            });
        }

        foreach (var c in graphics.Circles)
            Circles.Add(new Circle { Center = transform.Apply(c.Center), Radius = c.Radius, Width = c.Width, Layer = c.Layer });

        foreach (var p in graphics.Polygons)
            Polygons.Add(new Polygon { Vertices = p.Vertices.Select(transform.Apply).ToList(), Width = p.Width, Layer = p.Layer });

        foreach (var r in graphics.Rectangles)
        {
            var center = new Point((r.Min.X + r.Max.X) / 2, (r.Min.Y + r.Max.Y) / 2);
            var own = new Transform(center, r.Rotation);
            var half = new Point(r.Max.X - center.X, r.Max.Y - center.Y);
            var corners = new[]
            {
                new Point(-half.X, -half.Y), new Point(half.X, -half.Y),
                new Point(half.X, half.Y), new Point(-half.X, half.Y),
            };
            Polygons.Add(new Polygon { Vertices = corners.Select(c => transform.Apply(own.Apply(c))).ToList(), Layer = r.Layer });
        }

        foreach (var f in graphics.Frames)
            AddFrame(f, transform);

        foreach (var t in graphics.Texts)
        {
            var value = resolve(t.Value);
            if (value is not null)
                AddText(value, t.Position, t.Size, t.Rotation, t.Align, t.Layer, transform);
        }
    }

    private void AddFrame(Frame frame, Transform transform)
    {
        var border = Math.Clamp(Math.Min(frame.Max.X - frame.Min.X, frame.Max.Y - frame.Min.Y) * 0.02, 2, 5);
        var outer = frame;
        var inner = (Min: outer.Min + new Point(border, border), Max: outer.Max - new Point(border, border));

        void Line(Point a, Point b) =>
            Wires.Add(new Wire { From = transform.Apply(a), To = transform.Apply(b), Width = 0.254, Layer = frame.Layer });
        void Box(Point min, Point max)
        {
            Line(min, new Point(max.X, min.Y));
            Line(new Point(max.X, min.Y), max);
            Line(max, new Point(min.X, max.Y));
            Line(new Point(min.X, max.Y), min);
        }

        Box(outer.Min, outer.Max);
        Box(inner.Min, inner.Max);

        var textSize = border * 0.5;
        var center = new Align(HAlign.Center, VAlign.Center);
        if (frame.Columns > 0)
        {
            var step = (inner.Max.X - inner.Min.X) / frame.Columns;
            for (var i = 0; i < frame.Columns; i++)
            {
                var x = inner.Min.X + step * i;
                if (i > 0)
                {
                    Line(new Point(x, outer.Min.Y), new Point(x, inner.Min.Y));
                    Line(new Point(x, inner.Max.Y), new Point(x, outer.Max.Y));
                }
                var label = (i + 1).ToString(CultureInfo.InvariantCulture);
                AddText(label, new Point(x + step / 2, outer.Min.Y + border / 2), textSize, default, center, frame.Layer, transform);
                AddText(label, new Point(x + step / 2, outer.Max.Y - border / 2), textSize, default, center, frame.Layer, transform);
            }
        }
        if (frame.Rows > 0)
        {
            var step = (inner.Max.Y - inner.Min.Y) / frame.Rows;
            for (var i = 0; i < frame.Rows; i++)
            {
                // Rows are lettered from the top.
                var y = inner.Max.Y - step * i;
                if (i > 0)
                {
                    Line(new Point(outer.Min.X, y), new Point(inner.Min.X, y));
                    Line(new Point(inner.Max.X, y), new Point(outer.Max.X, y));
                }
                var label = ((char)('A' + i)).ToString();
                AddText(label, new Point(outer.Min.X + border / 2, y - step / 2), textSize, default, center, frame.Layer, transform);
                AddText(label, new Point(outer.Max.X - border / 2, y - step / 2), textSize, default, center, frame.Layer, transform);
            }
        }
    }

    /// <summary>
    /// Places a text and keeps it readable: a mirrored symbol does not mirror its text,
    /// and text pointing left or down is turned around unless it has the spin flag.
    /// </summary>
    private void AddText(string value, Point position, double size, Rotation rotation, Align align, int layer, Transform transform)
    {
        // Rotate with the symbol; a mirror then reflects the direction (angle -> -angle) and swaps left/right alignment.
        var angle = rotation.Angle + transform.Rotation.Angle;
        if (transform.Rotation.Mirror)
        {
            angle = -angle;
            align = align.FlipH();
        }
        angle = GeometryMath.NormalizeAngle(angle);

        if (!rotation.Spin && angle > 90 && angle <= 270)
        {
            angle = GeometryMath.NormalizeAngle(angle - 180);
            align = align.FlipH().FlipV();
        }

        Texts.Add(new TextPrimitive(value, transform.Apply(position), size, angle, align, layer));
    }

    /// <summary>Replaces Eagle text variables such as &gt;NAME, &gt;VALUE or &gt;SHEET. Returns the text unchanged if it is not one.</summary>
    private string? ResolvePlaceholder(string text, Part? part, Instance? instance)
    {
        if (!text.StartsWith('>') || text.Length < 2)
            return text;

        var key = text[1..].ToUpperInvariant();
        switch (key)
        {
            case "NAME" when part is not null && instance is not null:
                var deviceSet = _schematic.Resolve(instance)?.DeviceSet;
                return deviceSet is { Gates.Count: > 1 } ? part.Name + instance.Gate : part.Name;
            case "PART" when part is not null:
                return part.Name;
            case "GATE" when instance is not null:
                return instance.Gate;
            case "VALUE" when part is not null:
                return _schematic.ValueOf(part);
            case "SHEET":
                return $"{_sheetIndex + 1}/{_schematic.Sheets.Count}";
            case "SHEETNR":
                return (_sheetIndex + 1).ToString(CultureInfo.InvariantCulture);
            case "SHEETS":
                return _schematic.Sheets.Count.ToString(CultureInfo.InvariantCulture);
            case "DRAWING_NAME":
                return _schematic.Name;
            case "LAST_DATE_TIME":
                return _schematic.LastModified.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
            case "PLOT_DATE_TIME":
                return DateTime.Now.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
        }

        // Attributes set on the part win over those of its device technology, then the schematic's own.
        if (part is not null && part.Attributes.TryGetValue(key, out var partValue))
            return partValue;
        if (instance is not null && _schematic.Resolve(instance)?.Device is { } device
            && device.Technologies.TryGetValue(part?.Technology ?? "", out var technology)
            && technology.TryGetValue(key, out var technologyValue))
            return technologyValue;
        if (_schematic.Attributes.TryGetValue(key, out var globalValue))
            return globalValue;
        return text;
    }
}
