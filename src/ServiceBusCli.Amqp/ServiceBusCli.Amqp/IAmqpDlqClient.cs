namespace ServiceBusCli.Amqp;

public interface IAmqpDlqClient
{
    // Attempts to complete a message from a DLQ session by reading up to maxReceive messages in that session.
    // Returns true if the target was found and completed.
    Task<bool> CompleteDlqSessionMessageAsync(
        string fullyQualifiedNamespace,
        string queueName,
        string sessionId,
        long sequenceNumber,
        int maxReceive,
        string? connectionString,
        System.Threading.CancellationToken ct);
}

