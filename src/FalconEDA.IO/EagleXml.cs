using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace FalconEDA.IO;

/// <summary>
/// Reads and writes Eagle's XML so that a file saved by FalconEDA differs from Eagle's only where something
/// was changed, which keeps version control diffs small: one element per line, no indentation, empty
/// elements closed with "/>", UTF-8 without byte order mark, the original XML declaration and doctype,
/// the original line endings (LF or CR LF) and a line break at the end.
/// </summary>
public static class EagleXml
{
    /// <summary>How the file was written, kept on its document: line ending, and whether it had a doctype.</summary>
    private sealed record SourceFormat(string NewLine, bool HasDoctype);

    public static XDocument Load(Stream stream)
    {
        var format = DetectFormat(stream);

        // Eagle files declare <!DOCTYPE eagle SYSTEM "eagle.dtd">; the DTD file is not needed to read them.
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
        using var reader = XmlReader.Create(stream, settings);
        var document = XDocument.Load(reader);
        document.AddAnnotation(format);
        return document;
    }

    /// <summary>A copy to change and save, keeping how the original file was written.</summary>
    public static XDocument Copy(XDocument document)
    {
        var copy = new XDocument(document);
        if (document.Annotation<SourceFormat>() is { } format)
            copy.AddAnnotation(format);
        return copy;
    }

    public static void Save(XDocument document, Stream stream)
    {
        var format = document.Annotation<SourceFormat>() ?? new SourceFormat("\n", HasDoctype: true);
        var newLine = format.NewLine;

        // The reader keeps the line breaks between elements as text. Next to new elements they would stop the
        // writer from putting each element on its own line, so remove them; text inside elements stays.
        document.DescendantNodes().OfType<XText>()
            .Where(t => string.IsNullOrWhiteSpace(t.Value) && (t.Parent is null || t.Parent.HasElements))
            .ToList()
            .ForEach(t => t.Remove());

        // Eagle files carry <!DOCTYPE eagle SYSTEM "eagle.dtd"> (a few older ones do not); the reader skips it, so put it back.
        document.DocumentType?.Remove();
        if (format.HasDoctype)
            document.AddFirst(new XDocumentType("eagle", null, "eagle.dtd", null));

        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = true,
            IndentChars = "",
            NewLineChars = newLine,
            NewLineHandling = NewLineHandling.Replace,
            OmitXmlDeclaration = true,
        };
        var text = new StringBuilder();
        using (var writer = XmlWriter.Create(text, settings))
            document.Save(writer);

        // .NET writes "<a b="c" />"; Eagle writes "<a b="c"/>". Inside text and attribute values '>' is
        // always escaped, so " />" only occurs at the end of empty elements.
        text.Replace(" />", "/>");

        var declaration = document.Declaration?.ToString() ?? "<?xml version=\"1.0\" encoding=\"utf-8\"?>";
        stream.Write(new UTF8Encoding(false).GetBytes(declaration + newLine + text + newLine));
    }

    private static SourceFormat DetectFormat(Stream stream)
    {
        if (!stream.CanSeek)
            return new SourceFormat("\n", HasDoctype: true);
        var start = stream.Position;
        var buffer = new byte[512];
        var count = stream.Read(buffer, 0, buffer.Length);
        stream.Position = start;
        var newLine = Array.IndexOf(buffer, (byte)'\n', 0, count);
        var hasDoctype = Encoding.UTF8.GetString(buffer, 0, count).Contains("<!DOCTYPE", StringComparison.Ordinal);
        return new SourceFormat(newLine > 0 && buffer[newLine - 1] == (byte)'\r' ? "\r\n" : "\n", hasDoctype);
    }
}
