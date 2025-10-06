using System;
using System.Collections.Generic;
using System.Linq;

namespace ServiceBusCli.Core;

public static class SelectionHelper
{
    public static IEnumerable<SBNamespace> SortNamespaces(IEnumerable<SBNamespace> source)
        => source.OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase);

    public static IEnumerable<EntityRow> SortEntities(IEnumerable<EntityRow> source)
        => source.OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase);

    public static SBNamespace? ResolveNamespaceSelection(IReadOnlyList<SBNamespace> namespaces, int oneBasedIndex)
    {
        if (oneBasedIndex <= 0) return null;
        var sorted = SortNamespaces(namespaces).ToList();
        var idx = oneBasedIndex - 1;
        return idx >= 0 && idx < sorted.Count ? sorted[idx] : null;
    }

    public static EntityRow? ResolveEntitySelection(IReadOnlyList<EntityRow> entities, int oneBasedIndex)
    {
        if (oneBasedIndex <= 0) return null;
        var sorted = SortEntities(entities).ToList();
        var idx = oneBasedIndex - 1;
        return idx >= 0 && idx < sorted.Count ? sorted[idx] : null;
    }
}
