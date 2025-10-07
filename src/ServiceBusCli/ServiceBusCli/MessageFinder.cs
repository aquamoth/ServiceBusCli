using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ServiceBusCli;

internal static class MessageFinder
{
    // Tries to locate a message by sequence using a page fetch; falls back to a targeted peek
    public static async Task<MessageRow?> FindAsync(
        long sequence,
        int pageSize,
        Func<long, int, Task<IReadOnlyList<MessageRow>>> fetchPage,
        Func<long, Task<MessageRow?>> peekSingle)
    {
        long startSeq = sequence > pageSize ? sequence - pageSize + 1 : sequence;
        var page = await fetchPage(startSeq, pageSize);
        var found = page.FirstOrDefault(m => m.SequenceNumber == sequence);
        if (found is not null) return found;
        // Fallback: direct peek of the exact sequence
        return await peekSingle(sequence);
    }
}

