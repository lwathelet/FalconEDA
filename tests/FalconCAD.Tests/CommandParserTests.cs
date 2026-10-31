using FalconCAD.Commands;
using FalconCAD.Core;

namespace FalconCAD.Tests;

public class CommandParserTests
{
    [Fact]
    public void Parses_name_numbers_and_coordinates()
    {
        var commands = CommandParser.Parse("route 0.2 (0 0) (10 5.5);");

        var command = Assert.Single(commands);
        Assert.Equal("ROUTE", command.Name);
        Assert.Equal(new NumberArgument(0.2), command.Arguments[0]);
        Assert.Equal(new CoordinateArgument(new Point(0, 0)), command.Arguments[1]);
        Assert.Equal(new CoordinateArgument(new Point(10, 5.5)), command.Arguments[2]);
    }

    [Fact]
    public void Splits_multiple_commands()
    {
        var commands = CommandParser.Parse("GRID mm; DELETE (3 4);");

        Assert.Equal(new[] { "GRID", "DELETE" }, commands.Select(c => c.Name));
    }

    [Fact]
    public void Semicolon_inside_text_does_not_split()
    {
        var commands = CommandParser.Parse("NAME 'a;b';");

        var command = Assert.Single(commands);
        Assert.Equal(new TextArgument("a;b"), command.Arguments[0]);
    }

    [Fact]
    public void Unterminated_coordinate_throws()
    {
        Assert.Throws<FormatException>(() => CommandParser.Parse("MOVE (1 2"));
    }
}
