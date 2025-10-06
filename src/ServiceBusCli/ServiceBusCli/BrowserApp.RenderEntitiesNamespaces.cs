using System;
using System.Linq;
using System.Collections.Generic;
using ServiceBusCli.Core;

namespace ServiceBusCli;

public sealed partial class BrowserApp
{
    private void RenderNamespacesTable(List<SBNamespace> namespaces, int start, int count)
    {
        var slice = namespaces.Skip(start).Take(count).ToList();
        if (slice.Count == 0) return;

        int idxW = Math.Max(2, (start + count).ToString().Length);
        int nameW = Math.Max(12, Math.Min(48, slice.Max(n => n.Name.Length)));
        int rgW = Math.Min(24, Math.Max(8, slice.Max(n => (n.ResourceGroup ?? string.Empty).Length)));
        int subW = 8;

        int total = Console.WindowWidth;
        int occupied = idxW + 1 + nameW + 1 + rgW + 1 + subW;
        if (occupied > total)
        {
            int deficit = occupied - total;
            int reduce = Math.Min(deficit, nameW - 12);
            if (reduce > 0) { nameW -= reduce; deficit -= reduce; }
            if (deficit > 0)
            {
                reduce = Math.Min(deficit, rgW - 8);
                if (reduce > 0) { rgW -= reduce; deficit -= reduce; }
            }
        }
        else if (occupied < total)
        {
            nameW += (total - occupied);
        }

        ColorConsole.Write(Align("#", idxW, false), _theme.Control); Console.Write(" ");
        ColorConsole.Write(Align("Namespace", nameW, false), _theme.Control); Console.Write(" ");
        ColorConsole.Write(Align("ResourceGroup", rgW, false), _theme.Control); Console.Write(" ");
        ColorConsole.Write(Align("Sub", subW, false), _theme.Control);
        Console.WriteLine();

        for (int i = 0; i < slice.Count; i++)
        {
            int globalIdx = start + i + 1;
            var ns = slice[i];
            var subShort = ns.SubscriptionId ?? string.Empty;
            if (subShort.Length > 8) subShort = subShort.Substring(0, 8);

            ColorConsole.Write(Align(globalIdx.ToString(), idxW, false), _theme.Number); Console.Write(" ");
            ColorConsole.Write(Align(TextTruncation.Truncate(ns.Name, nameW), nameW, false), _theme.Letters); Console.Write(" ");
            Console.Write(Align(TextTruncation.Truncate(ns.ResourceGroup ?? string.Empty, rgW), rgW, false)); Console.Write(" ");
            Console.Write(Align(subShort, subW, false));
            Console.WriteLine();
        }
    }

    private void RenderEntitiesTable(IReadOnlyList<EntityRow> entities, int start, int count)
    {
        var slice = entities.Skip(start).Take(count).ToList();
        if (slice.Count == 0) return;

        int idxW = Math.Max(2, (start + count).ToString().Length);
        int kindW = 5;
        int pathW = Math.Max(12, Math.Min(48, slice.Max(e => e.Path.Length)));
        int statusW = Math.Min(10, Math.Max(6, slice.Max(e => (e.Status ?? string.Empty).Length)));
        int numW = 8;

        int total = Console.WindowWidth;
        int occupied = idxW + 1 + kindW + 1 + pathW + 1 + statusW + 1 + numW*3 + 2;
        if (occupied > total)
        {
            int deficit = occupied - total;
            int reduce = Math.Min(deficit, pathW - 12);
            if (reduce > 0) { pathW -= reduce; deficit -= reduce; }
            if (deficit > 0)
            {
                reduce = Math.Min(deficit, statusW - 6);
                if (reduce > 0) { statusW -= reduce; deficit -= reduce; }
            }
        }
        else if (occupied < total)
        {
            pathW += (total - occupied);
        }

        ColorConsole.Write(Align("#", idxW, false), _theme.Control); Console.Write(" ");
        ColorConsole.Write(Align("Kind", kindW, false), _theme.Control); Console.Write(" ");
        ColorConsole.Write(Align("Path", pathW, false), _theme.Control); Console.Write(" ");
        ColorConsole.Write(Align("Status", statusW, false), _theme.Control); Console.Write(" ");
        ColorConsole.Write(Align("Total", numW, true), _theme.Control); Console.Write(" ");
        ColorConsole.Write(Align("Active", numW, true), _theme.Control); Console.Write(" ");
        ColorConsole.Write(Align("DLQ", numW, true), _theme.Control);
        Console.WriteLine();

        for (int i = 0; i < slice.Count; i++)
        {
            int globalIdx = start + i + 1;
            var e = slice[i];
            var kind = e.Kind == EntityKind.Queue ? "Queue" : "Sub";
            ColorConsole.Write(Align(globalIdx.ToString(), idxW, false), _theme.Number); Console.Write(" ");
            Console.Write(Align(kind, kindW, false)); Console.Write(" ");
            ColorConsole.Write(Align(TextTruncation.Truncate(e.Path, pathW), pathW, false), _theme.Letters); Console.Write(" ");
            Console.Write(Align(TextTruncation.Truncate(e.Status ?? string.Empty, statusW), statusW, false)); Console.Write(" ");
            ColorConsole.Write(Align(e.Total.ToString(), numW, true), _theme.Number); Console.Write(" ");
            ColorConsole.Write(Align(e.Active.ToString(), numW, true), _theme.Number); Console.Write(" ");
            ColorConsole.Write(Align(e.DeadLetter.ToString(), numW, true), _theme.Number);
            Console.WriteLine();
        }
    }
}
