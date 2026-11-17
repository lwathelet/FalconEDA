using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FalconEDA.Commands;
using FalconEDA.Core;
using FalconEDA.IO;

namespace FalconEDA.App.Services;

/// <summary>The library files in the library folders: their index for searching, and loaded libraries for ADD.</summary>
public sealed class LibraryCatalog : ILibraryProvider
{
    private static readonly string CacheFile =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FalconEDA", "library-index.json");

    private readonly Dictionary<string, Library?> _loaded = new(StringComparer.OrdinalIgnoreCase);

    public LibraryIndex Index { get; private set; } = LibraryIndex.Empty;

    public bool IsReady { get; private set; }

    /// <summary>Raised on the background thread while indexing, and when done.</summary>
    public event Action<int, int>? Progress;

    public event Action? Ready;

    /// <summary>Builds the index in the background; the first time this reads every library, later only changed ones.</summary>
    public Task BuildAsync(IEnumerable<string> folders)
    {
        var list = folders.ToList();
        IsReady = false;
        return Task.Run(() =>
        {
            var index = LibraryIndex.Build(list, CacheFile, new Progress<(int Done, int Total)>(p => Progress?.Invoke(p.Done, p.Total)));
            lock (_loaded)
            {
                Index = index;
                _loaded.Clear();
            }
            IsReady = true;
            Ready?.Invoke();
        });
    }

    public Library? Load(string name)
    {
        lock (_loaded)
        {
            if (_loaded.TryGetValue(name, out var cached))
                return cached;

            Library? library = null;
            if (Index.Paths.TryGetValue(name, out var path))
            {
                try
                {
                    library = EagleLibraryReader.Read(path);
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or System.Xml.XmlException)
                {
                    library = null;
                }
            }
            _loaded[name] = library;
            return library;
        }
    }

    public void SaveValues(string library, string deviceSet, Device device)
    {
        if (!Index.Paths.TryGetValue(library, out var path))
            throw new FileNotFoundException($"The library {library} is not in the library folders.");
        EagleLibraryWriter.AddTechnologies(path, deviceSet, device);
    }

    public IReadOnlyList<string> LibrariesWith(string fullName) => Index.Entries
        .Where(e => e.FullName.Equals(fullName, StringComparison.OrdinalIgnoreCase))
        .Select(e => e.Library)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
}
