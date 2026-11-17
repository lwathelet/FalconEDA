using FalconEDA.Commands;
using FalconEDA.Core;
using FalconEDA.IO;

namespace FalconEDA.Tests;

/// <summary>ADD with a small library file: a resistor (device set R, device 0402) and a dual op-amp with two gates.</summary>
public sealed class AddTests : IDisposable
{
    private const string LibraryXml = """
        <?xml version="1.0" encoding="utf-8"?>
        <!DOCTYPE eagle SYSTEM "eagle.dtd">
        <eagle version="9.4.2">
        <drawing>
        <layers>
        <layer number="1" name="Top" color="4" fill="1" visible="yes" active="yes"/>
        <layer number="94" name="Symbols" color="4" fill="1" visible="yes" active="yes"/>
        <layer number="95" name="Names" color="7" fill="1" visible="yes" active="yes"/>
        <layer number="96" name="Values" color="7" fill="1" visible="yes" active="yes"/>
        <layer number="150" name="Notes" color="7" fill="1" visible="yes" active="yes"/>
        </layers>
        <library>
        <packages>
        <package name="0402"><smd name="1" x="-0.5" y="0" dx="0.6" dy="0.6" layer="1"/><smd name="2" x="0.5" y="0" dx="0.6" dy="0.6" layer="1"/></package>
        <package name="SO8"><smd name="1" x="-2" y="-2" dx="0.6" dy="1.5" layer="1"/></package>
        <package name="UNUSED"><smd name="1" x="0" y="0" dx="1" dy="1" layer="1"/></package>
        </packages>
        <symbols>
        <symbol name="R">
        <wire x1="-2.54" y1="-0.889" x2="2.54" y2="-0.889" width="0.254" layer="94"/>
        <wire x1="-2.54" y1="0" x2="2.54" y2="0" width="0.254" layer="150"/>
        <text x="-3.81" y="1.4986" size="1.778" layer="95">&gt;NAME</text>
        <pin name="1" x="-5.08" y="0" visible="off" length="short" direction="pas"/>
        <pin name="2" x="5.08" y="0" visible="off" length="short" direction="pas" rot="R180"/>
        </symbol>
        <symbol name="OPAMP">
        <wire x1="-5" y1="5" x2="5" y2="0" width="0.4" layer="94"/>
        <pin name="OUT" x="7.62" y="0" length="short" rot="R180"/>
        </symbol>
        <symbol name="UNUSED"><wire x1="0" y1="0" x2="1" y2="1" width="0.1" layer="94"/></symbol>
        </symbols>
        <devicesets>
        <deviceset name="R" prefix="R" uservalue="yes">
        <gates><gate name="G$1" symbol="R" x="0" y="0"/></gates>
        <devices><device name="0402" package="0402">
        <connects><connect gate="G$1" pin="1" pad="1"/><connect gate="G$1" pin="2" pad="2"/></connects>
        <technologies><technology name=""/></technologies>
        </device></devices>
        </deviceset>
        <deviceset name="LM358" prefix="IC">
        <gates><gate name="A" symbol="OPAMP" x="0" y="10"/><gate name="B" symbol="OPAMP" x="0" y="-10"/></gates>
        <devices><device name="D" package="SO8">
        <connects><connect gate="A" pin="OUT" pad="1"/></connects>
        <technologies><technology name=""/></technologies>
        </device></devices>
        </deviceset>
        <deviceset name="UNUSED">
        <gates><gate name="G$1" symbol="UNUSED" x="0" y="0"/></gates>
        <devices><device name="" package="UNUSED"><technologies><technology name=""/></technologies></device></devices>
        </deviceset>
        </devicesets>
        </library>
        </drawing>
        </eagle>
        """;

    private readonly string _folder = Directory.CreateTempSubdirectory("falconeda-add-").FullName;
    private readonly Editor _editor = new();

    public AddTests()
    {
        File.WriteAllText(Path.Combine(_folder, "testlib.lbr"), LibraryXml);
        _editor.LibraryProvider = new FolderLibraries(_folder);
        Run("GRID MM 1;");
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private sealed class FolderLibraries(string folder) : ILibraryProvider
    {
        private readonly LibraryIndex _index = LibraryIndex.Build([folder], Path.Combine(folder, "index.json"));

        public Library? Load(string name) =>
            _index.Paths.TryGetValue(name, out var path) ? EagleLibraryReader.Read(path) : null;

        public void SaveValues(string library, string deviceSet, Device device) =>
            EagleLibraryWriter.AddTechnologies(_index.Paths[library], deviceSet, device);

        public IReadOnlyList<string> LibrariesWith(string fullName) =>
            _index.Entries.Where(e => e.FullName == fullName).Select(e => e.Library).Distinct().ToList();
    }

    private void Run(string text)
    {
        foreach (var command in CommandParser.Parse(text))
            CommandExecutor.Execute(_editor, command);
    }

    [Fact]
    public void Add_places_a_named_part_with_only_its_device_set_in_the_schematic()
    {
        Run("ADD R0402@testlib (10 20);");

        var instance = Assert.Single(_editor.Sheet.Instances);
        Assert.Equal("R1", instance.Part);
        Assert.Equal(new Point(10, 20), instance.Position);
        Assert.NotNull(_editor.Schematic.Resolve(instance));
        var library = _editor.Schematic.Libraries["testlib"];
        Assert.Equal(new[] { "R" }, library.DeviceSets.Keys);
        Assert.Equal(new[] { "R" }, library.Symbols.Keys);
        Assert.Contains(_editor.Schematic.Layers, l => l.Number == 150);

        // Placed "smashed" like Eagle 9: the name gets an attribute where the symbol's >NAME text is.
        Assert.True(instance.Smashed);
        var name = Assert.Single(instance.Attributes);
        Assert.Equal("NAME", name.Name);
        Assert.True(GeometryMath.SamePoint(new Point(10 - 3.81, 20 + 1.4986), name.Position!.Value));
    }

    [Fact]
    public void Add_without_library_finds_the_device()
    {
        Run("ADD R0402 (0 0);");

        Assert.Equal("testlib", _editor.Schematic.Parts["R1"].LibraryKey);
    }

    [Fact]
    public void Add_accepts_name_and_orientation_like_eagle()
    {
        Run("ADD 'RSHUNT' R90 R0402@testlib (0 0);");

        var instance = Assert.Single(_editor.Sheet.Instances);
        Assert.Equal("RSHUNT", instance.Part);
        Assert.Equal(new Rotation(90), instance.Rotation);
    }

    [Fact]
    public void Unknown_device_gives_a_clear_message()
    {
        var ex = Assert.Throws<CommandException>(() => Run("ADD NOPE@testlib;"));
        Assert.Contains("no device NOPE", ex.Message);
    }

    [Fact]
    public void Gates_are_placed_one_after_another_then_the_next_part_follows()
    {
        Run("ADD LM358D@testlib;");
        var tool = Assert.IsType<AddTool>(_editor.Tool);

        tool.Click(new Point(0, 0), 0, 1);
        tool.Click(new Point(20, 0), 0, 1);
        tool.Click(new Point(40, 0), 0, 1);

        Assert.Equal(new[] { ("IC1", "A"), ("IC1", "B"), ("IC2", "A") },
            _editor.Sheet.Instances.Select(i => (i.Part, i.Gate)));
    }

    [Fact]
    public void Undo_removes_the_added_part()
    {
        Run("ADD R0402@testlib (10 20); UNDO;");

        Assert.Empty(_editor.Sheet.Instances);
        Assert.Empty(_editor.Schematic.Parts);
    }

    [Fact]
    public void Saved_schematic_contains_only_the_used_library_items()
    {
        Run("ADD R0402@testlib (10 20); ADD R0402@testlib (20 20);");
        var path = Path.Combine(_folder, "added.sch");

        EagleSchematicWriter.Save(_editor.Schematic, path);

        var text = File.ReadAllText(path);
        Assert.Contains("<package name=\"0402\"", text);
        Assert.DoesNotContain("UNUSED", text);
        Assert.Contains("<layer number=\"150\"", text);

        var read = EagleSchematicReader.Read(path);
        Assert.Equal(new[] { "R1", "R2" }, read.Parts.Keys.Order());
        Assert.All(read.Sheets[0].Instances, i => Assert.NotNull(read.Resolve(i)));
    }
}
