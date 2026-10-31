using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using FalconCAD.Core;

namespace FalconCAD.IO;

/// <summary>
/// Reads Eagle XML schematics (.sch), following the published Eagle DTD.
/// Elements FalconCAD does not use yet (modules, variants, SPICE data...) are skipped.
/// </summary>
public static class EagleSchematicReader
{
    public static Schematic Read(string path)
    {
        using var stream = File.OpenRead(path);
        var schematic = Read(stream);
        schematic.Name = Path.GetFileNameWithoutExtension(path);
        schematic.LastModified = File.GetLastWriteTime(path);
        return schematic;
    }

    public static Schematic Read(Stream stream)
    {
        if (IsBinaryFormat(stream))
            throw new InvalidDataException(
                "This file uses the binary format of EAGLE 5 or earlier. Open it in EAGLE 6 or later and save it to convert it to XML.");

        // Eagle files declare <!DOCTYPE eagle SYSTEM "eagle.dtd">; the DTD file is not needed to read them.
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
        using var reader = XmlReader.Create(stream, settings);
        var document = XDocument.Load(reader);

        var drawing = document.Root?.Element("drawing") ?? throw new InvalidDataException("Not an Eagle file: no <drawing> element.");
        var schematicElement = drawing.Element("schematic") ?? throw new InvalidDataException("Not an Eagle schematic: no <schematic> element.");

        var schematic = new Schematic();
        foreach (var layer in drawing.Element("layers")?.Elements("layer") ?? [])
        {
            schematic.Layers.Add(new Layer
            {
                Number = Int(layer, "number"),
                Name = Str(layer, "name"),
                Color = Int(layer, "color", 7),
                Visible = Bool(layer, "visible", true),
            });
        }
        if (schematic.Layers.Count == 0)
            schematic.Layers.AddRange(Schematic.DefaultLayers());

        foreach (var element in schematicElement.Element("libraries")?.Elements("library") ?? [])
        {
            var library = ReadLibrary(element);
            schematic.Libraries[Library.Key(library.Name, library.Urn)] = library;
        }

        foreach (var attribute in schematicElement.Element("attributes")?.Elements("attribute") ?? [])
            schematic.Attributes[Str(attribute, "name")] = Str(attribute, "value");

        foreach (var element in schematicElement.Element("parts")?.Elements("part") ?? [])
        {
            var part = new Part
            {
                Name = Str(element, "name"),
                LibraryKey = Library.Key(Str(element, "library"), Str(element, "library_urn")),
                DeviceSet = Str(element, "deviceset"),
                Device = Str(element, "device"),
                Technology = Str(element, "technology"),
                Value = (string?)element.Attribute("value"),
            };
            foreach (var attribute in element.Elements("attribute"))
                part.Attributes[Str(attribute, "name")] = Str(attribute, "value");
            schematic.Parts[part.Name] = part;
        }

        foreach (var element in schematicElement.Element("sheets")?.Elements("sheet") ?? [])
            ReadSheet(element, schematic.AddSheet());
        if (schematic.Sheets.Count == 0)
            schematic.AddSheet();

        return schematic;
    }

    /// <summary>XML files start with '&lt;' (possibly after a byte order mark or whitespace); old binary files do not.</summary>
    private static bool IsBinaryFormat(Stream stream)
    {
        if (!stream.CanSeek)
            return false;

        var start = stream.Position;
        var buffer = new byte[64];
        var count = stream.Read(buffer, 0, buffer.Length);
        stream.Position = start;

        var text = System.Text.Encoding.UTF8.GetString(buffer, 0, count).TrimStart('﻿', ' ', '\t', '\r', '\n');
        return text.Length > 0 && text[0] != '<';
    }

    private static Library ReadLibrary(XElement element)
    {
        var library = new Library { Name = Str(element, "name"), Urn = Str(element, "urn") };

        foreach (var s in element.Element("symbols")?.Elements("symbol") ?? [])
        {
            var symbol = new Symbol { Name = Str(s, "name") };
            ReadGraphics(s, symbol.Graphics);
            foreach (var pin in s.Elements("pin"))
                symbol.Pins.Add(ReadPin(pin));
            library.Symbols[symbol.Name] = symbol;
        }

        foreach (var ds in element.Element("devicesets")?.Elements("deviceset") ?? [])
        {
            var deviceSet = new DeviceSet
            {
                Name = Str(ds, "name"),
                Prefix = Str(ds, "prefix"),
                UserValue = Bool(ds, "uservalue", false),
            };
            foreach (var gate in ds.Element("gates")?.Elements("gate") ?? [])
                deviceSet.Gates[Str(gate, "name")] = new Gate { Name = Str(gate, "name"), Symbol = Str(gate, "symbol") };
            foreach (var d in ds.Element("devices")?.Elements("device") ?? [])
            {
                var device = new Device { Name = Str(d, "name") };
                foreach (var connect in d.Element("connects")?.Elements("connect") ?? [])
                    device.Connects[(Str(connect, "gate"), Str(connect, "pin"))] = Str(connect, "pad");
                foreach (var technology in d.Element("technologies")?.Elements("technology") ?? [])
                {
                    var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var attribute in technology.Elements("attribute"))
                        attributes[Str(attribute, "name")] = Str(attribute, "value");
                    device.Technologies[Str(technology, "name")] = attributes;
                }
                deviceSet.Devices[device.Name] = device;
            }
            library.DeviceSets[deviceSet.Name] = deviceSet;
        }

        return library;
    }

    private static void ReadSheet(XElement element, Sheet sheet)
    {
        if (element.Element("plain") is { } plain)
            ReadGraphics(plain, sheet.Plain);

        foreach (var i in element.Element("instances")?.Elements("instance") ?? [])
        {
            var instance = new Instance
            {
                Part = Str(i, "part"),
                Gate = Str(i, "gate"),
                Position = Pt(i, "x", "y"),
                Rotation = Rotation.Parse((string?)i.Attribute("rot")),
                Smashed = Bool(i, "smashed", false),
            };
            foreach (var a in i.Elements("attribute"))
            {
                instance.Attributes.Add(new PlacedAttribute
                {
                    Name = Str(a, "name"),
                    Position = a.Attribute("x") is null ? null : Pt(a, "x", "y"),
                    Size = Dbl(a, "size", 1.778),
                    Layer = Int(a, "layer", 95),
                    Rotation = Rotation.Parse((string?)a.Attribute("rot")),
                    Align = Align.Parse((string?)a.Attribute("align")),
                    Display = Str(a, "display", "value") switch
                    {
                        "off" => AttributeDisplay.Off,
                        "name" => AttributeDisplay.Name,
                        "both" => AttributeDisplay.Both,
                        _ => AttributeDisplay.Value,
                    },
                });
            }
            sheet.Instances.Add(instance);
        }

        foreach (var n in element.Element("nets")?.Elements("net") ?? [])
            sheet.Nets.Add(ReadNet(n));
        foreach (var b in element.Element("busses")?.Elements("bus") ?? [])
            sheet.Busses.Add(ReadNet(b));
    }

    private static Net ReadNet(XElement element)
    {
        var net = new Net { Name = Str(element, "name") };
        foreach (var segment in element.Elements("segment"))
        {
            net.Wires.AddRange(segment.Elements("wire").Select(ReadWire));
            net.Junctions.AddRange(segment.Elements("junction").Select(j => Pt(j, "x", "y")));
            net.PinRefs.AddRange(segment.Elements("pinref").Select(p => new PinRef(Str(p, "part"), Str(p, "gate"), Str(p, "pin"))));
            foreach (var label in segment.Elements("label"))
            {
                net.Labels.Add(new Label
                {
                    Position = Pt(label, "x", "y"),
                    Size = Dbl(label, "size", 1.778),
                    Layer = Int(label, "layer", 95),
                    Rotation = Rotation.Parse((string?)label.Attribute("rot")),
                    Align = Align.Parse((string?)label.Attribute("align")),
                    Xref = Bool(label, "xref", false),
                });
            }
        }
        return net;
    }

    private static void ReadGraphics(XElement element, Graphics graphics)
    {
        graphics.Wires.AddRange(element.Elements("wire").Select(ReadWire));

        foreach (var c in element.Elements("circle"))
            graphics.Circles.Add(new Circle { Center = Pt(c, "x", "y"), Radius = Dbl(c, "radius"), Width = Dbl(c, "width"), Layer = Int(c, "layer") });

        foreach (var p in element.Elements("polygon"))
        {
            graphics.Polygons.Add(new Polygon
            {
                Vertices = p.Elements("vertex").Select(v => Pt(v, "x", "y")).ToList(),
                Width = Dbl(p, "width"),
                Layer = Int(p, "layer"),
            });
        }

        foreach (var r in element.Elements("rectangle"))
        {
            var a = Pt(r, "x1", "y1");
            var b = Pt(r, "x2", "y2");
            graphics.Rectangles.Add(new Rectangle
            {
                Min = new Point(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y)),
                Max = new Point(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)),
                Layer = Int(r, "layer"),
                Rotation = Rotation.Parse((string?)r.Attribute("rot")),
            });
        }

        foreach (var t in element.Elements("text"))
        {
            graphics.Texts.Add(new Text
            {
                Value = t.Value,
                Position = Pt(t, "x", "y"),
                Size = Dbl(t, "size", 1.778),
                Layer = Int(t, "layer"),
                Rotation = Rotation.Parse((string?)t.Attribute("rot")),
                Align = Align.Parse((string?)t.Attribute("align")),
            });
        }

        foreach (var f in element.Elements("frame"))
        {
            var a = Pt(f, "x1", "y1");
            var b = Pt(f, "x2", "y2");
            graphics.Frames.Add(new Frame
            {
                Min = new Point(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y)),
                Max = new Point(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)),
                Columns = Int(f, "columns"),
                Rows = Int(f, "rows"),
                Layer = Int(f, "layer"),
            });
        }
    }

    private static Wire ReadWire(XElement w) => new()
    {
        From = Pt(w, "x1", "y1"),
        To = Pt(w, "x2", "y2"),
        Width = Dbl(w, "width"),
        Layer = Int(w, "layer"),
        Curve = Dbl(w, "curve"),
    };

    private static Pin ReadPin(XElement p) => new()
    {
        Name = Str(p, "name"),
        Position = Pt(p, "x", "y"),
        Rotation = Rotation.Parse((string?)p.Attribute("rot")),
        Length = Str(p, "length", "long") switch
        {
            "point" => PinLength.Point,
            "short" => PinLength.Short,
            "middle" => PinLength.Middle,
            _ => PinLength.Long,
        },
        Visible = Str(p, "visible", "both") switch
        {
            "off" => PinVisible.Off,
            "pad" => PinVisible.Pad,
            "pin" => PinVisible.Pin,
            _ => PinVisible.Both,
        },
    };

    private static string Str(XElement e, string name, string fallback = "") => (string?)e.Attribute(name) ?? fallback;

    private static double Dbl(XElement e, string name, double fallback = 0) =>
        (string?)e.Attribute(name) is { } s ? double.Parse(s, CultureInfo.InvariantCulture) : fallback;

    private static int Int(XElement e, string name, int fallback = 0) =>
        (string?)e.Attribute(name) is { } s ? int.Parse(s, CultureInfo.InvariantCulture) : fallback;

    private static bool Bool(XElement e, string name, bool fallback) =>
        (string?)e.Attribute(name) is { } s ? s == "yes" : fallback;

    private static Point Pt(XElement e, string x, string y) => new(Dbl(e, x), Dbl(e, y));
}
