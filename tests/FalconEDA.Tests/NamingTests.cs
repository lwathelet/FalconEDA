using System.Text;
using FalconEDA.Commands;
using FalconEDA.Core;
using FalconEDA.IO;

namespace FalconEDA.Tests;

/// <summary>
/// NAME, LABEL, JUNCTION, SMASH and SHOW on the sample schematic: R1 rotated R90 at (50.8 25.4),
/// net VCC from R1 pin 2 at (50.8 30.48) up to (50.8 40.64), with a label.
/// </summary>
public class NamingTests
{
    private readonly Editor _editor = new();

    public NamingTests()
    {
        _editor.Load(EagleSchematicReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(EagleSchematicReaderTests.Sample))), null);
        Run("GRID MM 0.01;");
    }

    private string Run(string text) => string.Join(" ", CommandParser.Parse(text).Select(c => CommandExecutor.Execute(_editor, c)));

    private Net Net(string name) => _editor.Sheet.Nets.Single(n => n.Name == name);

    [Fact]
    public void Name_renames_a_part_with_its_pins_and_undo_brings_it_back()
    {
        Run("NAME R1 R5;");

        Assert.True(_editor.Schematic.Parts.ContainsKey("R5"));
        Assert.Equal("R5", Assert.Single(_editor.Sheet.Instances).Part);
        Assert.Contains(new PinRef("R5", "G$1", "2"), Net("VCC").PinRefs);

        Run("UNDO;");
        Assert.True(_editor.Schematic.Parts.ContainsKey("R1"));
        Assert.Contains(new PinRef("R1", "G$1", "2"), Net("VCC").PinRefs);
    }

    [Fact]
    public void Name_renames_a_net()
    {
        Run("NAME VCC VDD;");

        Assert.Equal("VDD", Assert.Single(_editor.Sheet.Nets).Name);
    }

    [Fact]
    public void Naming_a_net_like_another_joins_them()
    {
        Run("NET (0 0) (10 0);");
        var message = Run("NAME N$1 VCC;");

        Assert.Contains("joined", message);
        Assert.Equal(2, Net("VCC").Wires.Count);
        Assert.Single(_editor.Sheet.Nets);
    }

    [Fact]
    public void A_part_name_that_is_taken_is_refused()
    {
        Run("GROUP ALL; COPY; PASTE (80 25.4);");

        Assert.Throws<CommandException>(() => Run("NAME R1 R2;"));
    }

    [Fact]
    public void Label_adds_a_net_name_label()
    {
        Run("LABEL (50.8 35) (55 35);");

        Assert.Equal(2, Net("VCC").Labels.Count);
        Assert.Contains(SheetView.Build(_editor.Schematic, 0).Texts, t => t.Value == "VCC" && GeometryMath.SamePoint(t.Position, new Point(55, 35)));
    }

    [Fact]
    public void Junction_is_placed_once_on_a_net()
    {
        Run("JUNCTION (50.8 35);");

        Assert.Equal(new Point(50.8, 35), Assert.Single(Net("VCC").Junctions));
        Assert.Throws<CommandException>(() => Run("JUNCTION (50.8 35);"));
        Assert.Throws<CommandException>(() => Run("JUNCTION (0 0);"));
    }

    [Fact]
    public void Smash_switches_the_part_texts_on_and_off()
    {
        Run("SMASH R1;");
        var instance = Assert.Single(_editor.Sheet.Instances);
        Assert.True(instance.Smashed);
        Assert.Equal(new[] { "NAME", "VALUE", "VAL" }, instance.Attributes.Select(a => a.Name));

        // Unsmashed, the texts go back to the symbol's places; the part keeps its attributes, which Eagle 9 needs.
        instance.Attributes[0].Position += new Point(5, 0);
        Run("SMASH R1;");
        Assert.False(instance.Smashed);
        Assert.Equal(new Point(50.8 - 1.4986, 25.4 - 3.81), instance.Attributes[0].Position);
    }

    [Fact]
    public void A_smashed_name_moves_on_its_own()
    {
        Run("SMASH R1;");
        var instance = Assert.Single(_editor.Sheet.Instances);
        var name = instance.Attributes.Single(a => a.Name == "NAME");
        var before = name.Position!.Value;

        // Click a little inside the name text, which runs upwards because R1 is rotated.
        var text = SheetView.Build(_editor.Schematic, 0).Texts.Single(t => t.Owner == name);
        var inside = text.Position + Point.Direction(text.Angle) * 0.5 + Point.Direction(text.Angle + 90) * 0.5;
        var picked = _editor.Sheet.SelectForMove(inside, 0.3);
        Assert.Contains(name, picked.Attributes);

        _editor.Sheet.Move(picked, new Point(10, 0));
        Assert.Equal(before + new Point(10, 0), name.Position);
        Assert.Equal(new Point(50.8, 25.4), instance.Position);
    }

    [Fact]
    public void Show_points_out_a_net_until_the_next_command()
    {
        var message = Run("SHOW VCC;");

        Assert.Contains("VCC: 1 pin(s) R1.2", message);
        Assert.Single(_editor.Highlight.Wires);
        Assert.Contains(_editor.Sheet.Instances[0], _editor.Highlight.Instances);

        Run("GRID MM 1;");
        Assert.True(_editor.Highlight.IsEmpty);
    }

    [Fact]
    public void Renamed_parts_and_nets_labels_and_junctions_are_saved()
    {
        Run("NAME R1 R5; NAME VCC VDD; LABEL (50.8 35) (55 35); JUNCTION (50.8 35); SMASH R5;");
        var path = Path.Combine(Path.GetTempPath(), $"falconeda-naming-{Guid.NewGuid():N}.sch");
        try
        {
            EagleSchematicWriter.Save(_editor.Schematic, path);
            var read = EagleSchematicReader.Read(path);

            var net = Assert.Single(read.Sheets[0].Nets);
            Assert.Equal("VDD", net.Name);
            Assert.Contains(new PinRef("R5", "G$1", "2"), net.PinRefs);
            Assert.Equal(2, net.Labels.Count);
            Assert.Single(net.Junctions);
            Assert.True(Assert.Single(read.Sheets[0].Instances).Smashed);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>What NAME asks when the net at <paramref name="p"/> is clicked.</summary>
    private RenameRequest ClickName(Point p)
    {
        RenameRequest? asked = null;
        _editor.RenameRequested += r => asked = r;
        Run("NAME;");
        _editor.Tool!.Click(p, 0.5, 1);
        _editor.SetTool(null);
        return asked!;
    }

    [Fact]
    public void Name_does_not_ask_for_a_net_in_one_piece_on_one_sheet()
    {
        var request = ClickName(new Point(50.8, 35));

        Assert.True(request.IsNet);
        Assert.Equal("VCC", request.Name);
        Assert.Empty(request.Scopes);
    }

    [Fact]
    public void Name_offers_the_piece_or_the_sheet_for_a_net_in_several_pieces()
    {
        Run("NET VCC (0 0) (10 0);");

        Assert.Equal(new[] { NameScope.Piece, NameScope.Sheet }, ClickName(new Point(5, 0)).Scopes);
    }

    [Fact]
    public void Name_offers_this_sheet_or_all_sheets_for_a_net_on_several_sheets()
    {
        _editor.Schematic.AddSheet().AddNet(new[] { new Point(0, 0), new Point(10, 0) }, name: "VCC");

        Assert.Equal(new[] { NameScope.Sheet, NameScope.AllSheets }, ClickName(new Point(50.8, 35)).Scopes);
    }

    [Fact]
    public void Renaming_one_piece_leaves_the_rest_of_the_net()
    {
        Run("NET VCC (0 0) (10 0); NAME (5 0) VDD;");

        var vdd = Net("VDD");
        Assert.Single(vdd.Wires);
        Assert.Empty(vdd.PinRefs);
        var vcc = Net("VCC");
        Assert.Contains(new PinRef("R1", "G$1", "2"), vcc.PinRefs);
        Assert.Single(vcc.Labels);
    }

    [Fact]
    public void Renaming_on_this_sheet_leaves_other_sheets()
    {
        var other = _editor.Schematic.AddSheet();
        other.AddNet(new[] { new Point(0, 0), new Point(10, 0) }, name: "VCC");

        Run("NAME VCC VDD SHEET;");

        Assert.Equal("VDD", Assert.Single(_editor.Sheet.Nets).Name);
        Assert.Equal("VCC", Assert.Single(other.Nets).Name);
    }

    [Fact]
    public void A_renamed_piece_is_saved()
    {
        Run("NET VCC (0 0) (10 0); NAME (5 0) VDD;");
        var path = Path.Combine(Path.GetTempPath(), $"falconeda-piece-{Guid.NewGuid():N}.sch");
        try
        {
            EagleSchematicWriter.Save(_editor.Schematic, path);
            var nets = EagleSchematicReader.Read(path).Sheets[0].Nets;
            Assert.Equal(new[] { "VCC", "VDD" }, nets.Select(n => n.Name).Order());
            Assert.Contains(new PinRef("R1", "G$1", "2"), nets.Single(n => n.Name == "VCC").PinRefs);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
