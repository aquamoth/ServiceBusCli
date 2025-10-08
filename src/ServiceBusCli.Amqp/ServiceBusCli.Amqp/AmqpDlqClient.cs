using System;
using System.Threading;
using System.Threading.Tasks;

namespace ServiceBusCli.Amqp;

// Placeholder implementation. In a future pass, this will use Microsoft.Azure.Amqp to
// open a receiver on '<queue>/$DeadLetterQueue' with a com.microsoft:session-filter and
// complete the target message with Accepted disposition.
public sealed class AmqpDlqClient : IAmqpDlqClient
{
    public async Task<bool> CompleteDlqSessionMessageAsync(
        string fullyQualifiedNamespace,
        string queueName,
        string sessionId,
        long sequenceNumber,
        int maxReceive,
        string? connectionString,
        CancellationToken ct)
    {
        // TODO(amqp): Implement raw AMQP using Microsoft.Azure.Amqp
        await Task.Yield();
        return false;
    }
}

