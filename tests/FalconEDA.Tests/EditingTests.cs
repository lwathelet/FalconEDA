using FalconCAD.Commands;
using FalconCAD.Core;

namespace FalconCAD.Tests;

public class EditingTests
{
    private readonly Editor _editor = new();

    private void Run(string text)
    {
        foreach (var command in CommandParser.Parse(text))
            CommandExecutor.Execute(_editor, command);
    }

    [Fact]
    public void Deleting_the_middle_of_a_net_splits_it()
    {
        Run("GRID MM 1; NET (0 0) (10 0) (20 0) (30 0); DELETE (15 0);");

        Assert.Equal(2, _editor.Sheet.Nets.Count);
        Assert.Equal(new[] { "N$1", "N$2" }, _editor.Sheet.Nets.Select(n => n.Name).Order());
    }

    [Fact]
    public void Deleting_the_end_of_a_net_keeps_one_net()
    {
        Run("GRID MM 1; NET (0 0) (10 0) (20 0); DELETE (15 0);");

        var net = Assert.Single(_editor.Sheet.Nets);
        Assert.Single(net.Wires);
    }

    [Fact]
    public void Undo_and_redo_restore_the_drawing()
    {
        Run("GRID MM 1; NET (0 0) (10 0); NET (0 5) (10 5);");

        Run("UNDO;");
        Assert.Single(_editor.Sheet.Nets);

        Run("UNDO;");
        Assert.Empty(_editor.Sheet.Nets);

        Run("REDO;");
        Assert.Single(_editor.Sheet.Nets);
    }

    [Fact]
    public void A_new_change_clears_redo()
    {
        Run("GRID MM 1; NET (0 0) (10 0); UNDO; NET (0 5) (10 5);");

        Assert.False(_editor.Redo());
    }

    [Fact]
    public void Edit_switches_sheet()
    {
        _editor.Schematic.AddSheet();

        Run("EDIT .s2;");

        Assert.Equal(1, _editor.SheetIndex);
        Assert.Throws<CommandException>(() => Run("EDIT .s5;"));
    }

    [Theory]
    [InlineData("R90", 90, false, false)]
    [InlineData("MR180", 180, true, false)]
    [InlineData("SMR270", 270, true, true)]
    [InlineData("", 0, false, false)]
    public void Parses_rotation(string text, double angle, bool mirror, bool spin)
    {
        Assert.Equal(new Rotation(angle, mirror, spin), Rotation.Parse(text));
    }

    [Fact]
    public void Transform_rotates_then_mirrors_then_moves()
    {
        var transform = new Transform(new Point(10, 20), Rotation.Parse("MR90"));

        // Rotate 90: (2, 1) -> (-1, 2). Mirror: (1, 2). Move: (11, 22).
        Assert.Equal(new Point(11, 22), transform.Apply(new Point(2, 1)));
    }
}
