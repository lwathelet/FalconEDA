using System.Text;
using FalconEDA.Commands;
using FalconEDA.Core;
using FalconEDA.IO;

namespace FalconEDA.Tests;

public class LibraryMergeTests
{
    private static Editor Open(string xml)
    {
        var editor = new Editor();
        editor.Load(EagleSchematicReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(xml))), null);
        return editor;
    }

    [Fact]
    public void Pasting_from_another_version_of_a_library_adds_what_is_missing_and_saves_it()
    {
        // Another schematic whose library "test" has a device set RX (with symbol RX) that the target's "test" lacks.
        var otherXml = EagleSchematicReaderTests.Sample
            .Replace("symbol name=\"R\"", "symbol name=\"RX\"")
            .Replace("symbol=\"R\"", "symbol=\"RX\"")
            .Replace("deviceset name=\"R\"", "deviceset name=\"RX\"")
            .Replace("deviceset=\"R\"", "deviceset=\"RX\"");
        var other = Open(otherXml);
        other.Selection.Instances.Add(other.Sheet.Instances[0]);
        CommandExecutor.Execute(other, new Command("COPY", []));

        var target = Open(EagleSchematicReaderTests.Sample);
        target.Clipboard = other.Clipboard;
        CommandExecutor.Execute(target, new Command("PASTE", [new CoordinateArgument(new Point(10, 10))]));

        var pasted = target.Sheet.Instances.Single(i => i.Part == "R2");
        Assert.NotNull(target.Schematic.Resolve(pasted));
        Assert.Contains("R", target.Schematic.Libraries["test"].DeviceSets.Keys);

        var path = Path.Combine(Path.GetTempPath(), $"falconeda-lib-{Guid.NewGuid():N}.sch");
        try
        {
            EagleSchematicWriter.Save(target.Schematic, path);
            var read = EagleSchematicReader.Read(path);
            Assert.NotNull(read.Resolve(read.Sheets[0].Instances.Single(i => i.Part == "R2")));
            Assert.NotNull(read.Resolve(read.Sheets[0].Instances.Single(i => i.Part == "R1")));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
