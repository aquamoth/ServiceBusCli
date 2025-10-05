using ServiceBusCli.Core;

namespace ServiceBusCli;

public static class SelectionUi
{
    public static async Task<SBEntityId?> SelectEntityAsync(
        IServiceBusDiscovery discovery,
        string? nsArg,
        string? queueArg,
        string? topicArg,
        string? subArg,
        CancellationToken ct = default)
    {
        var namespaces = new List<SBNamespace>();
        await foreach (var ns in discovery.ListNamespacesAsync(ct)) namespaces.Add(ns);

        SBNamespace? selectedNs = null;
        if (!string.IsNullOrWhiteSpace(nsArg))
        {
            selectedNs = namespaces.FirstOrDefault(n => string.Equals(n.Name, nsArg, StringComparison.OrdinalIgnoreCase) || string.Equals(n.FullyQualifiedNamespace, nsArg, StringComparison.OrdinalIgnoreCase));
            if (selectedNs == null)
            {
                Console.WriteLine($"Namespace '{nsArg}' not found among {namespaces.Count} discovered.");
            }
        }

        while (selectedNs == null)
        {
            if (namespaces.Count == 0)
            {
                Console.WriteLine("No Service Bus namespaces found in accessible subscriptions.");
                return null;
            }
            Console.WriteLine("Select a Service Bus namespace:");
            for (int i = 0; i < namespaces.Count; i++)
            {
                var ns = namespaces[i];
                Console.WriteLine($"  {i + 1,2}. {ns.Name}  ({ns.ResourceGroup}/{ns.SubscriptionId})  [{ns.FullyQualifiedNamespace}]");
            }
            Console.Write("Enter number (or q to quit): ");
            var input = Console.ReadLine();
            if (string.Equals(input, "q", StringComparison.OrdinalIgnoreCase)) return null;
            if (int.TryParse(input, out var idx) && idx >= 1 && idx <= namespaces.Count)
            {
                selectedNs = namespaces[idx - 1];
            }
        }

        // Entities: queues + topic subscriptions
        var entities = new List<SBEntityId>();
        await foreach (var q in discovery.ListQueuesAsync(selectedNs, ct)) entities.Add(q);
        await foreach (var s in discovery.ListSubscriptionsAsync(selectedNs, ct)) entities.Add(s);

        SBEntityId? preselected = null;
        if (!string.IsNullOrWhiteSpace(queueArg))
        {
            preselected = entities.OfType<QueueEntity>().FirstOrDefault(x => string.Equals(x.QueueName, queueArg, StringComparison.OrdinalIgnoreCase));
        }
        else if (!string.IsNullOrWhiteSpace(topicArg) && !string.IsNullOrWhiteSpace(subArg))
        {
            preselected = entities.OfType<SubscriptionEntity>().FirstOrDefault(x => string.Equals(x.TopicName, topicArg, StringComparison.OrdinalIgnoreCase) && string.Equals(x.SubscriptionName, subArg, StringComparison.OrdinalIgnoreCase));
        }
        if (preselected != null) return preselected;

        if (entities.Count == 0)
        {
            Console.WriteLine("No queues or subscriptions found in the selected namespace.");
            return null;
        }

        Console.WriteLine($"Selected namespace: {selectedNs.Name}  ({selectedNs.ResourceGroup}/{selectedNs.SubscriptionId})");
        Console.WriteLine("Select an entity:");
        for (int i = 0; i < entities.Count; i++)
        {
            Console.WriteLine($"  {i + 1,2}. {entities[i].DisplayName}");
        }
        while (true)
        {
            Console.Write("Enter number (or q to quit): ");
            var input = Console.ReadLine();
            if (string.Equals(input, "q", StringComparison.OrdinalIgnoreCase)) return null;
            if (int.TryParse(input, out var idx) && idx >= 1 && idx <= entities.Count)
            {
                return entities[idx - 1];
            }
        }
    }
}

