using System;
using System.Collections.Generic;
using Azure.Core;

namespace ServiceBusCli;

public sealed record EditorMessage(
    long SequenceNumber,
    DateTimeOffset? Enqueued,
    string? MessageId,
    string? Subject,
    string? ContentType,
    BinaryData Body,
    IReadOnlyDictionary<string, object> ApplicationProperties
);

