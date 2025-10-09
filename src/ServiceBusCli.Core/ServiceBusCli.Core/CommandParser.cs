using System.Text.RegularExpressions;

namespace ServiceBusCli.Core;

public enum CommandKind
{
    None,
    Help,
    Quit,
    Open,
    Queue,
    Dlq,
    Reject,
    Resubmit,
    Delete,
    Session
}

public sealed record ParsedCommand(CommandKind Kind, long? Index = null, string? Raw = null);

public static class CommandParser
{
    private static readonly Regex OpenRe = new("^open\\s+(?<n>\\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex QueueRe = new("^queue\\s+(?<n>\\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex DlqRe = new("^dlq\\s+(?<n>\\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RejectRe = new("^reject\\s+(?<n>\\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ResubmitRe = new("^resubmit\\s+(?<n>\\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex DeleteRe = new("^delete\\s+(?<n>\\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SessionRe = new("^session(?:\\s+(?<t>.+))?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static ParsedCommand Parse(string? input)
    {
        var text = (input ?? string.Empty).Trim();
        if (text.Length == 0) return new ParsedCommand(CommandKind.None, Raw: text);
        if (text is "h" or "help" or "?" or "H" or "HELP" or "?") return new ParsedCommand(CommandKind.Help, Raw: text);
        if (string.Equals(text, "q", StringComparison.OrdinalIgnoreCase) || string.Equals(text, "quit", StringComparison.OrdinalIgnoreCase) || string.Equals(text, "exit", StringComparison.OrdinalIgnoreCase))
            return new ParsedCommand(CommandKind.Quit, Raw: text);
        if (string.Equals(text, "dlq", StringComparison.OrdinalIgnoreCase))
            return new ParsedCommand(CommandKind.Dlq, Raw: text);
        if (string.Equals(text, "queue", StringComparison.OrdinalIgnoreCase))
            return new ParsedCommand(CommandKind.Queue, Raw: text);
        // Numeric-only input: treat as global index selection (open)
        if (text.All(char.IsDigit) && long.TryParse(text, out var num))
            return new ParsedCommand(CommandKind.Open, Index: num, Raw: text);
        var m = OpenRe.Match(text);
        if (m.Success && long.TryParse(m.Groups["n"].Value, out var n))
            return new ParsedCommand(CommandKind.Open, Index: n, Raw: text);
        m = QueueRe.Match(text);
        if (m.Success && long.TryParse(m.Groups["n"].Value, out var qn))
            return new ParsedCommand(CommandKind.Queue, Index: qn, Raw: text);
        m = DlqRe.Match(text);
        if (m.Success && long.TryParse(m.Groups["n"].Value, out var dn))
            return new ParsedCommand(CommandKind.Dlq, Index: dn, Raw: text);
        m = RejectRe.Match(text);
        if (m.Success && long.TryParse(m.Groups["n"].Value, out var rj))
            return new ParsedCommand(CommandKind.Reject, Index: rj, Raw: text);
        m = ResubmitRe.Match(text);
        if (m.Success && long.TryParse(m.Groups["n"].Value, out var rs))
            return new ParsedCommand(CommandKind.Resubmit, Index: rs, Raw: text);
        m = DeleteRe.Match(text);
        if (m.Success && long.TryParse(m.Groups["n"].Value, out var del))
            return new ParsedCommand(CommandKind.Delete, Index: del, Raw: text);
        m = SessionRe.Match(text);
        if (m.Success)
        {
            var t = m.Groups["t"].Success ? m.Groups["t"].Value.Trim() : null;
            return new ParsedCommand(CommandKind.Session, Index: null, Raw: t);
        }
        return new ParsedCommand(CommandKind.None, Raw: text);
    }
}
