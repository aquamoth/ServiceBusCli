using System.Text.RegularExpressions;

namespace ServiceBusCli.Core;

public enum CommandKind
{
    None,
    Help,
    Quit,
    Open
}

public sealed record ParsedCommand(CommandKind Kind, long? Index = null, string? Raw = null);

public static class CommandParser
{
    private static readonly Regex OpenRe = new("^open\\s+(?<n>\\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static ParsedCommand Parse(string? input)
    {
        var text = (input ?? string.Empty).Trim();
        if (text.Length == 0) return new ParsedCommand(CommandKind.None, Raw: text);
        if (text is "h" or "help" or "?" or "H" or "HELP" or "?") return new ParsedCommand(CommandKind.Help, Raw: text);
        if (string.Equals(text, "q", StringComparison.OrdinalIgnoreCase) || string.Equals(text, "quit", StringComparison.OrdinalIgnoreCase) || string.Equals(text, "exit", StringComparison.OrdinalIgnoreCase))
            return new ParsedCommand(CommandKind.Quit, Raw: text);
        // Numeric-only input: treat as global index selection (open)
        if (text.All(char.IsDigit) && long.TryParse(text, out var num))
            return new ParsedCommand(CommandKind.Open, Index: num, Raw: text);
        var m = OpenRe.Match(text);
        if (m.Success && long.TryParse(m.Groups["n"].Value, out var n))
            return new ParsedCommand(CommandKind.Open, Index: n, Raw: text);
        return new ParsedCommand(CommandKind.None, Raw: text);
    }
}
