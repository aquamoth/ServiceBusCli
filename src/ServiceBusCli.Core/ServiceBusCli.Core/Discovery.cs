using Azure;
using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.Resources;
using Azure.ResourceManager.ServiceBus;

namespace ServiceBusCli.Core;

public interface IServiceBusDiscovery
{
    IAsyncEnumerable<SBNamespace> ListNamespacesAsync(CancellationToken ct = default);
    IAsyncEnumerable<QueueEntity> ListQueuesAsync(SBNamespace ns, CancellationToken ct = default);
    IAsyncEnumerable<SubscriptionEntity> ListSubscriptionsAsync(SBNamespace ns, CancellationToken ct = default);
}

public sealed class ArmServiceBusDiscovery(TokenCredential credential) : IServiceBusDiscovery
{
    private readonly ArmClient _arm = new(credential);

    public async IAsyncEnumerable<SBNamespace> ListNamespacesAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (SubscriptionResource sub in _arm.GetSubscriptions().GetAllAsync())
        {
            var subId = sub.Data.SubscriptionId;
            foreach (ServiceBusNamespaceResource ns in sub.GetServiceBusNamespaces())
            {
                var fqdn = ns.Data?.ServiceBusEndpoint ?? $"{ns.Data?.Name}.servicebus.windows.net";
                var rg = ns.Id.ResourceGroupName ?? string.Empty;
                var name = ns.Data?.Name ?? ns.Id.Name;
                yield return new SBNamespace(subId, rg, name, fqdn);
            }
        }
    }

    public async IAsyncEnumerable<QueueEntity> ListQueuesAsync(SBNamespace ns, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var subId = ns.SubscriptionId;
        var nsId = ServiceBusNamespaceResource.CreateResourceIdentifier(subId, ns.ResourceGroup, ns.Name);
        var nsRes = _arm.GetServiceBusNamespaceResource(nsId);
        foreach (ServiceBusQueueResource q in nsRes.GetServiceBusQueues())
        {
            var qName = q.Data?.Name ?? q.Id.Name;
            yield return new QueueEntity(ns, qName);
        }
    }

    public async IAsyncEnumerable<SubscriptionEntity> ListSubscriptionsAsync(SBNamespace ns, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var subId = ns.SubscriptionId;
        var nsId = ServiceBusNamespaceResource.CreateResourceIdentifier(subId, ns.ResourceGroup, ns.Name);
        var nsRes = _arm.GetServiceBusNamespaceResource(nsId);
        foreach (ServiceBusTopicResource topic in nsRes.GetServiceBusTopics())
        {
            var topicName = topic.Data?.Name ?? topic.Id.Name;
            foreach (ServiceBusSubscriptionResource s in topic.GetServiceBusSubscriptions())
            {
                var subName = s.Data?.Name ?? s.Id.Name;
                yield return new SubscriptionEntity(ns, topicName, subName);
            }
        }
    }
}
