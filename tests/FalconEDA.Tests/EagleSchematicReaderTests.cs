using System.Text;
using FalconCAD.Core;
using FalconCAD.IO;

namespace FalconCAD.Tests;

public class EagleSchematicReaderTests
{
    private const string Sample = """
        <?xml version="1.0" encoding="utf-8"?>
        <!DOCTYPE eagle SYSTEM "eagle.dtd">
        <eagle version="9.4.2">
        <drawing>
        <layers>
        <layer number="91" name="Nets" color="2" fill="1" visible="yes" active="yes"/>
        <layer number="94" name="Symbols" color="4" fill="1" visible="yes" active="yes"/>
        </layers>
        <schematic>
        <libraries>
        <library name="test">
        <symbols>
        <symbol name="R">
        <wire x1="-2.54" y1="-0.889" x2="2.54" y2="-0.889" width="0.254" layer="94"/>
        <text x="-3.81" y="1.4986" size="1.778" layer="95">&gt;NAME</text>
        <text x="-3.81" y="-3.302" size="1.778" layer="96">&gt;VALUE</text>
        <text x="-3.81" y="-6" size="1.778" layer="96">&gt;VAL</text>
        <pin name="1" x="-5.08" y="0" visible="off" length="short" direction="pas" swaplevel="1"/>
        <pin name="2" x="5.08" y="0" visible="off" length="short" direction="pas" swaplevel="1" rot="R180"/>
        </symbol>
        </symbols>
        <devicesets>
        <deviceset name="R" prefix="R" uservalue="yes">
        <gates><gate name="G$1" symbol="R" x="0" y="0"/></gates>
        <devices><device name="0402" package="0402">
        <connects><connect gate="G$1" pin="1" pad="1"/><connect gate="G$1" pin="2" pad="2"/></connects>
        <technologies><technology name="10K_"><attribute name="VAL" value="10k 1%"/></technology></technologies>
        </device></devices>
        </deviceset>
        </devicesets>
        </library>
        </libraries>
        <parts>
        <part name="R1" library="test" deviceset="R" device="0402" technology="10K_" value="10k"/>
        </parts>
        <sheets>
        <sheet>
        <instances>
        <instance part="R1" gate="G$1" x="50.8" y="25.4" rot="R90"/>
        </instances>
        <nets>
        <net name="VCC" class="0">
        <segment>
        <pinref part="R1" gate="G$1" pin="2"/>
        <wire x1="50.8" y1="30.48" x2="50.8" y2="40.64" width="0.1524" layer="91"/>
        <label x="50.8" y="40.64" size="1.778" layer="95"/>
        </segment>
        </net>
        </nets>
        </sheet>
        </sheets>
        </schematic>
        </drawing>
        </eagle>
        """;

    private static Schematic ReadSample() => EagleSchematicReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(Sample)));

    [Fact]
    public void Reads_parts_instances_and_nets()
    {
        var schematic = ReadSample();

        Assert.Equal("10k", schematic.Parts["R1"].Value);
        var sheet = Assert.Single(schematic.Sheets);
        Assert.Single(sheet.Instances);
        var net = Assert.Single(sheet.Nets);
        Assert.Equal("VCC", net.Name);
        Assert.Equal(new PinRef("R1", "G$1", "2"), Assert.Single(net.PinRefs));
    }

    [Fact]
    public void Places_symbol_pins_on_the_sheet()
    {
        var view = SheetView.Build(ReadSample(), 0);

        // Pin 2 of a resistor rotated R90 at (50.8, 25.4) connects at (50.8, 30.48), where the net wire starts.
        Assert.Contains(view.Wires, w => w.Layer == 94 && GeometryMath.SamePoint(w.From, new Point(50.8, 30.48)));
    }

    [Fact]
    public void Resolves_name_value_and_net_label_texts()
    {
        var texts = SheetView.Build(ReadSample(), 0).Texts.Select(t => t.Value).ToList();

        Assert.Contains("R1", texts);
        Assert.Contains("10k", texts);
        Assert.Contains("VCC", texts);
    }

    [Fact]
    public void Resolves_attributes_of_the_device_technology()
    {
        var texts = SheetView.Build(ReadSample(), 0).Texts.Select(t => t.Value).ToList();

        Assert.Contains("10k 1%", texts);
        Assert.DoesNotContain(">VAL", texts);
    }

    [Fact]
    public void Reads_the_example_schematics_installed_with_eagle()
    {
        const string examples = @"C:\EAGLE 9.4.2\examples";
        if (!Directory.Exists(examples))
            return;

        var files = Directory.GetFiles(examples, "*.sch", SearchOption.AllDirectories);
        var read = 0;
        foreach (var file in files)
        {
            Schematic schematic;
            try
            {
                schematic = EagleSchematicReader.Read(file);
            }
            catch (InvalidDataException ex) when (ex.Message.Contains("binary format"))
            {
                continue;
            }

            // Some sheets are legitimately empty, but every file has something on it.
            var views = Enumerable.Range(0, schematic.Sheets.Count).Select(i => SheetView.Build(schematic, i)).ToList();
            Assert.Contains(views, v => v.Bounds() is not null);
            Assert.All(schematic.Sheets.SelectMany(s => s.Instances), i => Assert.NotNull(schematic.Resolve(i)));
            read++;
        }
        Assert.True(read > 0);
    }

    [Fact]
    public void Rejects_old_binary_files_with_a_clear_message()
    {
        var binary = new MemoryStream(new byte[] { 0x10, 0x00, 0x2A, 0x00 });

        var ex = Assert.Throws<InvalidDataException>(() => EagleSchematicReader.Read(binary));
        Assert.Contains("EAGLE 5", ex.Message);
    }
}
