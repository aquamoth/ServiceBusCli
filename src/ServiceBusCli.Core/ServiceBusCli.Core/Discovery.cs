using Azure;
using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.Resources;
using Azure.ResourceManager.ServiceBus;

namespace ServiceBusCli.Core;

public interface IServiceBusDiscovery
{
    IAsyncEnumerable<SBNamespace> ListNamespacesAsync(string? subscriptionId = null, CancellationToken ct = default);
    IAsyncEnumerable<QueueEntity> ListQueuesAsync(SBNamespace ns, CancellationToken ct = default);
    IAsyncEnumerable<SubscriptionEntity> ListSubscriptionsAsync(SBNamespace ns, CancellationToken ct = default);
}

public sealed class ArmServiceBusDiscovery(TokenCredential credential) : IServiceBusDiscovery
{
    private readonly ArmClient _arm = new(credential);

    public async IAsyncEnumerable<SBNamespace> ListNamespacesAsync(string? subscriptionId = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(subscriptionId))
        {
            SubscriptionResource sub;
            try
            {
                var subId = subscriptionId!;
                var subResId = SubscriptionResource.CreateResourceIdentifier(subId);
                sub = _arm.GetSubscriptionResource(subResId);
                await foreach (var _ in sub.GetLocationsAsync(cancellationToken: ct)) { break; } // touch to validate access
            }
            catch (RequestFailedException)
            {
                yield break;
            }

            var subIdStr = sub.Data.SubscriptionId;
            foreach (ServiceBusNamespaceResource ns in sub.GetServiceBusNamespaces())
            {
                var fqdn = ns.Data?.ServiceBusEndpoint ?? $"{ns.Data?.Name}.servicebus.windows.net";
                var rg = ns.Id.ResourceGroupName ?? string.Empty;
                var name = ns.Data?.Name ?? ns.Id.Name;
                yield return new SBNamespace(subIdStr, rg, name, fqdn);
            }
            yield break;
        }

        await foreach (SubscriptionResource sub in _arm.GetSubscriptions().GetAllAsync())
        {
            var subId = sub.Data.SubscriptionId;
            var items = new List<SBNamespace>();
            try
            {
                var page = sub.GetServiceBusNamespaces();
                foreach (ServiceBusNamespaceResource ns in page)
                {
                    var fqdn = ns.Data?.ServiceBusEndpoint ?? $"{ns.Data?.Name}.servicebus.windows.net";
                    var rg = ns.Id.ResourceGroupName ?? string.Empty;
                    var name = ns.Data?.Name ?? ns.Id.Name;
                    items.Add(new SBNamespace(subId, rg, name, fqdn));
                }
            }
            catch (RequestFailedException ex) when (ex.Status == 403 || ex.Status == 401)
            {
                // Skip subscriptions we cannot enumerate
                continue;
            }
            foreach (var item in items) yield return item;
        }
    }

    public async IAsyncEnumerable<QueueEntity> ListQueuesAsync(SBNamespace ns, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var subId = ns.SubscriptionId;
        var nsId = ServiceBusNamespaceResource.CreateResourceIdentifier(subId, ns.ResourceGroup, ns.Name);
        var nsRes = _arm.GetServiceBusNamespaceResource(nsId);
        await foreach (ServiceBusQueueResource q in nsRes.GetServiceBusQueues().GetAllAsync(cancellationToken: ct))
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
        await foreach (ServiceBusTopicResource topic in nsRes.GetServiceBusTopics().GetAllAsync(cancellationToken: ct))
        {
            var topicName = topic.Data?.Name ?? topic.Id.Name;
            await foreach (ServiceBusSubscriptionResource s in topic.GetServiceBusSubscriptions().GetAllAsync(cancellationToken: ct))
            {
                var subName = s.Data?.Name ?? s.Id.Name;
                yield return new SubscriptionEntity(ns, topicName, subName);
            }
        }
    }
}
