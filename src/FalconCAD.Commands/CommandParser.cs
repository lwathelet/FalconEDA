using FalconCAD.Core;

namespace FalconCAD.Commands;

/// <summary>One parsed command: a name followed by its arguments.</summary>
public sealed record Command(string Name, IReadOnlyList<Argument> Arguments);

public abstract record Argument;
public sealed record WordArgument(string Value) : Argument;
public sealed record NumberArgument(double Value) : Argument;
public sealed record TextArgument(string Value) : Argument;
public sealed record CoordinateArgument(Point Value) : Argument;

/// <summary>
/// Parses command-line text such as <c>ROUTE 0.2 (0 0) (10 0);</c>.
/// Commands are separated by ';'. Arguments are separated by whitespace.
/// Coordinates are written (x y). Text is written 'like this'.
/// </summary>
public static class CommandParser
{
    public static IReadOnlyList<Command> Parse(string input)
    {
        var commands = new List<Command>();
        foreach (var statement in SplitStatements(input))
        {
            var command = ParseStatement(statement);
            if (command is not null)
                commands.Add(command);
        }
        return commands;
    }

    private static IEnumerable<string> SplitStatements(string input)
    {
        var start = 0;
        var inText = false;
        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];
            if (c == '\'')
                inText = !inText;
            else if (c == ';' && !inText)
            {
                yield return input[start..i];
                start = i + 1;
            }
        }
        if (start < input.Length)
            yield return input[start..];
    }

    private static Command? ParseStatement(string statement)
    {
        var tokens = Tokenize(statement);
        if (tokens.Count == 0)
            return null;

        if (tokens[0] is not WordArgument name)
            throw new FormatException($"A command must start with a name: '{statement.Trim()}'");

        return new Command(name.Value.ToUpperInvariant(), tokens.Skip(1).ToList());
    }

    private static List<Argument> Tokenize(string text)
    {
        var tokens = new List<Argument>();
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
            }
            else if (c == '\'')
            {
                var end = text.IndexOf('\'', i + 1);
                if (end < 0)
                    throw new FormatException("Unterminated text argument.");
                tokens.Add(new TextArgument(text[(i + 1)..end]));
                i = end + 1;
            }
            else if (c == '(')
            {
                var end = text.IndexOf(')', i + 1);
                if (end < 0)
                    throw new FormatException("Unterminated coordinate.");
                tokens.Add(ParseCoordinate(text[(i + 1)..end]));
                i = end + 1;
            }
            else
            {
                var start = i;
                while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] != '(' && text[i] != '\'')
                    i++;
                var word = text[start..i];
                tokens.Add(double.TryParse(word, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var number)
                    ? new NumberArgument(number)
                    : new WordArgument(word));
            }
        }
        return tokens;
    }

    private static CoordinateArgument ParseCoordinate(string text)
    {
        var parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2
            || !double.TryParse(parts[0], System.Globalization.CultureInfo.InvariantCulture, out var x)
            || !double.TryParse(parts[1], System.Globalization.CultureInfo.InvariantCulture, out var y))
            throw new FormatException($"Invalid coordinate: ({text})");
        return new CoordinateArgument(new Point(x, y));
    }
}
