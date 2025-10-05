namespace ServiceBusCli.Core;

public static class TextTruncation
{
    public static string Truncate(string text, int width)
    {
        if (width <= 0) return string.Empty;
        if (text.Length <= width) return text;
        if (width == 1) return "…";
        return text.Substring(0, width - 1) + "…";
    }
}

public sealed class ColumnSpec
{
    public string Name { get; }
    public int MinWidth { get; }
    public int MaxSample { get; private set; }
    public int Width { get; set; }

    public ColumnSpec(string name, int minWidth)
    {
        Name = name;
        MinWidth = minWidth;
        MaxSample = 0;
        Width = minWidth;
    }

    public void Sample(int length)
    {
        if (length > MaxSample) MaxSample = length;
    }

    public void FinalizeWidth(int remaining)
    {
        Width = Math.Min(Math.Max(MinWidth, MaxSample), remaining);
    }
}

public static class TableLayout
{
    public static IReadOnlyList<ColumnSpec> Compute(int totalWidth, params ColumnSpec[] columns)
    {
        if (columns.Length == 0) return Array.Empty<ColumnSpec>();
        // Reserve at least 1 space between columns
        var remaining = Math.Max(0, totalWidth - (columns.Length - 1));
        foreach (var c in columns)
        {
            c.FinalizeWidth(remaining);
            remaining -= c.Width;
        }
        // Distribute any leftover to the last column for value-like behavior
        if (remaining > 0)
        {
            columns[^1].Width += remaining;
        }
        return columns;
    }
}
