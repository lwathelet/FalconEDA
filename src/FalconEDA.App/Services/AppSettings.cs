using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace FalconEDA.App.Services;

/// <summary>FalconEDA's settings, kept in %APPDATA%\FalconEDA\settings.json.</summary>
public sealed class AppSettings
{
    private static readonly string FilePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FalconEDA", "settings.json");

    /// <summary>Folders with library files (.lbr). Only local folders: FalconEDA has no cloud libraries.</summary>
    public List<string> LibraryFolders { get; set; } = new() { @"C:\Wattlet\R&D\eagle\lbr" };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) is { } settings)
                return settings;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // Damaged settings fall back to the defaults.
        }
        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
