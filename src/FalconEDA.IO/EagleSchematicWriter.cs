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
    /// <summary>Saves to <paramref name="path"/>, rotating backups of the file it replaces.</summary>
    public static void Save(Schematic schematic, string path)
    {
        SafeFile.Write(path, 's', stream => Write(schematic, stream));

        schematic.Name = Path.GetFileNameWithoutExtension(path);
        foreach (var sheet in schematic.Sheets)
            sheet.Modified = false;
        schematic.PartsModified = false;
    }

    public static void Write(Schematic schematic, Stream stream)
    {
        // The XML read from the file is changed in place: wires, parts and instances know their element in it,
        // which lets unchanged and moved objects keep their exact XML. Afterwards it matches the file written.
        var document = SourceDocuments.Get(schematic) ?? CreateDocument(schematic);
        SourceDocuments.Set(schematic, document);
        var schematicElement = document.Root!.Element("drawing")!.Element("schematic")!;
        var sheets = GetOrAdd(schematicElement, "sheets", "description", "libraries", "attributes", "variantdefs", "classes", "modules", "parts");
        var sheetElements = sheets.Elements("sheet").ToList();

        // Parts and libraries belong to the whole schematic; they can only have changed together with a sheet.
        if (schematic.PartsModified || schematic.Sheets.Any(s => s.Modified))
        {
            WriteMissingLayers(schematic, document.Root!.Element("drawing")!);
            WritePartsAndLibraries(schematic, schematicElement);
        }

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

        EagleXml.Save(document, stream);
    }

    /// <summary>
    /// Writes a changed sheet. Whatever did not change (each net, the plain wires, each part) keeps the exact XML
    /// it was read from, so the file only differs from the original where something was edited.
    /// </summary>
    private static void WriteSheet(Schematic schematic, Sheet sheet, XElement element)
    {
        // The sheet's children must stay in DTD order: description, plain, moduleinsts, instances, busses, nets.
        var plain = GetOrAdd(element, "plain", "description");
        if (!SameWires(plain.Elements("wire").Select(EagleSchematicReader.ReadWire), sheet.Wires))
        {
            plain.Elements("wire").Remove();
            plain.AddFirst(sheet.Wires.Select(WireElement));
        }

        var instances = GetOrAdd(element, "instances", "description", "plain", "moduleinsts");
        var newInstances = sheet.Instances.Select(InstanceElement).ToList();
        if (!instances.Elements().Select(e => e.ToString()).SequenceEqual(newInstances.Select(e => e.ToString())))
        {
            instances.RemoveNodes();
            instances.Add(newInstances);
        }
        foreach (var (instance, written) in sheet.Instances.Zip(instances.Elements()))
            instance.Source = written;

        var busses = GetOrAdd(element, "busses", "description", "plain", "moduleinsts", "instances");
        WriteNets(busses, "bus", sheet.Busses, schematic, sheet);

        var nets = GetOrAdd(element, "nets", "description", "plain", "moduleinsts", "instances", "busses");
        WriteNets(nets, "net", sheet.Nets, schematic, sheet);
    }

    /// <summary>Writes the nets (or busses), keeping the original XML of every one that did not change.</summary>
    private static void WriteNets(XElement list, string name, IReadOnlyList<Net> nets, Schematic schematic, Sheet sheet)
    {
        // Each net's original XML: the element it was read from (it may have been renamed since), else one of its name.
        var elements = list.Elements(name).ToList();
        var claimed = nets.Select(n => n.Source).OfType<XElement>().Where(elements.Contains).ToHashSet();
        var byName = elements.Where(e => !claimed.Contains(e))
            .GroupBy(e => (string?)e.Attribute("name") ?? "")
            .ToDictionary(g => g.Key, g => g.First());
        var written = nets.Select(net =>
        {
            var element = net.Source is XElement source && elements.Contains(source) ? source : byName.GetValueOrDefault(net.Name);
            if (element is not null && (SameNet(EagleSchematicReader.ReadNet(element), net) || TryUpdateInPlace(element, net, schematic, sheet)))
                return element;
            var fresh = NetElement(name, net, schematic, sheet, withPins: name == "net");
            net.Source = fresh;
            return fresh;
        }).ToList();

        if (written.Count == list.Elements(name).Count() && written.Zip(list.Elements(name)).All(p => ReferenceEquals(p.First, p.Second)))
            return;
        list.RemoveNodes();
        list.Add(written.Select(e => e.Parent is null ? e : new XElement(e)));
    }

    /// <summary>
    /// A net whose wires, labels and junctions moved, or that got extra wires or pins (as when a part is moved and
    /// its nets stretch), is updated in its original XML: coordinates change in place, a new wire goes into the
    /// segment it connects to (segments it joins are merged), a removed wire is taken out, and everything else
    /// stays in Eagle's order. Returns false when that is not enough (pins or labels removed, a segment falling
    /// apart); then the net is written anew.
    /// </summary>
    private static bool TryUpdateInPlace(XElement element, Net net, Schematic schematic, Sheet sheet)
    {
        SetIfChanged(element, "name", net.Name);

        // Wires and labels that are gone (e.g. a short wire that shrank to nothing when a part was moved back).
        var modelSources = net.Wires.Select(w => w.Source).Concat(net.Labels.Select(l => l.Source)).OfType<XElement>().ToHashSet();
        foreach (var removed in element.Descendants().Where(e => e.Name.LocalName is "wire" or "label" && !modelSources.Contains(e)).ToList())
            RemoveFromSegment(removed);

        var wireElements = element.Descendants("wire").ToList();
        var labelElements = element.Descendants("label").ToList();
        var junctionElements = element.Descendants("junction").ToList();
        var known = net.Wires.Where(w => w.Source is XElement s && wireElements.Contains(s)).ToList();
        var added = net.Wires.Except(known).ToList();
        var knownLabels = net.Labels.Where(l => l.Source is XElement s && labelElements.Contains(s)).ToList();
        var addedLabels = net.Labels.Except(knownLabels).ToList();

        static string PinKey(PinRef p) => $"{p.Part}/{p.Gate}/{p.Pin}";
        static PinRef PinOf(XElement p) => new((string?)p.Attribute("part") ?? "", (string?)p.Attribute("gate") ?? "", (string?)p.Attribute("pin") ?? "");
        var xmlPins = element.Descendants("pinref").Select(PinOf).ToList();
        var addedPins = net.PinRefs.Where(p => !xmlPins.Contains(p)).ToList();

        if (known.Count != wireElements.Count || known.Select(w => w.Source).Distinct().Count() != known.Count
            || knownLabels.Select(l => l.Source).Distinct().Count() != knownLabels.Count
            || junctionElements.Count > net.Junctions.Count
            || xmlPins.Select(PinKey).Distinct().Count() != xmlPins.Count)
            return false;

        // Where new wires and pins go: the segment holding a wire they touch, or listing a pin at that point.
        var segmentOf = known.ToDictionary(w => w, w => ((XElement)w.Source!).Parent!);
        var pinSegments = element.Descendants("pinref")
            .Select(p => (Segment: p.Parent!, Position: schematic.PinPosition(sheet, new PinRef(
                (string?)p.Attribute("part") ?? "", (string?)p.Attribute("gate") ?? "", (string?)p.Attribute("pin") ?? ""))))
            .Where(x => x.Position is not null)
            .ToList();
        XElement? SegmentTouching(Point p) =>
            segmentOf.FirstOrDefault(x => x.Key.DistanceTo(p) < GeometryMath.Epsilon).Value
            ?? pinSegments.FirstOrDefault(x => GeometryMath.SamePoint(x.Position!.Value, p)).Segment;

        foreach (var wire in added)
        {
            var segments = new[] { SegmentTouching(wire.From), SegmentTouching(wire.To) }.OfType<XElement>().Distinct().ToList();
            var segment = segments.FirstOrDefault();
            if (segment is null)
            {
                segment = new XElement("segment");
                element.Add(segment);
            }
            foreach (var other in segments.Skip(1))
            {
                // The new wire joins two segments into one.
                segment.Add(other.Elements());
                foreach (var key in segmentOf.Where(x => x.Value == other).Select(x => x.Key).ToList())
                    segmentOf[key] = segment;
                other.Remove();
            }
            var wireElement = WireElement(wire);
            var lastWire = segment.Elements("wire").LastOrDefault();
            if (lastWire is null)
                segment.Add(wireElement);
            else
                lastWire.AddAfterSelf(wireElement);
            segmentOf[wire] = segment;
        }

        // Pins no longer on the net: a part renamed (same gate and pin, same place) gets its name changed in place;
        // others are taken out.
        foreach (var pinElement in element.Descendants("pinref").Where(p => !net.PinRefs.Contains(PinOf(p))).ToList())
        {
            var old = PinOf(pinElement);
            var renamed = addedPins.FirstOrDefault(p => p.Gate == old.Gate && p.Pin == old.Pin
                && schematic.PinPosition(sheet, p) is { } at && SegmentTouching(at) == pinElement.Parent);
            if (renamed is not null)
            {
                pinElement.SetAttributeValue("part", renamed.Part);
                addedPins.Remove(renamed);
            }
            else
            {
                RemoveFromSegment(pinElement);
            }
        }

        foreach (var pin in addedPins)
        {
            if (schematic.PinPosition(sheet, pin) is not { } position || SegmentTouching(position) is not { } segment)
                return false;
            var pinElement = new XElement("pinref",
                new XAttribute("part", pin.Part), new XAttribute("gate", pin.Gate), new XAttribute("pin", pin.Pin));
            var lastPin = segment.Elements("pinref").LastOrDefault();
            if (lastPin is null)
                segment.AddFirst(pinElement);
            else
                lastPin.AddAfterSelf(pinElement);
        }

        foreach (var wire in known)
        {
            var e = (XElement)wire.Source!;
            SetNumber(e, "x1", wire.From.X);
            SetNumber(e, "y1", wire.From.Y);
            SetNumber(e, "x2", wire.To.X);
            SetNumber(e, "y2", wire.To.Y);
            if (e.Attribute("curve") is not null || wire.Curve != 0)
                SetNumber(e, "curve", wire.Curve);
        }
        foreach (var label in knownLabels)
        {
            var e = (XElement)label.Source!;
            SetNumber(e, "x", label.Position.X);
            SetNumber(e, "y", label.Position.Y);
            if (Rotation.Parse((string?)e.Attribute("rot")) != label.Rotation)
                e.SetAttributeValue("rot", label.Rotation == default ? null : label.Rotation.ToString());
        }
        for (var i = 0; i < junctionElements.Count; i++)
        {
            SetNumber(junctionElements[i], "x", net.Junctions[i].X);
            SetNumber(junctionElements[i], "y", net.Junctions[i].Y);
        }

        // New junctions (JUNCTION) and labels (LABEL) go into the segment they belong to, after its wires and dots.
        foreach (var junction in net.Junctions.Skip(junctionElements.Count))
        {
            if (SegmentTouching(junction) is not { } segment)
                return false;
            var after = segment.Elements().LastOrDefault(e => e.Name.LocalName is "wire" or "junction");
            var junctionElement = new XElement("junction", Coord("x", junction.X), Coord("y", junction.Y));
            if (after is null)
                segment.Add(junctionElement);
            else
                after.AddAfterSelf(junctionElement);
        }
        foreach (var label in addedLabels)
        {
            var segment = segmentOf.Where(x => x.Value.Parent == element)
                .MinBy(x => x.Key.DistanceTo(label.Position)).Value ?? element.Elements("segment").FirstOrDefault();
            if (segment is null)
                return false;
            segment.Add(LabelElement(label));
        }

        // Each segment must still be one connected piece; if a removal cut one in two, write the net anew.
        var wireOf = net.Wires.Where(w => w.Source is XElement).ToDictionary(w => (XElement)w.Source!);
        return element.Elements("segment").All(segment =>
            Net.ConnectedGroups(segment.Elements("wire").Select(e => wireOf[e]).ToList()).Count <= 1);
    }

    /// <summary>Takes an element out of its segment, and the segment out of the net when nothing is left in it.</summary>
    private static void RemoveFromSegment(XElement item)
    {
        var segment = item.Parent!;
        item.Remove();
        if (!segment.HasElements)
            segment.Remove();
    }

    private static bool SameNet(Net a, Net b) =>
        a.Name == b.Name && a.Class == b.Class && SameWires(a.Wires, b.Wires)
        && Canonical(a.PinRefs, p => $"{p.Part}/{p.Gate}/{p.Pin}").SequenceEqual(Canonical(b.PinRefs, p => $"{p.Part}/{p.Gate}/{p.Pin}"))
        && Canonical(a.Junctions, Key).SequenceEqual(Canonical(b.Junctions, Key))
        && Canonical(a.Labels, LabelKey).SequenceEqual(Canonical(b.Labels, LabelKey));

    private static bool SameWires(IEnumerable<Wire> a, IEnumerable<Wire> b) =>
        Canonical(a, WireKey).SequenceEqual(Canonical(b, WireKey));

    private static IEnumerable<string> Canonical<T>(IEnumerable<T> items, Func<T, string> key) => items.Select(key).Order(StringComparer.Ordinal);

    private static string Key(Point p) => $"{Number(p.X)},{Number(p.Y)}";

    private static string WireKey(Wire w) =>
        $"{Key(w.From)};{Key(w.To)};{Number(w.Width)};{w.Layer};{Number(w.Curve)};{w.Style};{w.Cap}";

    private static string LabelKey(Label l) => $"{Key(l.Position)};{Number(l.Size)};{l.Layer};{l.Rotation};{l.Align};{l.Xref}";

    /// <summary>Adds the layers that symbols added from library files use and the file does not define yet.</summary>
    private static void WriteMissingLayers(Schematic schematic, XElement drawing)
    {
        var layers = GetOrAdd(drawing, "layers", "settings", "grid");
        var present = layers.Elements("layer").Select(l => (int?)l.Attribute("number")).ToHashSet();
        foreach (var layer in schematic.Layers.Where(l => !present.Contains(l.Number)))
        {
            var element = new XElement("layer",
                new XAttribute("number", layer.Number), new XAttribute("name", layer.Name),
                new XAttribute("color", layer.Color), new XAttribute("fill", 1),
                new XAttribute("visible", layer.Visible ? "yes" : "no"), new XAttribute("active", "yes"));
            var after = layers.Elements("layer").LastOrDefault(l => (int?)l.Attribute("number") < layer.Number);
            if (after is null)
                layers.AddFirst(element);
            else
                after.AddAfterSelf(element);
        }
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
            if (present.Contains(key))
                continue;

            // A library first used through ADD has no XML of its own yet; its items are filled in below.
            libraries.Add(library.Source is XElement source
                ? new XElement(source)
                : new XElement("library",
                    new XAttribute("name", library.Name),
                    library.Urn.Length > 0 ? new XAttribute("urn", library.Urn) : null));
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
                part.Source = element;
                element.SetAttributeValue("name", part.Name);
                SetIfChanged(element, "technology", part.Technology.Length > 0 ? part.Technology : null);
                SetIfChanged(element, "value", part.Value);

                // The part's own attributes (VAL overrides, DNP, NO_BOM...), keeping those that did not change as they were.
                foreach (var child in element.Elements("attribute").ToList())
                {
                    var name = (string?)child.Attribute("name") ?? "";
                    if (!part.Attributes.TryGetValue(name, out var value))
                        child.Remove();
                    else
                        SetIfChanged(child, "value", value);
                }
                var existing = element.Elements("attribute").Select(a => (string?)a.Attribute("name")).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var firstOther = element.Elements().FirstOrDefault(e => e.Name != "attribute");
                foreach (var (name, value) in part.Attributes.Where(a => !existing.Contains(a.Key)))
                {
                    var attribute = new XElement("attribute", new XAttribute("name", name), new XAttribute("value", value));
                    if (firstOther is null)
                        element.Add(attribute);
                    else
                        firstOther.AddBeforeSelf(attribute);
                }
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
                    part.Value is null ? null : new XAttribute("value", part.Value),
                    part.Attributes.Select(a => new XElement("attribute", new XAttribute("name", a.Key), new XAttribute("value", a.Value))));
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
            if (existingSets.Contains(deviceSet.Name))
            {
                // Values added to a device's list show up as technologies the XML does not have yet.
                var element = deviceSets.Elements("deviceset").First(e => (string?)e.Attribute("name") == deviceSet.Name);
                foreach (var device in deviceSet.Devices.Values)
                {
                    var deviceElement = element.Element("devices")?.Elements("device").FirstOrDefault(e => ((string?)e.Attribute("name") ?? "") == device.Name);
                    if (deviceElement is not null)
                        AddMissingTechnologies(deviceElement, device);
                }
                continue;
            }
            if (deviceSet.Source is not XElement source)
                continue;
            var copied = new XElement(source);
            deviceSets.Add(copied);
            existingSets.Add(deviceSet.Name);

            // Values added since the library file was read are not in its XML yet.
            foreach (var device in deviceSet.Devices.Values)
            {
                var deviceElement = copied.Element("devices")?.Elements("device").FirstOrDefault(e => ((string?)e.Attribute("name") ?? "") == device.Name);
                if (deviceElement is not null)
                    AddMissingTechnologies(deviceElement, device);
            }

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

    /// <summary>Adds the device's technologies that its XML lacks, in name order like Eagle.</summary>
    public static void AddMissingTechnologies(XElement deviceElement, Device device)
    {
        var technologies = deviceElement.Element("technologies");
        if (technologies is null)
        {
            technologies = new XElement("technologies");
            deviceElement.Add(technologies);
        }
        var present = technologies.Elements("technology").Select(t => (string?)t.Attribute("name") ?? "").ToHashSet();
        foreach (var (name, attributes) in device.Technologies.Where(t => !present.Contains(t.Key)))
        {
            var technology = new XElement("technology", new XAttribute("name", name),
                attributes.Select(a => new XElement("attribute",
                    new XAttribute("name", a.Key), new XAttribute("value", a.Value),
                    a.Key.Equals("PACKAGE", StringComparison.OrdinalIgnoreCase) ? null : new XAttribute("constant", "no"))));
            var after = technologies.Elements("technology").LastOrDefault(t => string.CompareOrdinal((string?)t.Attribute("name") ?? "", name) < 0);
            if (after is null)
                technologies.AddFirst(technology);
            else
                after.AddAfterSelf(technology);
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

    /// <summary>Sets a coordinate only when the number differs, so "50.80" written by another program stays as it is.</summary>
    private static void SetNumber(XElement element, string name, double value)
    {
        if ((string?)element.Attribute(name) is { } text
            && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var old)
            && Math.Abs(old - value) < 1e-9)
            return;
        element.SetAttributeValue(name, Number(value));
    }

    /// <summary>Sets an XML attribute only when its value differs, so unchanged attributes keep their exact text.</summary>
    private static void SetIfChanged(XElement element, string name, string? value)
    {
        if ((string?)element.Attribute(name) != value)
            element.SetAttributeValue(name, value);
    }

    /// <summary>An instance, based on its original XML with position, orientation and smashed texts updated.</summary>
    private static XElement InstanceElement(Instance instance)
    {
        var element = instance.Source is XElement source
            ? new XElement(source)
            : new XElement("instance", new XAttribute("part", ""), new XAttribute("gate", instance.Gate));

        element.SetAttributeValue("part", instance.Part);
        SetNumber(element, "x", instance.Position.X);
        SetNumber(element, "y", instance.Position.Y);
        element.SetAttributeValue("smashed", instance.Smashed ? "yes" : null);

        // Texts the part no longer has (after unsmashing) are taken out.
        foreach (var child in element.Elements("attribute").Where(c => instance.Attributes.All(a => a.Name != (string?)c.Attribute("name"))).ToList())
            child.Remove();
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
                SetNumber(child, "x", p.X);
                SetNumber(child, "y", p.Y);
            }
            child.SetAttributeValue("rot", attribute.Rotation == default ? null : attribute.Rotation.ToString());
            child.SetAttributeValue("align", attribute.Align == Align.BottomLeft ? null : AlignName(attribute.Align));
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
        w.Source = element;
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
        l.Source = element;
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

    private sealed class Segment(List<Wire> wires)
    {
        public List<Wire> Wires { get; } = wires;
        public List<PinRef> Pins { get; } = new();
        public List<Point> Junctions { get; } = new();
        public List<Label> Labels { get; } = new();

        public bool Touches(Point p) => Wires.Any(w => w.DistanceTo(p) < GeometryMath.Epsilon);
    }
}
