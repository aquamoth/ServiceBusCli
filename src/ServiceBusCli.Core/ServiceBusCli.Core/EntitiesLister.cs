using Azure;
using Azure.Core;
using Azure.Messaging.ServiceBus.Administration;

namespace ServiceBusCli.Core;

public enum EntityKind { Queue, Subscription }

public sealed record EntityRow(
    EntityKind Kind,
    string Path,
    string Status,
    long Total,
    long Active,
    long DeadLetter
);

public interface IEntitiesLister
{
    Task<IReadOnlyList<EntityRow>> ListEntitiesAsync(SBNamespace ns, CancellationToken ct = default);
}

public sealed class EntitiesLister(TokenCredential credential) : IEntitiesLister
{
    public async Task<IReadOnlyList<EntityRow>> ListEntitiesAsync(SBNamespace ns, CancellationToken ct = default)
    {
        var admin = new ServiceBusAdministrationClient(ns.FullyQualifiedNamespace, credential);

        var rows = new List<EntityRow>();

        // Queues: fetch properties and runtime in bulk
        var queueStatus = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await foreach (var qp in admin.GetQueuesAsync(ct))
        {
            queueStatus[qp.Name] = qp.Status.ToString();
        }
        await foreach (var qrp in admin.GetQueuesRuntimePropertiesAsync(ct))
        {
            queueStatus.TryGetValue(qrp.Name, out var statusStr);
            var dead = qrp.DeadLetterMessageCount; // shortcut property added in SDKs; fallback to details if needed
            if (dead == 0 && qrp is { TransferDeadLetterMessageCount: 0 } && qrp.ActiveMessageCount == 0 && qrp.ScheduledMessageCount == 0)
            {
                // Some SDKs only expose details:
                dead = qrp.TotalMessageCount - qrp.ActiveMessageCount - qrp.ScheduledMessageCount;
            }
            rows.Add(new EntityRow(
                EntityKind.Queue,
                qrp.Name,
                statusStr ?? "Unknown",
                qrp.TotalMessageCount,
                qrp.ActiveMessageCount,
                dead
            ));
        }

        // Subscriptions: iterate topics and enumerate subs
        await foreach (var topic in admin.GetTopicsAsync(ct))
        {
            var subStatus = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            await foreach (var sp in admin.GetSubscriptionsAsync(topic.Name, ct))
            {
                subStatus[$"{topic.Name}/{sp.SubscriptionName}"] = sp.Status.ToString();
            }
            await foreach (var srp in admin.GetSubscriptionsRuntimePropertiesAsync(topic.Name, ct))
            {
                var key = $"{topic.Name}/{srp.SubscriptionName}";
                subStatus.TryGetValue(key, out var statusStr);
                var dead = srp.DeadLetterMessageCount;
                rows.Add(new EntityRow(
                    EntityKind.Subscription,
                    $"{topic.Name}/Subscriptions/{srp.SubscriptionName}",
                    statusStr ?? "Unknown",
                    srp.TotalMessageCount,
                    srp.ActiveMessageCount,
                    dead
                ));
            }
        }

        // Sort: Queues first by name, then subscriptions by path
        return rows
            .OrderBy(r => r.Kind)
            .ThenBy(r => r.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

