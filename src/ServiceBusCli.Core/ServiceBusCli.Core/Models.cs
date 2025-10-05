using System.Text;

namespace ServiceBusCli.Core;

public sealed record SBNamespace(
    string SubscriptionId,
    string ResourceGroup,
    string Name,
    string FullyQualifiedNamespace
)
{
    public override string ToString() => $"{Name} ({ResourceGroup}/{SubscriptionId})";
}

public abstract record SBEntityId(SBNamespace Namespace)
{
    public abstract string DisplayName { get; }
}

public sealed record QueueEntity(SBNamespace Namespace, string QueueName) : SBEntityId(Namespace)
{
    public override string DisplayName => $"Queue {QueueName}";
    public string Path => QueueName;
}

public sealed record SubscriptionEntity(SBNamespace Namespace, string TopicName, string SubscriptionName) : SBEntityId(Namespace)
{
    public override string DisplayName => $"Sub {TopicName}/Subscriptions/{SubscriptionName}";
    public string Path => $"{TopicName}/Subscriptions/{SubscriptionName}";
}

