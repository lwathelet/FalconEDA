using System.Runtime.CompilerServices;
using System.Xml.Linq;
using FalconEDA.Core;

namespace FalconEDA.IO;

/// <summary>Remembers the XML each schematic was read from, so the writer can keep everything it does not edit.</summary>
internal static class SourceDocuments
{
    private static readonly ConditionalWeakTable<Schematic, XDocument> Documents = new();

    public static void Set(Schematic schematic, XDocument document) => Documents.AddOrUpdate(schematic, document);

    public static XDocument? Get(Schematic schematic) => Documents.TryGetValue(schematic, out var document) ? document : null;
}
