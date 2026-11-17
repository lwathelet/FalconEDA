using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace FalconEDA.IO;

/// <summary>One device in a library file: what ADD and the library browser search.</summary>
public sealed record DeviceEntry(
    string Library,
    string LibraryPath,
    string DeviceSet,
    string Device,
    string Package,
    string Prefix,
    string Description,
    int Gates)
{
    /// <summary>The name ADD takes before the @: device set and device together, e.g. R-EU_R0402.</summary>
    public string FullName => DeviceSet + Device;

    /// <summary>The argument for ADD, e.g. R-EU_R0402@rcl.</summary>
    public string AddName => $"{FullName}@{Library}";
}

/// <summary>
/// The devices of all library files in the library folders. Reading hundreds of megabytes of libraries
/// takes a while, so what was found is kept in a cache file and only changed files are read again.
/// </summary>
public sealed class LibraryIndex
{
    private const int CacheVersion = 1;

    private LibraryIndex(IReadOnlyList<DeviceEntry> entries, IReadOnlyDictionary<string, string> paths)
    {
        Entries = entries;
        Paths = paths;
    }

    public static LibraryIndex Empty { get; } = new(Array.Empty<DeviceEntry>(), new Dictionary<string, string>());

    public IReadOnlyList<DeviceEntry> Entries { get; }

    /// <summary>Library file by library name (the file name without .lbr). Where two folders have the same name, the first folder wins.</summary>
    public IReadOnlyDictionary<string, string> Paths { get; }

    /// <summary>Indexes every .lbr in <paramref name="folders"/>, using and updating the cache file.</summary>
    public static LibraryIndex Build(IEnumerable<string> folders, string cacheFile, IProgress<(int Done, int Total)>? progress = null)
    {
        var cache = LoadCache(cacheFile);
        var files = folders.Where(Directory.Exists)
            .SelectMany(f => Directory.EnumerateFiles(f, "*.lbr"))
            .ToList();

        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<DeviceEntry>();
        var newCache = new Dictionary<string, CachedFile>();
        for (var i = 0; i < files.Count; i++)
        {
            var file = files[i];
            var library = Path.GetFileNameWithoutExtension(file);
            if (!paths.TryAdd(library, file))
                continue;

            var info = new FileInfo(file);
            if (!cache.TryGetValue(file, out var cached) || cached.Size != info.Length || cached.Modified != info.LastWriteTimeUtc.Ticks)
                cached = new CachedFile(info.Length, info.LastWriteTimeUtc.Ticks, ReadDevices(file, library));
            newCache[file] = cached;
            entries.AddRange(cached.Entries);
            progress?.Report((i + 1, files.Count));
        }

        SaveCache(cacheFile, newCache);
        return new LibraryIndex(entries, paths);
    }

    /// <summary>Devices whose name, library, package or description contain every word of <paramref name="text"/>.</summary>
    public IEnumerable<DeviceEntry> Search(string text)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return Entries.Where(e => words.All(w =>
            e.FullName.Contains(w, StringComparison.OrdinalIgnoreCase)
            || e.Library.Contains(w, StringComparison.OrdinalIgnoreCase)
            || e.Package.Contains(w, StringComparison.OrdinalIgnoreCase)
            || e.Description.Contains(w, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>Reads only the device sets of a library file, skipping symbols and packages.</summary>
    private static List<DeviceEntry> ReadDevices(string path, string library)
    {
        var entries = new List<DeviceEntry>();
        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, IgnoreWhitespace = true };
            using var reader = XmlReader.Create(path, settings);
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element)
                    continue;
                if (reader.Name is "packages" or "symbols" or "packages3d")
                {
                    reader.Skip();
                    continue;
                }
                if (reader.Name != "deviceset")
                    continue;

                var deviceSet = (XElement)XNode.ReadFrom(reader);
                var name = (string?)deviceSet.Attribute("name") ?? "";
                var prefix = (string?)deviceSet.Attribute("prefix") ?? "";
                var description = PlainText((string?)deviceSet.Element("description") ?? "");
                var gates = deviceSet.Element("gates")?.Elements("gate").Count() ?? 0;
                foreach (var device in deviceSet.Element("devices")?.Elements("device") ?? [])
                {
                    entries.Add(new DeviceEntry(library, path, name, (string?)device.Attribute("name") ?? "",
                        (string?)device.Attribute("package") ?? "", prefix, description, gates));
                }
            }
        }
        catch (XmlException)
        {
            // Old binary libraries and broken files are left out of the index.
        }
        return entries;
    }

    /// <summary>Eagle descriptions are small HTML documents; the index keeps a short plain-text version.</summary>
    private static string PlainText(string html)
    {
        var text = Regex.Replace(html, "<[^>]+>", " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, @"\s+", " ").Trim();
        return text.Length > 300 ? text[..300] : text;
    }

    private sealed record CachedFile(long Size, long Modified, List<DeviceEntry> Entries);

    private sealed record CacheContent(int Version, Dictionary<string, CachedFile> Files);

    private static Dictionary<string, CachedFile> LoadCache(string cacheFile)
    {
        try
        {
            if (File.Exists(cacheFile)
                && JsonSerializer.Deserialize<CacheContent>(File.ReadAllText(cacheFile)) is { Version: CacheVersion } content)
                return content.Files;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // A damaged cache is simply rebuilt.
        }
        return new Dictionary<string, CachedFile>();
    }

    private static void SaveCache(string cacheFile, Dictionary<string, CachedFile> files)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cacheFile)!);
            File.WriteAllText(cacheFile, JsonSerializer.Serialize(new CacheContent(CacheVersion, files)));
        }
        catch (IOException)
        {
            // Without a cache the next start only takes longer.
        }
    }
}
