using FalconEDA.Commands;
using FalconEDA.Core;
using FalconEDA.IO;

namespace FalconEDA.Tests;

/// <summary>A component's list of values (VAL, MNF, REF as technologies), and the DNP and NO_BOM flags.</summary>
public sealed class ValueTests : IDisposable
{
    private const string LibraryXml = """
        <?xml version="1.0" encoding="utf-8"?>
        <!DOCTYPE eagle SYSTEM "eagle.dtd">
        <eagle version="9.4.2">
        <drawing>
        <layers>
        <layer number="94" name="Symbols" color="4" fill="1" visible="yes" active="yes"/>
        <layer number="95" name="Names" color="7" fill="1" visible="yes" active="yes"/>
        <layer number="96" name="Values" color="7" fill="1" visible="yes" active="yes"/>
        </layers>
        <library>
        <packages>
        <package name="R0402"><smd name="1" x="-0.5" y="0" dx="0.6" dy="0.6" layer="1"/></package>
        <package name="SO8"><smd name="1" x="-2" y="-2" dx="0.6" dy="1.5" layer="1"/></package>
        </packages>
        <symbols>
        <symbol name="R">
        <wire x1="-2.54" y1="-0.889" x2="2.54" y2="-0.889" width="0.254" layer="94"/>
        <text x="-3.81" y="1.4986" size="1.778" layer="95">&gt;NAME</text>
        <text x="-3.81" y="-3.302" size="1.778" layer="96">&gt;VAL</text>
        <pin name="1" x="-5.08" y="0" visible="off" length="short"/>
        <pin name="2" x="5.08" y="0" visible="off" length="short" rot="R180"/>
        </symbol>
        <symbol name="IC">
        <wire x1="-5" y1="5" x2="5" y2="0" width="0.4" layer="94"/>
        <text x="0" y="6" size="1.778" layer="96">&gt;VALUE</text>
        </symbol>
        </symbols>
        <devicesets>
        <deviceset name="R-EU_" prefix="R" uservalue="yes">
        <gates><gate name="G$1" symbol="R" x="0" y="0"/></gates>
        <devices><device name="R0402" package="R0402">
        <technologies>
        <technology name="">
        <attribute name="MNF" value="" constant="no"/>
        <attribute name="PACKAGE" value="R0402"/>
        <attribute name="REF" value="" constant="no"/>
        <attribute name="VAL" value="" constant="no"/>
        </technology>
        <technology name="100K_">
        <attribute name="MNF" value="Yageo RC0402FR-07100KL" constant="no"/>
        <attribute name="PACKAGE" value="R0402"/>
        <attribute name="REF" value="000008" constant="no"/>
        <attribute name="VAL" value="100K" constant="no"/>
        </technology>
        <technology name="10K_">
        <attribute name="MNF" value="Yageo RC0402FR-0710KL" constant="no"/>
        <attribute name="PACKAGE" value="R0402"/>
        <attribute name="REF" value="000007" constant="no"/>
        <attribute name="VAL" value="10K" constant="no"/>
        </technology>
        </technologies>
        </device></devices>
        </deviceset>
        <deviceset name="OPAMP" prefix="IC">
        <gates><gate name="G$1" symbol="IC" x="0" y="0"/></gates>
        <devices><device name="" package="SO8"><technologies><technology name=""/></technologies></device></devices>
        </deviceset>
        </devicesets>
        </library>
        </drawing>
        </eagle>
        """;

    private readonly string _folder = Directory.CreateTempSubdirectory("falconeda-values-").FullName;
    private readonly string _libraryFile;
    private readonly Editor _editor = new();

    public ValueTests()
    {
        _libraryFile = Path.Combine(_folder, "vals.lbr");
        // Saved once the way Eagle writes libraries, so the test sees only what adding a value changes.
        using (var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(LibraryXml)))
        using (var file = File.Create(_libraryFile))
            EagleXml.Save(EagleXml.Load(stream), file);
        _editor.LibraryProvider = new FolderLibraries(_folder);
        Run("GRID MM 1;");
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private sealed class FolderLibraries(string folder) : ILibraryProvider
    {
        private readonly LibraryIndex _index = LibraryIndex.Build([folder], Path.Combine(folder, "index.json"));

        public Library? Load(string name) => _index.Paths.TryGetValue(name, out var path) ? EagleLibraryReader.Read(path) : null;

        public IReadOnlyList<string> LibrariesWith(string fullName) =>
            _index.Entries.Where(e => e.FullName == fullName).Select(e => e.Library).Distinct().ToList();

        public void SaveValues(string library, string deviceSet, Device device) =>
            EagleLibraryWriter.AddTechnologies(_index.Paths[library], deviceSet, device);
    }

    private void Run(string text)
    {
        foreach (var command in CommandParser.Parse(text))
            CommandExecutor.Execute(_editor, command);
    }

    private Part R1 => _editor.Schematic.Parts["R1"];

    private List<string> Texts() => SheetView.Build(_editor.Schematic, 0).Texts.Select(t => t.Value).ToList();

    [Fact]
    public void Add_with_a_value_sets_the_technology_and_shows_the_value()
    {
        Run("ADD R-EU_R0402@vals VALUE=100K (0 0);");

        Assert.Equal("100K_", R1.Technology);
        Assert.Equal("100K", R1.Value);
        Assert.Contains("100K", Texts());
    }

    [Fact]
    public void Value_picks_from_the_list_and_undo_puts_the_old_one_back()
    {
        Run("ADD R-EU_R0402@vals VALUE=100K (0 0); VALUE R1 10K;");

        Assert.Equal("10K_", R1.Technology);
        Assert.Contains("10K", Texts());

        Run("UNDO;");
        Assert.Equal("100K_", R1.Technology);
    }

    [Fact]
    public void A_value_not_in_the_list_needs_a_manufacturer_and_reference()
    {
        Run("ADD R-EU_R0402@vals (0 0);");

        var ex = Assert.Throws<CommandException>(() => Run("VALUE R1 47K;"));
        Assert.Contains("not in the list", ex.Message);
    }

    [Fact]
    public void A_new_value_is_added_to_the_library_file_without_changing_the_rest()
    {
        var before = File.ReadAllLines(_libraryFile);
        Run("ADD R-EU_R0402@vals (0 0); VALUE R1 47K 'Vishay CRCW040247K0FKED' '000123';");

        Assert.Equal("47K_", R1.Technology);
        Assert.Equal("47K", R1.Value);

        var library = EagleLibraryReader.Read(_libraryFile);
        var option = Assert.Single(Values.Of(library.DeviceSets["R-EU_"].Devices["R0402"]), v => v.Val == "47K");
        Assert.Equal(new ValueOption("47K_", "47K", "Vishay CRCW040247K0FKED", "000123"), option);

        // Only lines were added: the old file is still there, line for line, and kept as a backup.
        var after = File.ReadAllLines(_libraryFile).ToList();
        var added = after.IndexOf("<technology name=\"47K_\">");
        Assert.Equal("<technology name=\"10K_\">", after[added - 6]);
        after.RemoveRange(added, 6);
        Assert.Equal(before, after);
        Assert.True(File.Exists(Path.Combine(_folder, "vals.l#1")));
    }

    [Fact]
    public void Parts_without_a_list_take_a_typed_value_like_eagle()
    {
        Run("ADD OPAMP@vals (0 0); VALUE IC1 LM358;");

        Assert.Equal("LM358", _editor.Schematic.Parts["IC1"].Value);
        Assert.Contains("LM358", Texts());
    }

    [Fact]
    public void Dnp_shows_after_the_value_and_switches_off_again()
    {
        Run("ADD R-EU_R0402@vals VALUE=100K (0 0); DNP R1;");

        Assert.True(Values.IsDnp(R1));
        Assert.Contains("100K DNP", Texts());

        Run("DNP R1;");
        Assert.False(Values.IsDnp(R1));
    }

    [Fact]
    public void Dnp_and_no_bom_act_on_the_group()
    {
        Run("ADD R-EU_R0402@vals VALUE=100K (0 0); ADD R-EU_R0402@vals VALUE=10K (10 0); GROUP ALL; DNP;");

        Assert.All(_editor.Schematic.Parts.Values, p => Assert.True(Values.IsDnp(p)));

        Run("DNP;");
        Assert.All(_editor.Schematic.Parts.Values, p => Assert.False(Values.IsDnp(p)));

        Run("NO_BOM ON;");
        Assert.All(_editor.Schematic.Parts.Values, p => Assert.True(Values.IsNoBom(p)));
    }

    [Fact]
    public void Values_and_flags_are_saved()
    {
        Run("ADD R-EU_R0402@vals (0 0); VALUE R1 47K 'Vishay' '000123'; DNP R1; NO_BOM R1;");
        var path = Path.Combine(_folder, "values.sch");

        EagleSchematicWriter.Save(_editor.Schematic, path);
        var read = EagleSchematicReader.Read(path);

        var part = read.Parts["R1"];
        Assert.Equal("47K_", part.Technology);
        Assert.Equal("47K", part.Value);
        Assert.True(Values.IsDnp(part));
        Assert.True(Values.IsNoBom(part));
        Assert.Contains(Values.Of(CommandExecutor.DeviceOf(read, part)!), v => v.Val == "47K" && v.Ref == "000123");
        Assert.Contains("47K DNP", SheetView.Build(read, 0).Texts.Select(t => t.Value));
    }
}
