namespace FalconEDA.IO;

/// <summary>Replaces a file safely: writes a temporary file first, then keeps the old one as a numbered backup, as Eagle does.</summary>
internal static class SafeFile
{
    /// <summary>Eagle keeps the previous versions as name.s#1 (newest) to name.s#9 for schematics, name.l#1... for libraries.</summary>
    public const int BackupCount = 9;

    public static void Write(string path, char backupLetter, Action<Stream> write)
    {
        var temp = path + ".tmp";
        using (var stream = File.Create(temp))
            write(stream);

        if (File.Exists(path))
        {
            File.Delete(BackupPath(path, backupLetter, BackupCount));
            for (var i = BackupCount - 1; i >= 1; i--)
            {
                if (File.Exists(BackupPath(path, backupLetter, i)))
                    File.Move(BackupPath(path, backupLetter, i), BackupPath(path, backupLetter, i + 1));
            }
            File.Copy(path, BackupPath(path, backupLetter, 1), overwrite: true);
        }
        File.Move(temp, path, overwrite: true);
    }

    private static string BackupPath(string path, char letter, int number) => Path.ChangeExtension(path, $".{letter}#{number}");
}
