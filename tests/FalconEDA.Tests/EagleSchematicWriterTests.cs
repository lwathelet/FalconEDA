using System.Xml;
using FalconEDA.Commands;
using FalconEDA.Core;
using FalconEDA.IO;

namespace FalconEDA.Tests;

public sealed class EagleSchematicWriterTests : IDisposable
{
    private const string EagleDtd = @"C:\EAGLE 9.4.2\doc\eagle.dtd";
    private const string Examples = @"C:\EAGLE 9.4.2\examples";

    private readonly string _folder = Directory.CreateTempSubdirectory("falconeda-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static IEnumerable<string> ExampleFiles() => Directory.Exists(Examples)
        ? Directory.GetFiles(Examples, "*.sch", SearchOption.AllDirectories)
            .Where(f => File.ReadAllBytes(f).AsSpan(0, 1)[0] == (byte)'<')
        : [];

    /// <summary>
    /// Checks the file against Eagle's own DTD, when Eagle is installed. Eagle's own files
    /// already break a few DTD rules (attributes such as minversion), so with an
    /// <paramref name="original"/> only errors that file does not have count.
    /// </summary>
    private void AssertValid(string path, string? original = null)
    {
        if (!File.Exists(EagleDtd))
            return;

        var allowed = original is null ? new HashSet<string>() : DtdErrors(original).ToHashSet();
        var errors = DtdErrors(path).Where(e => !allowed.Contains(e)).ToList();
        Assert.True(errors.Count == 0, string.Join("\n", errors.Take(10)));
    }

    private List<string> DtdErrors(string path)
    {
        var folder = Path.Combine(_folder, "validate");
        Directory.CreateDirectory(folder);
        var copy = Path.Combine(folder, "file.sch");
        File.Copy(path, copy, overwrite: true);
        File.Copy(EagleDtd, Path.Combine(folder, "eagle.dtd"), overwrite: true);

        var errors = new List<string>();
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Parse,
            ValidationType = ValidationType.DTD,
            XmlResolver = new XmlUrlResolver(),
        };
        settings.ValidationEventHandler += (_, e) => errors.Add($"{e.Severity}: {e.Message}");
        using (var reader = XmlReader.Create(copy, settings))
        {
            while (reader.Read()) { }
        }
        return errors;
    }

    [Fact]
    public void New_schematic_is_valid_and_reads_back()
    {
        var editor = new Editor();
        foreach (var command in CommandParser.Parse("GRID MM 1; NET (0 0) (10 0) (10 10); NET VCC (0 20) (10 20);"))
            CommandExecutor.Execute(editor, command);

        var path = Path.Combine(_folder, "new.sch");
        EagleSchematicWriter.Save(editor.Schematic, path);

        AssertValid(path);
        var read = EagleSchematicReader.Read(path);
        Assert.Equal(new[] { "N$1", "VCC" }, read.Sheets[0].Nets.Select(n => n.Name));
        Assert.Equal(3, read.Sheets[0].Nets.Sum(n => n.Wires.Count));
    }

    [Fact]
    public void Unchanged_examples_round_trip_without_losing_anything()
    {
        foreach (var file in ExampleFiles())
        {
            var original = EagleSchematicReader.Read(file);
            var path = Path.Combine(_folder, Path.GetFileName(file));
            EagleSchematicWriter.Save(original, path);

            AssertValid(path, file);
            var copy = EagleSchematicReader.Read(path);
            Assert.Equal(original.Parts.Count, copy.Parts.Count);
            Assert.Equal(original.Libraries.Count, copy.Libraries.Count);
            Assert.Equal(original.Sheets.Count, copy.Sheets.Count);
            for (var i = 0; i < original.Sheets.Count; i++)
            {
                Assert.Equal(original.Sheets[i].Instances.Count, copy.Sheets[i].Instances.Count);
                Assert.Equal(original.Sheets[i].AllWires().Count(), copy.Sheets[i].AllWires().Count());
            }
        }
    }

    [Fact]
    public void Edited_sheet_keeps_pins_labels_and_parts()
    {
        var file = ExampleFiles().FirstOrDefault(f => f.EndsWith("Arduino_MEGA2560_ref.sch"));
        if (file is null)
            return;

        var schematic = EagleSchematicReader.Read(file);
        var sheet = schematic.Sheets[0];
        var pinsBefore = sheet.Nets.Sum(n => n.PinRefs.Count);
        var labelsBefore = sheet.Nets.Sum(n => n.Labels.Count);
        sheet.AddNet(new[] { new Point(-50, -50), new Point(-40, -50) }, name: "FALCON_TEST");

        var path = Path.Combine(_folder, "edited.sch");
        EagleSchematicWriter.Save(schematic, path);

        AssertValid(path, file);
        var copy = EagleSchematicReader.Read(path).Sheets[0];
        Assert.Contains(copy.Nets, n => n.Name == "FALCON_TEST");
        Assert.Equal(pinsBefore, copy.Nets.Sum(n => n.PinRefs.Count));
        Assert.Equal(labelsBefore, copy.Nets.Sum(n => n.Labels.Count));
        Assert.Equal(sheet.Instances.Count, copy.Instances.Count);
    }

    [Fact]
    public void Saving_keeps_backups_like_eagle()
    {
        var schematic = Schematic.CreateNew();
        var path = Path.Combine(_folder, "backup.sch");

        EagleSchematicWriter.Save(schematic, path);
        EagleSchematicWriter.Save(schematic, path);
        EagleSchematicWriter.Save(schematic, path);

        Assert.True(File.Exists(Path.Combine(_folder, "backup.s#1")));
        Assert.True(File.Exists(Path.Combine(_folder, "backup.s#2")));
        Assert.False(File.Exists(Path.Combine(_folder, "backup.s#3")));
    }

    [Fact]
    public void Saving_an_unchanged_file_again_gives_the_same_bytes()
    {
        var first = Path.Combine(_folder, "first.sch");
        var schematic = Schematic.CreateNew();
        schematic.Sheets[0].AddNet(new[] { new Point(0, 0), new Point(10, 0) });
        EagleSchematicWriter.Save(schematic, first);

        var second = Path.Combine(_folder, "second.sch");
        EagleSchematicWriter.Save(EagleSchematicReader.Read(first), second);

        Assert.Equal(File.ReadAllBytes(first), File.ReadAllBytes(second));
    }

    [Fact]
    public void Saving_an_eagle_file_without_changes_gives_exactly_the_same_bytes()
    {
        // Keeps version control diffs to what was really changed.
        foreach (var file in ExampleFiles())
        {
            var path = Path.Combine(_folder, "same.sch");
            EagleSchematicWriter.Save(EagleSchematicReader.Read(file), path);

            Assert.True(File.ReadAllBytes(file).AsSpan().SequenceEqual(File.ReadAllBytes(path)), Path.GetFileName(file));
        }
    }

    /// <summary>Lines between the first and last difference, counted in both files: right for one local change.</summary>
    private static int ChangedLines(string a, string b)
    {
        var before = File.ReadAllLines(a);
        var after = File.ReadAllLines(b);
        var prefix = 0;
        while (prefix < Math.Min(before.Length, after.Length) && before[prefix] == after[prefix])
            prefix++;
        var suffix = 0;
        while (suffix < Math.Min(before.Length, after.Length) - prefix && before[^(suffix + 1)] == after[^(suffix + 1)])
            suffix++;
        return before.Length - prefix - suffix + (after.Length - prefix - suffix);
    }

    [Fact]
    public void Moving_a_part_and_back_gives_the_original_file()
    {
        foreach (var file in ExampleFiles())
        {
            var editor = new Editor();
            editor.Load(EagleSchematicReader.Read(file), file);
            var part = editor.Sheet.Instances.FirstOrDefault(i => editor.Schematic.Resolve(i) is not null);
            if (part is null)
                continue;
            var (x, y) = (part.Position.X, part.Position.Y);
            string F(double v) => v.ToString(System.Globalization.CultureInfo.InvariantCulture);

            var wires = editor.Sheet.AllWires().Count();
            foreach (var command in CommandParser.Parse($"GRID MM 0.01; MOVE {part.Part} ({F(x + 5.08)} {F(y)})"))
                CommandExecutor.Execute(editor, command);

            // A wire that shrank to nothing on the way is removed (as Eagle's optimizing does), so going back
            // cannot restore it; this only holds when no wire disappeared.
            if (editor.Sheet.AllWires().Count() < wires)
                continue;
            var path = Path.Combine(_folder, "moved.sch");
            EagleSchematicWriter.Save(editor.Schematic, path);

            foreach (var command in CommandParser.Parse($"MOVE {part.Part} ({F(x)} {F(y)})"))
                CommandExecutor.Execute(editor, command);
            EagleSchematicWriter.Save(editor.Schematic, path);

            Assert.True(File.ReadAllBytes(file).AsSpan().SequenceEqual(File.ReadAllBytes(path)), Path.GetFileName(file));
        }
    }

    [Fact]
    public void Changing_a_value_changes_only_that_part_in_the_file()
    {
        var file = ExampleFiles().FirstOrDefault(f => f.EndsWith("Arduino_MEGA2560_ref.sch"));
        if (file is null)
            return;
        var editor = new Editor();
        editor.Load(EagleSchematicReader.Read(file), file);
        var part = editor.Schematic.Parts.Values.First(p => p.Name == "R1");

        foreach (var command in CommandParser.Parse("VALUE R1 4K7; CHANGE DNP ON R1;"))
            CommandExecutor.Execute(editor, command);
        var path = Path.Combine(_folder, "value.sch");
        EagleSchematicWriter.Save(editor.Schematic, path);

        // The part's line, plus its DNP attribute and closing tag.
        Assert.True(ChangedLines(file, path) <= 4, $"{ChangedLines(file, path)} lines changed");
    }
}
