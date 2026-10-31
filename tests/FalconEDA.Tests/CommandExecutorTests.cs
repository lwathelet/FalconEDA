using FalconCAD.Commands;
using FalconCAD.Core;

namespace FalconCAD.Tests;

public class CommandExecutorTests
{
    private readonly Editor _editor = new();

    private void Run(string text)
    {
        foreach (var command in CommandParser.Parse(text))
            CommandExecutor.Execute(_editor, command);
    }

    [Fact]
    public void Grid_sets_unit_and_size()
    {
        Run("GRID MM 1;");

        Assert.Equal(Unit.Mm, _editor.Grid.Unit);
        Assert.Equal(1, _editor.Grid.Size);
    }

    [Fact]
    public void Grid_unit_alone_keeps_physical_size()
    {
        Run("GRID MM;");

        Assert.Equal(2.54, _editor.Grid.Size, 9);
    }

    [Fact]
    public void Net_with_coordinates_creates_named_net()
    {
        Run("GRID MM 1; NET (0 0) (10 0) (10 5);");

        var net = Assert.Single(_editor.Sheet.Nets);
        Assert.Equal("N$1", net.Name);
        Assert.Equal(2, net.Wires.Count);
    }

    [Fact]
    public void Net_ending_on_another_net_joins_it_with_a_junction()
    {
        Run("GRID MM 1; NET (0 0) (10 0); NET (5 0) (5 10);");

        var net = Assert.Single(_editor.Sheet.Nets);
        Assert.Equal(new Point(5, 0), Assert.Single(net.FindJunctions()));
    }

    [Fact]
    public void Net_connecting_two_nets_merges_them()
    {
        Run("GRID MM 1; NET (0 0) (10 0); NET (20 0) (30 0); NET (10 0) (20 0);");

        Assert.Single(_editor.Sheet.Nets);
    }

    [Fact]
    public void Separate_nets_get_separate_names()
    {
        Run("GRID MM 1; NET (0 0) (10 0); NET (0 5) (10 5); NET VCC (0 10) (10 10);");

        Assert.Equal(new[] { "N$1", "N$2", "VCC" }, _editor.Sheet.Nets.Select(n => n.Name));
    }

    [Fact]
    public void Delete_removes_nearest_segment()
    {
        Run("GRID MM 1; NET (0 0) (10 0) (10 10); DEL (10 5);");

        var net = Assert.Single(_editor.Sheet.Nets);
        Assert.Single(net.Wires);
    }

    [Fact]
    public void Net_without_coordinates_starts_mouse_tool()
    {
        Run("NET;");

        Assert.IsType<PolylineTool>(_editor.Tool);
    }

    [Fact]
    public void Ambiguous_abbreviation_is_rejected()
    {
        Assert.Throws<CommandException>(() => Run("W (0 0);"));
    }

    [Fact]
    public void Unknown_command_is_rejected()
    {
        Assert.Throws<CommandException>(() => Run("FOO;"));
    }
}
