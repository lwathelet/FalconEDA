using System.Xml.Linq;
using FalconEDA.Core;

namespace FalconEDA.IO;

/// <summary>Changes Eagle library files (.lbr) in place, keeping everything else exactly as it was.</summary>
public static class EagleLibraryWriter
{
    /// <summary>
    /// Writes the technologies (values) that <paramref name="device"/> has and the library file does not,
    /// e.g. after a value was added to a component's list. The previous file is kept as name.l#1.
    /// </summary>
    public static void AddTechnologies(string path, string deviceSet, Device device)
    {
        XDocument document;
        using (var stream = File.OpenRead(path))
            document = EagleXml.Load(stream);

        var deviceElement = document.Root?.Element("drawing")?.Element("library")?.Element("devicesets")?
            .Elements("deviceset").FirstOrDefault(e => (string?)e.Attribute("name") == deviceSet)?
            .Element("devices")?.Elements("device").FirstOrDefault(e => ((string?)e.Attribute("name") ?? "") == device.Name)
            ?? throw new InvalidDataException($"{Path.GetFileName(path)} has no device {deviceSet}{device.Name}.");

        EagleSchematicWriter.AddMissingTechnologies(deviceElement, device);
        SafeFile.Write(path, 'l', stream => EagleXml.Save(document, stream));
    }
}
