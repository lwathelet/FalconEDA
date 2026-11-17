using System.Text;
using FalconEDA.Commands;
using FalconEDA.Core;
using FalconEDA.IO;

namespace FalconEDA.Tests;

/// <summary>
/// Uses the sample schematic: R1 rotated R90 at (50.8 25.4), pin 1 at (50.8 20.32), pin 2 at (50.8 30.48),
/// and net VCC from pin 2 up to (50.8 40.64) with a label there.
/// </summary>
public class GroupEditingTests
{
    private static readonly PinRef Pin2 = new("R1", "G$1", "2");

    private readonly Editor _editor = new();

    public GroupEditingTests()
    {
        var schematic = EagleSchematicReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(EagleSchematicReaderTests.Sample)));
        _editor.Load(schematic, null);
        Run("GRID MM 0.01;");
    }

    private void Run(string text)
    {
        foreach (var command in CommandParser.Parse(text))
            CommandExecutor.Execute(_editor, command);
    }

    private Instance R(string name) => _editor.Sheet.Instances.Single(i => i.Part == name);

    private Net Vcc => _editor.Sheet.Nets.Single(n => n.Name == "VCC");

    private void SelectEverything()
    {
        _editor.Selection.Clear();
        _editor.Selection.UnionWith(_editor.Sheet.SelectIn(new Rect(new Point(0, 0), new Point(100, 100))));
    }

    [Fact]
    public void Moving_a_part_stretches_the_net_on_its_pin()
    {
        Run("MOVE R1 (60 25.4);");

        Assert.Equal(new Point(60, 25.4), R("R1").Position);
        Assert.Contains(Vcc.Wires, w => GeometryMath.SamePoint(w.From, new Point(60, 30.48)));
        Assert.Contains(Pin2, Vcc.PinRefs);
    }

    [Fact]
    public void Rotating_a_part_keeps_its_net_connected()
    {
        Run("ROTATE R1;");

        Assert.Equal(new Rotation(180), R("R1").Rotation);
        Assert.Contains(Vcc.Wires, w => GeometryMath.SamePoint(w.From, new Point(45.72, 25.4)));
        Assert.Contains(Pin2, Vcc.PinRefs);
    }

    [Fact]
    public void Rotate_by_name_accepts_eagle_orientations()
    {
        Run("ROTATE =MR270 R1;");

        Assert.Equal(new Rotation(270, Mirror: true), R("R1").Rotation);
    }

    [Fact]
    public void Mirror_flips_the_part()
    {
        Run("MIRROR R1;");

        Assert.True(R("R1").Rotation.Mirror);
        Assert.Contains(Pin2, Vcc.PinRefs);
    }

    [Fact]
    public void Moving_a_corner_drags_both_wires_that_meet_there()
    {
        Run("NET (0 0) (10 0) (10 10); MOVE (10 0) (20 0);");

        var net = _editor.Sheet.Nets.Single(n => n.Name != "VCC");
        Assert.Contains(net.Wires, w => GeometryMath.SamePoint(w.From, new Point(0, 0)) && GeometryMath.SamePoint(w.To, new Point(20, 0)));
        Assert.Contains(net.Wires, w => GeometryMath.SamePoint(w.From, new Point(20, 0)) && GeometryMath.SamePoint(w.To, new Point(10, 10)));
    }

    [Fact]
    public void Undo_puts_a_moved_part_back()
    {
        Run("MOVE R1 (60 25.4); UNDO;");

        Assert.Equal(new Point(50.8, 25.4), R("R1").Position);
    }

    [Fact]
    public void Copy_then_paste_renumbers_the_part_and_joins_named_nets()
    {
        SelectEverything();
        Run("COPY;");
        Assert.True(_editor.Sheet.Instances.Count == 1, "COPY with a group must not change the drawing");

        Run("PASTE (80 25.4);");

        var copy = R("R2");
        Assert.Equal("10k", _editor.Schematic.Parts["R2"].Value);
        Assert.Contains(new PinRef("R2", "G$1", "2"), Vcc.PinRefs);
        Assert.Equal(2, Vcc.Wires.Count);
        Assert.Contains(copy, _editor.Selection.Instances);
    }

    [Fact]
    public void Cut_removes_the_group_and_paste_brings_it_back()
    {
        SelectEverything();
        Run("CUT;");

        Assert.Empty(_editor.Sheet.Instances);
        Assert.False(_editor.Schematic.Parts.ContainsKey("R1"));
        Assert.Empty(_editor.Sheet.Nets);

        Run("PASTE (50.8 25.4);");

        Assert.Equal("R1", Assert.Single(_editor.Sheet.Instances).Part);
        Assert.Contains(Pin2, Vcc.PinRefs);
    }

    [Fact]
    public void Cut_without_a_group_is_refused()
    {
        Assert.Throws<CommandException>(() => Run("CUT;"));
    }

    [Fact]
    public void Copy_without_a_group_duplicates_the_clicked_part_like_eagle()
    {
        Run("COPY;");
        var tool = Assert.IsType<PlaceTool>(_editor.Tool);

        tool.Click(new Point(50.8, 25.4), 0.5, 1);
        tool.Click(new Point(70, 25.4), 0.5, 1);

        Assert.Equal(new Point(70, 25.4), R("R2").Position);
        Assert.Null(_editor.Clipboard);
    }

    [Fact]
    public void Moving_the_group_with_the_mouse_and_cancelling_puts_it_back()
    {
        SelectEverything();
        Run("MOVE;");
        var tool = Assert.IsType<MoveTool>(_editor.Tool);

        tool.Click(new Point(50.8, 25.4), 0.5, 1);
        tool.Move(new Point(60.8, 25.4));
        Assert.Equal(new Point(60.8, 25.4), R("R1").Position);

        tool.Cancel();
        Assert.Equal(new Point(50.8, 25.4), R("R1").Position);
    }

    [Fact]
    public void Group_all_and_delete_empty_the_sheet()
    {
        Run("GROUP ALL;");
        CommandExecutor.DeleteSelection(_editor);

        Assert.Empty(_editor.Sheet.Instances);
        Assert.Empty(_editor.Sheet.Nets);
    }

    [Fact]
    public void Moved_and_pasted_parts_are_saved()
    {
        Run("MOVE R1 (60 25.4);");
        SelectEverything();
        Run("COPY; PASTE (90 25.4);");

        var path = Path.Combine(Path.GetTempPath(), $"falconeda-group-{Guid.NewGuid():N}.sch");
        try
        {
            EagleSchematicWriter.Save(_editor.Schematic, path);
            var read = EagleSchematicReader.Read(path);

            var sheet = read.Sheets[0];
            Assert.Equal(new Point(60, 25.4), sheet.Instances.Single(i => i.Part == "R1").Position);
            // PASTE puts the middle of the copied group at the given point.
            var expected = new Point(60, 25.4) + new Point(90, 25.4) - _editor.Clipboard!.Reference;
            Assert.True(GeometryMath.SamePoint(expected, sheet.Instances.Single(i => i.Part == "R2").Position));
            Assert.NotNull(read.Resolve(sheet.Instances.Single(i => i.Part == "R2")));
            Assert.Contains(new PinRef("R2", "G$1", "2"), sheet.Nets.Single(n => n.Name == "VCC").PinRefs);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Moving_a_part_away_from_a_pin_it_touched_keeps_the_connection_with_a_wire()
    {
        // A copy of R1 whose pin 1 sits right on R1's pin 2, at the start of net VCC.
        _editor.Selection.Instances.Add(R("R1"));
        Run("COPY; PASTE (50.8 35.56);");
        Assert.Contains(new PinRef("R2", "G$1", "1"), Vcc.PinRefs);

        Run("MOVE R2 (60 35.56);");

        Assert.Contains(Pin2, Vcc.PinRefs);
        Assert.Contains(new PinRef("R2", "G$1", "1"), Vcc.PinRefs);
        Assert.Contains(Vcc.Wires, w => GeometryMath.SamePoint(w.From, new Point(50.8, 30.48)) && GeometryMath.SamePoint(w.To, new Point(60, 30.48)));
    }
}
