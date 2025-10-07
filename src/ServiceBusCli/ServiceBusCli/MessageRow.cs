using Azure;
using Azure.Messaging.ServiceBus;

namespace ServiceBusCli;

// Lightweight row model for messages used by the TUI and helpers
internal sealed record MessageRow(
    long SequenceNumber,
    DateTimeOffset? Enqueued,
    string? MessageId,
    string? Subject,
    string? SessionId,
    string? ContentType,
    string Preview,
    BinaryData Body,
    IReadOnlyDictionary<string, object> ApplicationProperties);

