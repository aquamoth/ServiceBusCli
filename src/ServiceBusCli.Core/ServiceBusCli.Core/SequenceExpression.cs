using System;
using System.Collections.Generic;

namespace ServiceBusCli.Core;

public static class SequenceExpression
{
    // Parses expressions like: "514-590,595,597,602-607" into a list of longs.
    // Allows flexible whitespace around commas and dashes. Ignores invalid parts.
    public static List<long> Parse(string? expr)
    {
        var result = new List<long>();
        if (string.IsNullOrWhiteSpace(expr)) return result;
        var parts = expr.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var raw in parts)
        {
            var p = raw.Trim();
            // Allow spaces around '-'
            int dash = p.IndexOf('-');
            if (dash > 0)
            {
                var a = p.Substring(0, dash).Trim();
                var b = p[(dash + 1)..].Trim();
                if (long.TryParse(a, out var start) && long.TryParse(b, out var end))
                {
                    if (end < start) (start, end) = (end, start);
                    // Avoid gigantic allocations for ridiculous ranges
                    var count = end - start + 1;
                    if (count > 0 && count <= 1_000_000)
                    {
                        for (long s = start; s <= end; s++) result.Add(s);
                    }
                }
                continue;
            }
            if (long.TryParse(p, out var single))
            {
                result.Add(single);
            }
        }
        return result;
    }
}

