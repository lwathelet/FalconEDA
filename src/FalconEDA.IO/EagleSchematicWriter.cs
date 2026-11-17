using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using FalconEDA.Core;

namespace FalconEDA.IO;

/// <summary>
/// Writes Eagle XML schematics (.sch).
/// A schematic that was read from a file is written back from that file's XML: only sheets
/// changed in FalconEDA are regenerated, so data FalconEDA does not model yet is kept as it was.
/// </summary>
public static class EagleSchematicWriter
{
    /// <summary>Like Eagle, keep the previous versions as name.s#1 (newest) to name.s#9.</summary>
    public const int BackupCount = 9;

    /// <summary>Saves to <paramref name="path"/>, rotating backups of the file it replaces.</summary>
    public static void Save(Schematic schematic, string path)
    {
        var temp = path + ".tmp";
        using (var stream = File.Create(temp))
            Write(schematic, stream);

        if (File.Exists(path))
        {
            RotateBackups(path);
            File.Copy(path, BackupPath(path, 1), overwrite: true);
        }
        File.Move(temp, path, overwrite: true);

        schematic.Name = Path.GetFileNameWithoutExtension(path);
        foreach (var sheet in schematic.Sheets)
            sheet.Modified = false;
    }

    public static void Write(Schematic schematic, Stream stream)
    {
        var document = SourceDocuments.Get(schematic) is { } source ? new XDocument(source) : CreateDocument(schematic);
        var schematicElement = document.Root!.Element("drawing")!.Element("schematic")!;
        var sheets = GetOrAdd(schematicElement, "sheets", "description", "libraries", "attributes", "variantdefs", "classes", "modules", "parts");
        var sheetElements = sheets.Elements("sheet").ToList();

        // Parts and libraries belong to the whole schematic; they can only have changed together with a sheet.
        if (schematic.Sheets.Any(s => s.Modified))
            WritePartsAndLibraries(schematic, schematicElement);

        for (var i = 0; i < schematic.Sheets.Count; i++)
        {
            var sheet = schematic.Sheets[i];
            if (i >= sheetElements.Count)
            {
                var added = new XElement("sheet");
                sheets.Add(added);
                sheetElements.Add(added);
            }
            else if (!sheet.Modified)
            {
                continue;
            }
            WriteSheet(schematic, sheet, sheetElements[i]);
        }

        // The reader keeps the line breaks between elements as text. Next to new elements they would stop the
        // writer from putting each element on its own line, so remove them; text inside elements stays.
        document.DescendantNodes().OfType<XText>()
            .Where(t => string.IsNullOrWhiteSpace(t.Value) && (t.Parent is null || t.Parent.HasElements))
            .ToList()
            .ForEach(t => t.Remove());

        // Eagle files carry <!DOCTYPE eagle SYSTEM "eagle.dtd">; the reader skips it, so put it back.
        document.DocumentType?.Remove();
        document.AddFirst(new XDocumentType("eagle", null, "eagle.dtd", null));

        // Like Eagle: one element per line, no indentation, UTF-8 without byte order mark.
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = true,
            IndentChars = "",
            NewLineChars = "\n",
        };
        using var writer = XmlWriter.Create(stream, settings);
        document.Save(writer);
    }

    private static void WriteSheet(Schematic schematic, Sheet sheet, XElement element)
    {
        // The sheet's children must stay in DTD order: description, plain, moduleinsts, instances, busses, nets.
        var plain = GetOrAdd(element, "plain", "description");
        plain.Elements("wire").Remove();
        plain.AddFirst(sheet.Wires.Select(WireElement));

        var instances = GetOrAdd(element, "instances", "description", "plain", "moduleinsts");
        instances.RemoveNodes();
        instances.Add(sheet.Instances.Select(InstanceElement));

        var busses = GetOrAdd(element, "busses", "description", "plain", "moduleinsts", "instances");
        busses.RemoveNodes();
        foreach (var bus in sheet.Busses)
            busses.Add(NetElement("bus", bus, schematic, sheet, withPins: false));

        var nets = GetOrAdd(element, "nets", "description", "plain", "moduleinsts", "instances", "busses");
        nets.RemoveNodes();
        foreach (var net in sheet.Nets)
            nets.Add(NetElement("net", net, schematic, sheet, withPins: true));
    }

    /// <summary>
    /// Writes the part list from the model, starting from each part's original XML so nothing is lost,
    /// and adds the libraries of parts pasted in from another schematic.
    /// </summary>
    private static void WritePartsAndLibraries(Schematic schematic, XElement schematicElement)
    {
        var libraries = GetOrAdd(schematicElement, "libraries", "description");
        var present = libraries.Elements("library")
            .Select(l => Library.Key((string?)l.Attribute("name") ?? "", (string?)l.Attribute("urn") ?? ""))
            .ToHashSet();
        foreach (var (key, library) in schematic.Libraries)
        {
            if (!present.Contains(key) && library.Source is XElement source)
                libraries.Add(new XElement(source));
        }
        foreach (var element in libraries.Elements("library"))
        {
            var key = Library.Key((string?)element.Attribute("name") ?? "", (string?)element.Attribute("urn") ?? "");
            if (schematic.Libraries.TryGetValue(key, out var library))
                AddMissingLibraryItems(element, library);
        }

        var parts = GetOrAdd(schematicElement, "parts", "description", "libraries", "attributes", "variantdefs", "classes", "modules");
        parts.RemoveNodes();
        foreach (var part in schematic.Parts.Values)
        {
            XElement element;
            if (part.Source is XElement source)
            {
                element = new XElement(source);
                element.SetAttributeValue("name", part.Name);
            }
            else
            {
                var key = part.LibraryKey.Split('|', 2);
                element = new XElement("part",
                    new XAttribute("name", part.Name),
                    new XAttribute("library", key[0]),
                    key.Length > 1 ? new XAttribute("library_urn", key[1]) : null,
                    new XAttribute("deviceset", part.DeviceSet),
                    new XAttribute("device", part.Device),
                    part.Technology.Length > 0 ? new XAttribute("technology", part.Technology) : null,
                    part.Value is null ? null : new XAttribute("value", part.Value));
            }
            parts.Add(element);
        }
    }

    /// <summary>
    /// Device sets added to a library when pasting from another schematic (see Sheet.Paste) are copied in
    /// from the XML they were read from, with their symbols and the packages and 3D packages their devices use.
    /// </summary>
    private static void AddMissingLibraryItems(XElement libraryElement, Library library)
    {
        var deviceSets = GetOrAdd(libraryElement, "devicesets", "description", "packages", "packages3d", "symbols");
        var symbols = GetOrAdd(libraryElement, "symbols", "description", "packages", "packages3d");
        var existingSets = deviceSets.Elements("deviceset").Select(e => (string?)e.Attribute("name")).ToHashSet();
        var existingSymbols = symbols.Elements("symbol").Select(e => (string?)e.Attribute("name")).ToHashSet();

        foreach (var deviceSet in library.DeviceSets.Values)
        {
            if (existingSets.Contains(deviceSet.Name) || deviceSet.Source is not XElement source)
                continue;
            deviceSets.Add(new XElement(source));
            existingSets.Add(deviceSet.Name);

            foreach (var gate in deviceSet.Gates.Values)
            {
                if (!existingSymbols.Contains(gate.Symbol) && library.Symbols.TryGetValue(gate.Symbol, out var symbol)
                    && symbol.Source is XElement symbolSource)
                {
                    symbols.Add(new XElement(symbolSource));
                    existingSymbols.Add(gate.Symbol);
                }
            }

            // Packages live in the library the device set came from.
            var sourceLibrary = source.Parent?.Parent;
            foreach (var device in source.Descendants("device"))
            {
                if ((string?)device.Attribute("package") is { } packageName)
                    CopyMissing(sourceLibrary, libraryElement, "packages", "package", "name", packageName, "description");
                foreach (var instance3d in device.Descendants("package3dinstance"))
                {
                    if ((string?)instance3d.Attribute("package3d_urn") is { } urn)
                        CopyMissing(sourceLibrary, libraryElement, "packages3d", "package3d", "urn", urn, "description", "packages");
                }
            }
        }
    }

    private static void CopyMissing(XElement? fromLibrary, XElement toLibrary, string listName, string itemName,
        string keyAttribute, string key, params string[] before)
    {
        var item = fromLibrary?.Element(listName)?.Elements(itemName).FirstOrDefault(e => (string?)e.Attribute(keyAttribute) == key);
        if (item is null)
            return;
        var list = GetOrAdd(toLibrary, listName, before);
        if (!list.Elements(itemName).Any(e => (string?)e.Attribute(keyAttribute) == key))
            list.Add(new XElement(item));
    }

    /// <summary>An instance, based on its original XML with position, orientation and smashed texts updated.</summary>
    private static XElement InstanceElement(Instance instance)
    {
        var element = instance.Source is XElement source
            ? new XElement(source)
            : new XElement("instance", new XAttribute("part", ""), new XAttribute("gate", instance.Gate));

        element.SetAttributeValue("part", instance.Part);
        element.SetAttributeValue("x", Number(instance.Position.X));
        element.SetAttributeValue("y", Number(instance.Position.Y));
        if (instance.Smashed)
            element.SetAttributeValue("smashed", "yes");
        element.SetAttributeValue("rot", instance.Rotation == default ? null : instance.Rotation.ToString());

        foreach (var attribute in instance.Attributes)
        {
            var child = element.Elements("attribute").FirstOrDefault(a => (string?)a.Attribute("name") == attribute.Name);
            if (child is null)
            {
                child = new XElement("attribute", new XAttribute("name", attribute.Name),
                    new XAttribute("size", Number(attribute.Size)), new XAttribute("layer", attribute.Layer));
                element.Add(child);
            }
            if (attribute.Position is { } p)
            {
                child.SetAttributeValue("x", Number(p.X));
                child.SetAttributeValue("y", Number(p.Y));
            }
            child.SetAttributeValue("rot", attribute.Rotation == default ? null : attribute.Rotation.ToString());
        }
        return element;
    }

    /// <summary>
    /// A net is stored as segments: groups of connected wires with the pins, junctions and labels on them.
    /// </summary>
    private static XElement NetElement(string name, Net net, Schematic schematic, Sheet sheet, bool withPins)
    {
        var element = new XElement(name, new XAttribute("name", net.Name));
        if (withPins)
            element.Add(new XAttribute("class", net.Class));

        var groups = Net.ConnectedGroups(net.Wires);
        var segments = groups.Select(g => new Segment(g)).ToList();

        if (withPins)
        {
            var loosePins = new List<PinRef>();
            foreach (var pinRef in net.PinRefs)
            {
                var position = schematic.PinPosition(sheet, pinRef);
                var segment = position is { } p ? segments.FirstOrDefault(s => s.Touches(p)) : null;
                if (segment is null)
                    loosePins.Add(pinRef);
                else
                    segment.Pins.Add(pinRef);
            }

            // Pins joined directly to each other, without a wire, form a segment of their own.
            if (loosePins.Count > 0)
            {
                var segment = new Segment(new List<Wire>());
                segment.Pins.AddRange(loosePins);
                segments.Add(segment);
            }

            foreach (var segment in segments.Where(s => s.Wires.Count > 0))
            {
                var probe = new Net { Name = net.Name };
                probe.Wires.AddRange(segment.Wires);
                segment.Junctions.AddRange(net.Junctions.Where(segment.Touches).Concat(probe.FindJunctions())
                    .DistinctBy(p => (Math.Round(p.X, 4), Math.Round(p.Y, 4))));
            }
        }

        foreach (var label in net.Labels)
        {
            var nearest = segments.Where(s => s.Wires.Count > 0)
                .MinBy(s => s.Wires.Min(w => w.DistanceTo(label.Position)));
            (nearest ?? segments.FirstOrDefault())?.Labels.Add(label);
        }

        foreach (var segment in segments)
        {
            element.Add(new XElement("segment",
                segment.Pins.Select(p => new XElement("pinref",
                    new XAttribute("part", p.Part), new XAttribute("gate", p.Gate), new XAttribute("pin", p.Pin))),
                segment.Wires.Select(WireElement),
                segment.Junctions.Select(j => new XElement("junction", Coord("x", j.X), Coord("y", j.Y))),
                segment.Labels.Select(LabelElement)));
        }
        return element;
    }

    private static XElement WireElement(Wire w)
    {
        var element = new XElement("wire",
            Coord("x1", w.From.X), Coord("y1", w.From.Y), Coord("x2", w.To.X), Coord("y2", w.To.Y),
            Coord("width", w.Width), new XAttribute("layer", w.Layer));
        if (w.Style != "continuous")
            element.Add(new XAttribute("style", w.Style));
        if (w.Curve != 0)
        {
            element.Add(Coord("curve", w.Curve));
            if (w.Cap != "round")
                element.Add(new XAttribute("cap", w.Cap));
        }
        return element;
    }

    private static XElement LabelElement(Label l)
    {
        var element = new XElement("label",
            Coord("x", l.Position.X), Coord("y", l.Position.Y), Coord("size", l.Size), new XAttribute("layer", l.Layer));
        if (l.Rotation != default)
            element.Add(new XAttribute("rot", l.Rotation.ToString()));
        if (l.Xref)
            element.Add(new XAttribute("xref", "yes"));
        if (l.Align != Align.BottomLeft)
            element.Add(new XAttribute("align", AlignName(l.Align)));
        return element;
    }

    private static string AlignName(Align align)
    {
        if (align is { H: HAlign.Center, V: VAlign.Center })
            return "center";
        var v = align.V switch { VAlign.Top => "top", VAlign.Center => "center", _ => "bottom" };
        var h = align.H switch { HAlign.Right => "right", HAlign.Center => "center", _ => "left" };
        return $"{v}-{h}";
    }

    /// <summary>A minimal Eagle document for a schematic that was not read from a file.</summary>
    private static XDocument CreateDocument(Schematic schematic) => new(
        new XDeclaration("1.0", "utf-8", null),
        new XElement("eagle", new XAttribute("version", "9.4.2"),
            new XElement("drawing",
                new XElement("settings",
                    new XElement("setting", new XAttribute("alwaysvectorfont", "no")),
                    new XElement("setting", new XAttribute("verticaltext", "up"))),
                new XElement("grid",
                    new XAttribute("distance", "0.1"), new XAttribute("unitdist", "inch"), new XAttribute("unit", "inch"),
                    new XAttribute("style", "lines"), new XAttribute("multiple", "1"), new XAttribute("display", "no"),
                    new XAttribute("altdistance", "0.01"), new XAttribute("altunitdist", "inch"), new XAttribute("altunit", "inch")),
                new XElement("layers", schematic.Layers.Select(l => new XElement("layer",
                    new XAttribute("number", l.Number), new XAttribute("name", l.Name),
                    new XAttribute("color", l.Color), new XAttribute("fill", 1),
                    new XAttribute("visible", l.Visible ? "yes" : "no"), new XAttribute("active", "yes")))),
                new XElement("schematic",
                    new XAttribute("xreflabel", "%F%N/%S.%C%R"), new XAttribute("xrefpart", "/%S.%C%R"),
                    new XElement("libraries"),
                    new XElement("attributes"),
                    new XElement("variantdefs"),
                    new XElement("classes",
                        new XElement("class", new XAttribute("number", 0), new XAttribute("name", "default"),
                            new XAttribute("width", 0), new XAttribute("drill", 0))),
                    new XElement("parts"),
                    new XElement("sheets")))));

    /// <summary>Returns the child element, creating it after the last of <paramref name="before"/> that exists.</summary>
    private static XElement GetOrAdd(XElement parent, string name, params string[] before)
    {
        if (parent.Element(name) is { } existing)
            return existing;

        var created = new XElement(name);
        var anchor = parent.Elements().LastOrDefault(e => before.Contains(e.Name.LocalName));
        if (anchor is null)
            parent.AddFirst(created);
        else
            anchor.AddAfterSelf(created);
        return created;
    }

    // Coordinates are written in millimetres, rounded to remove floating point noise from grid snapping.
    private static XAttribute Coord(string name, double value) => new(name, Number(value));

    private static string Number(double value) =>
        (Math.Round(value, 6) + 0.0).ToString("0.######", CultureInfo.InvariantCulture);

    private static string BackupPath(string path, int number) => Path.ChangeExtension(path, $".s#{number}");

    private static void RotateBackups(string path)
    {
        File.Delete(BackupPath(path, BackupCount));
        for (var i = BackupCount - 1; i >= 1; i--)
        {
            if (File.Exists(BackupPath(path, i)))
                File.Move(BackupPath(path, i), BackupPath(path, i + 1));
        }
    }

    private sealed class Segment(List<Wire> wires)
    {
        public List<Wire> Wires { get; } = wires;
        public List<PinRef> Pins { get; } = new();
        public List<Point> Junctions { get; } = new();
        public List<Label> Labels { get; } = new();

        public bool Touches(Point p) => Wires.Any(w => w.DistanceTo(p) < GeometryMath.Epsilon);
    }
}
