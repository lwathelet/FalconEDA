using FalconEDA.Core;

namespace FalconEDA.IO;

/// <summary>Reads Eagle library files (.lbr). The library is named after the file, as in Eagle.</summary>
public static class EagleLibraryReader
{
    public static Library Read(string path)
    {
        using var stream = File.OpenRead(path);
        if (EagleSchematicReader.IsBinaryFormat(stream))
            throw new InvalidDataException(
                $"{Path.GetFileName(path)} uses the binary format of EAGLE 5 or earlier. Open it in EAGLE 6 or later and save it to convert it to XML.");

        var document = EagleSchematicReader.LoadXml(stream);
        var drawing = document.Root?.Element("drawing") ?? throw new InvalidDataException($"{Path.GetFileName(path)} is not an Eagle file.");
        var element = drawing.Element("library") ?? throw new InvalidDataException($"{Path.GetFileName(path)} is not an Eagle library.");

        var library = EagleSchematicReader.ReadLibrary(element, Path.GetFileNameWithoutExtension(path));

        // A library file is never copied into a schematic as a whole, only the items that are used.
        library.Source = null;
        library.Layers.AddRange(EagleSchematicReader.ReadLayers(drawing));
        return library;
    }
}
