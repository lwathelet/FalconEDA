namespace FalconEDA.Core;

/// <summary>NAME: renaming parts and nets across the whole schematic.</summary>
public sealed partial class Schematic
{
    /// <summary>Renames a part on every sheet, including the pins its nets connect to.</summary>
    public void RenamePart(string oldName, string newName)
    {
        if (!Parts.TryGetValue(oldName, out var part))
            throw new ArgumentException($"There is no part {oldName}.", nameof(oldName));
        if (Parts.ContainsKey(newName))
            throw new ArgumentException($"There is already a part called {newName}.", nameof(newName));

        Parts.Remove(oldName);
        Parts[newName] = part.Renamed(newName);
        foreach (var sheet in Sheets)
        {
            var touched = false;
            foreach (var instance in sheet.Instances.Where(i => i.Part == oldName))
            {
                instance.Part = newName;
                touched = true;
            }
            foreach (var net in sheet.Nets)
            {
                for (var i = 0; i < net.PinRefs.Count; i++)
                {
                    if (net.PinRefs[i].Part == oldName)
                    {
                        net.PinRefs[i] = net.PinRefs[i] with { Part = newName };
                        touched = true;
                    }
                }
            }
            if (touched)
                sheet.Modified = true;
        }
        PartsModified = true;
    }

    /// <summary>
    /// Renames a net on every sheet, or only on <paramref name="only"/>. Nets of the same name are one signal, so if
    /// <paramref name="newName"/> is already used, the two become one net; on a sheet where both exist they are merged.
    /// </summary>
    public void RenameNet(string oldName, string newName, Sheet? only = null)
    {
        if (!Sheets.Any(s => (only is null || s == only) && s.Nets.Any(n => n.Name == oldName)))
            throw new ArgumentException($"There is no net {oldName}.", nameof(oldName));

        foreach (var sheet in Sheets.Where(s => only is null || s == only))
        {
            if (sheet.Nets.FirstOrDefault(n => n.Name == oldName) is not { } net)
                continue;
            if (sheet.Nets.FirstOrDefault(n => n.Name == newName) is { } existing)
            {
                existing.Wires.AddRange(net.Wires);
                existing.Labels.AddRange(net.Labels);
                existing.Junctions.AddRange(net.Junctions);
                existing.PinRefs.AddRange(net.PinRefs.Where(p => !existing.PinRefs.Contains(p)));
                sheet.Nets.Remove(net);
            }
            else
            {
                net.Name = newName;
            }
            sheet.Modified = true;
        }
    }

    /// <summary>Whether a net of this name exists on any sheet.</summary>
    public bool HasNet(string name) => Sheets.Any(s => s.Nets.Any(n => n.Name == name));

    /// <summary>
    /// Renames one connected piece of a net: its wires, the pins and junctions on it and the labels next to it
    /// get the new name; the rest of the net keeps the old one. A piece that is the whole net on its sheet is
    /// renamed on that sheet.
    /// </summary>
    public void RenameNetPiece(Sheet sheet, Net net, IReadOnlyCollection<Wire> piece, string newName)
    {
        if (piece.Count == net.Wires.Count)
        {
            RenameNet(net.Name, newName, sheet);
            return;
        }

        var target = sheet.Nets.FirstOrDefault(n => n.Name == newName);
        if (target is null)
        {
            target = new Net { Name = newName, Class = net.Class };
            sheet.Nets.Add(target);
        }

        var rest = net.Wires.Except(piece).ToList();
        bool OnPiece(Point p) => piece.Any(w => w.DistanceTo(p) < GeometryMath.Epsilon);

        net.Wires.RemoveAll(piece.Contains);
        target.Wires.AddRange(piece);
        foreach (var label in net.Labels.Where(l => piece.Min(w => w.DistanceTo(l.Position)) < rest.Min(w => w.DistanceTo(l.Position))).ToList())
        {
            net.Labels.Remove(label);
            target.Labels.Add(label);
        }
        foreach (var junction in net.Junctions.Where(OnPiece).ToList())
        {
            net.Junctions.Remove(junction);
            target.Junctions.Add(junction);
        }
        foreach (var pin in net.PinRefs.Where(p => PinPosition(sheet, p) is { } at && OnPiece(at)).ToList())
        {
            net.PinRefs.Remove(pin);
            if (!target.PinRefs.Contains(pin))
                target.PinRefs.Add(pin);
        }
        sheet.Modified = true;
    }
}

/// <summary>What NAME can rename when a net is clicked: the piece, the net on the sheet, or the net on all sheets.</summary>
public enum NameScope { Piece, Sheet, AllSheets }

/// <summary>LABEL, JUNCTION and SMASH on a sheet.</summary>
public sealed partial class Sheet
{
    /// <summary>The net at <paramref name="p"/> and the connected piece of it that was clicked.</summary>
    public (Net Net, List<Wire> Piece)? NetPieceAt(Point p, double radius)
    {
        if (NetAt(p, radius) is not { } net)
            return null;
        var nearest = net.Wires.MinBy(w => w.DistanceTo(p))!;
        return (net, Net.ConnectedGroups(net.Wires).First(g => g.Contains(nearest)));
    }

    /// <summary>The net with a wire nearest to <paramref name="p"/>, within <paramref name="radius"/>.</summary>
    public Net? NetAt(Point p, double radius) => Nets
        .SelectMany(n => n.Wires.Select(w => (Net: n, Distance: w.DistanceTo(p))))
        .Where(x => x.Distance <= radius)
        .OrderBy(x => x.Distance)
        .Select(x => x.Net)
        .FirstOrDefault();

    public void AddLabel(Net net, Label label)
    {
        net.Labels.Add(label);
        Modified = true;
    }

    /// <summary>Places a junction dot where a net passes; false if there is no net or already a dot there.</summary>
    public bool AddJunction(Point p)
    {
        var net = Nets.FirstOrDefault(n => n.Touches(p));
        if (net is null || net.Junctions.Any(j => GeometryMath.SamePoint(j, p)))
            return false;
        net.Junctions.Add(p);
        Modified = true;
        return true;
    }

    /// <summary>
    /// SMASH: gives the part its own name and value texts, placed where the symbol has them, so they can be moved
    /// on their own. Unsmashing puts them back on the symbol. Either way the part keeps its attributes at the
    /// symbol's places: Eagle 9 shows a part's name and value only when its file entry has them.
    /// </summary>
    public void Smash(Instance instance, bool smash)
    {
        instance.Smashed = smash;
        if (_owner.Resolve(instance)?.Symbol is { } symbol)
        {
            instance.Attributes.Clear();
            instance.Attributes.AddRange(Symbols.SmashedAttributes(symbol, instance.Transform));
        }
        Modified = true;
    }

    /// <summary>The label or smashed text (name, value...) under <paramref name="p"/>.</summary>
    public object? TextAt(Point p, double radius) =>
        SheetView.Build(_owner, _owner.Sheets.IndexOf(this)).TextOwnerAt(p, radius);
}
