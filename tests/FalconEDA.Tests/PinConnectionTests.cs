using System.Text;
using FalconEDA.Commands;
using FalconEDA.Core;
using FalconEDA.IO;

namespace FalconEDA.Tests;

/// <summary>
/// Uses the sample schematic: R1 rotated R90 at (50.8 25.4), so pin 1 connects at (50.8 20.32)
/// and pin 2 at (50.8 30.48), where net VCC starts.
/// </summary>
public class PinConnectionTests
{
    private static readonly PinRef Pin1 = new("R1", "G$1", "1");
    private static readonly PinRef Pin2 = new("R1", "G$1", "2");

    private readonly Editor _editor = new();

    public PinConnectionTests()
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

    private Net NetOf(PinRef pin) => Assert.Single(_editor.Sheet.Nets, n => n.PinRefs.Contains(pin));

    [Fact]
    public void Net_ending_on_a_pin_connects_it()
    {
        Run("NET (50.8 10) (50.8 20.32);");

        Assert.Equal("N$1", NetOf(Pin1).Name);
    }

    [Fact]
    public void Net_drawn_to_a_pin_that_is_already_on_a_net_joins_that_net()
    {
        Run("NET (60 30.48) (50.8 30.48);");

        var vcc = NetOf(Pin2);
        Assert.Equal("VCC", vcc.Name);
        Assert.Single(_editor.Sheet.Nets);
        Assert.Equal(2, vcc.Wires.Count);
    }

    [Fact]
    public void Deleting_the_only_wire_on_a_pin_disconnects_it()
    {
        Run("DELETE (50.8 35);");

        Assert.DoesNotContain(_editor.Sheet.Nets, n => n.PinRefs.Contains(Pin2));
    }

    [Fact]
    public void After_a_split_the_pin_stays_with_the_piece_it_touches()
    {
        Run("NET (50.8 20.32) (50.8 10) (70 10) (70 0); DELETE (60 10);");

        var withPin = NetOf(Pin1);
        Assert.True(withPin.Touches(new Point(50.8, 20.32)));
        var other = Assert.Single(_editor.Sheet.Nets, n => n != withPin && n.Name != "VCC");
        Assert.Empty(other.PinRefs);
    }

    [Fact]
    public void Undo_restores_the_pin_connection()
    {
        Run("DELETE (50.8 35); UNDO;");

        Assert.Equal("VCC", NetOf(Pin2).Name);
    }

    [Fact]
    public void Clicking_a_pin_ends_the_net_like_eagle()
    {
        Run("NET;");
        var tool = Assert.IsType<PolylineTool>(_editor.Tool);

        tool.Click(new Point(50.8, 10), 0, 1);
        tool.Click(new Point(50.8, 20.32), 0, 1);

        Assert.Equal("N$1", NetOf(Pin1).Name);
        Assert.Empty(tool.Preview);
    }

    [Fact]
    public void Pin_connection_is_saved()
    {
        Run("NET (50.8 10) (50.8 20.32);");
        var path = Path.Combine(Path.GetTempPath(), $"falconeda-pin-{Guid.NewGuid():N}.sch");
        try
        {
            EagleSchematicWriter.Save(_editor.Schematic, path);

            var net = EagleSchematicReader.Read(path).Sheets[0].Nets.Single(n => n.Name == "N$1");
            Assert.Contains(Pin1, net.PinRefs);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
