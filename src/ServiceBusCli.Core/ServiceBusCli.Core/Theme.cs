namespace ServiceBusCli.Core;

public sealed record Theme(
    string Name,
    ConsoleColor Control,
    ConsoleColor Number,
    ConsoleColor Default,
    ConsoleColor Letters
);

public static class ThemePresets
{
    public static Theme Default => new("default", ConsoleColor.Yellow, ConsoleColor.Cyan, Console.ForegroundColor, ConsoleColor.Yellow);
    public static Theme Mono => new("mono", Console.ForegroundColor, Console.ForegroundColor, Console.ForegroundColor, Console.ForegroundColor);
    public static Theme NoColor => new("no-color", Console.ForegroundColor, Console.ForegroundColor, Console.ForegroundColor, Console.ForegroundColor);
    public static Theme Solarized => new("solarized", ConsoleColor.DarkYellow, ConsoleColor.Cyan, ConsoleColor.Gray, ConsoleColor.DarkCyan);

    public static Theme Resolve(string? name, bool noColor)
    {
        if (noColor) return NoColor;
        return (name ?? "default").ToLowerInvariant() switch
        {
            "default" => Default,
            "mono" => Mono,
            "no-color" => NoColor,
            "solarized" => Solarized,
            _ => Default
        };
    }
}

