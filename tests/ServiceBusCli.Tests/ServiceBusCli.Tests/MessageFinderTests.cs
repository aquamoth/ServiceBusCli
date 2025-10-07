using FluentAssertions;
using ServiceBusCli;

namespace ServiceBusCli.Tests;

public class MessageFinderTests
{
    [Fact]
    public async Task Finds_sequence_via_single_peek_when_not_in_page()
    {
        long target = 2710;
        int pageSize = 10;
        // Simulate page fetch that does NOT include the target
        Task<IReadOnlyList<MessageRow>> Fetch(long from, int count)
        {
            var list = new List<MessageRow>();
            for (int i = 0; i < pageSize; i++)
            {
                var seq = from + i;
                if (seq == target) continue; // omit target to simulate not-in-view
                list.Add(new MessageRow(seq, DateTimeOffset.UtcNow, null, null, null, null, string.Empty, new BinaryData(Array.Empty<byte>()), new Dictionary<string, object>()));
            }
            return Task.FromResult((IReadOnlyList<MessageRow>)list);
        }

        Task<MessageRow?> PeekSingle(long seq)
        {
            if (seq == target)
            {
                return Task.FromResult<MessageRow?>(new MessageRow(seq, DateTimeOffset.UtcNow, null, null, null, null, string.Empty, new BinaryData(Array.Empty<byte>()), new Dictionary<string, object>()));
            }
            return Task.FromResult<MessageRow?>(null);
        }

        var found = await MessageFinder.FindAsync(target, pageSize, Fetch, PeekSingle);
        found.Should().NotBeNull();
        found!.SequenceNumber.Should().Be(target);
    }
}

