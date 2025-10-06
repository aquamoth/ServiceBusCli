using Azure.Core;
using Azure.Messaging.ServiceBus;
using ServiceBusCli.Core;

namespace ServiceBusCli;

public sealed class BrowserApp
{
    private readonly TokenCredential _credential;
    private readonly IServiceBusDiscovery _discovery;
    private readonly Theme _theme;

    private readonly string? _azureSubscriptionId;
    private readonly string? _nsArg;
    private readonly string? _queueArg;
    private readonly string? _topicArg;
    private readonly string? _tSubArg;

    public BrowserApp(TokenCredential credential, IServiceBusDiscovery discovery, Theme theme,
        string? azureSubscriptionId = null, string? nsArg = null, string? queueArg = null, string? topicArg = null, string? topicSubscriptionArg = null)
    {
        _credential = credential;
        _discovery = discovery;
        _theme = theme;
        _azureSubscriptionId = azureSubscriptionId;
        _nsArg = nsArg;
        _queueArg = queueArg;
        _topicArg = topicArg;
        _tSubArg = topicSubscriptionArg;
    }

    private enum View { Namespaces, Entities, Messages }

    private sealed record MessageRow(
        long SequenceNumber,
        DateTimeOffset? Enqueued,
        string? MessageId,
        string? Subject,
        string? ContentType,
        string Preview,
        BinaryData Body,
        IReadOnlyDictionary<string, object> ApplicationProperties);

    public async Task RunAsync(CancellationToken ct = default)
    {
        var namespaces = new List<SBNamespace>();
        await foreach (var ns in _discovery.ListNamespacesAsync(_azureSubscriptionId, ct)) namespaces.Add(ns);

        SBNamespace? selectedNs = null;
        SBEntityId? selectedEntity = null;

        // Preselect if args supplied
        if (!string.IsNullOrWhiteSpace(_nsArg))
        {
            string Normalize(string s)
            {
                var v = s.Trim();
                v = v.Replace("https://", string.Empty).Replace("http://", string.Empty).Replace("sb://", string.Empty);
                if (v.EndsWith("/")) v = v[..^1];
                var slash = v.IndexOf('/');
                if (slash >= 0) v = v[..slash];
                var colon = v.IndexOf(':');
                if (colon >= 0) v = v[..colon];
                return v.ToLowerInvariant();
            }
            var target = Normalize(_nsArg);
            selectedNs = namespaces.FirstOrDefault(n => Normalize(n.Name) == target || Normalize(n.FullyQualifiedNamespace) == target);
        }

        View view = selectedNs == null ? View.Namespaces : View.Entities;
        var entitiesLister = new EntitiesLister(_credential);
        IReadOnlyList<EntityRow> entities = Array.Empty<EntityRow>();

        // Messages view state
        ServiceBusClient? messageClient = null;
        ServiceBusReceiver? receiver = null;
        long nextFromSequence = 0; // 0 => start of queue/subscription
        var pageHistory = new Stack<long>();
        var messages = new List<MessageRow>();
        string? status = null;

        // If args include entity, resolve it and jump to messages
        if (selectedNs != null && (!string.IsNullOrWhiteSpace(_queueArg) || (!string.IsNullOrWhiteSpace(_topicArg) && !string.IsNullOrWhiteSpace(_tSubArg))))
        {
            SBEntityId? pre = null;
            var preEntities = await entitiesLister.ListEntitiesAsync(selectedNs, ct);
            if (!string.IsNullOrWhiteSpace(_queueArg))
                pre = preEntities.Where(e => e.Kind == EntityKind.Queue && string.Equals(e.Path, _queueArg, StringComparison.OrdinalIgnoreCase)).Select(e => new QueueEntity(selectedNs, e.Path)).FirstOrDefault();
            else if (!string.IsNullOrWhiteSpace(_topicArg) && !string.IsNullOrWhiteSpace(_tSubArg))
                pre = preEntities.Where(e => e.Kind == EntityKind.Subscription && string.Equals(e.Path, $"{_topicArg}/Subscriptions/{_tSubArg}", StringComparison.OrdinalIgnoreCase)).Select(e => new SubscriptionEntity(selectedNs, _topicArg!, _tSubArg!)).FirstOrDefault();
            if (pre != null)
            {
                selectedEntity = pre;
                view = View.Messages;
            }
        }

        while (true)
        {
            Render(view, namespaces, selectedNs, entities, selectedEntity, messages, status);
            var key = Console.ReadKey(true);
            if (key.Key == ConsoleKey.Q) break;
            if (key.Key == ConsoleKey.PageDown)
            {
                if (view == View.Namespaces) _nsPage++;
                else if (view == View.Entities) _entPage++;
                else if (view == View.Messages)
                {
                    if (messages.Count > 0)
                    {
                        pageHistory.Push(nextFromSequence);
                        nextFromSequence = messages.Last().SequenceNumber + 1;
                        try
                        {
                            (messageClient, receiver) = await EnsureReceiverAsync(selectedNs!, selectedEntity!, messageClient, receiver, ct);
                            messages.Clear();
                            messages.AddRange(await FetchMessagesPageAsync(receiver!, nextFromSequence, ct));
                        }
                        catch (Exception ex) when (IsUnauthorized(ex))
                        {
                            view = View.Entities;
                            status = "Unauthorized: require Azure Service Bus Data Receiver (Listen) on entity.";
                        }
                        }
                }
                continue;
            }
            if (key.Key == ConsoleKey.PageUp)
            {
                if (view == View.Namespaces && _nsPage > 0) _nsPage--;
                else if (view == View.Entities && _entPage > 0) _entPage--;
                else if (view == View.Messages && pageHistory.Count > 0)
                {
                    nextFromSequence = pageHistory.Pop();
                    try
                    {
                        (messageClient, receiver) = await EnsureReceiverAsync(selectedNs!, selectedEntity!, messageClient, receiver, ct);
                        messages.Clear();
                        messages.AddRange(await FetchMessagesPageAsync(receiver!, nextFromSequence, ct));
                    }
                    catch (Exception ex) when (IsUnauthorized(ex))
                    {
                        view = View.Entities;
                        status = "Unauthorized: require Azure Service Bus Data Receiver (Listen) on entity.";
                    }
                }
                continue;
            }
            if (key.Key == ConsoleKey.Enter)
            {
                // Read a command line
                Console.SetCursorPosition(0, Console.WindowHeight - 1);
                Console.Write("Command (h for help)> ");
                var line = Console.ReadLine() ?? string.Empty;
                var cmd = CommandParser.Parse(line);
                if (cmd.Kind == CommandKind.Quit) break;
                if (view != View.Messages && cmd.Kind == CommandKind.Open && cmd.Index is > 0)
                {
                    if (cmd.Index is null || cmd.Index.Value > int.MaxValue) { continue; }
                    var index = (int)cmd.Index.Value - 1; // global index (1-based display)
                    if (view == View.Namespaces)
                    {
                        var idx = index;
                        if (idx >= 0 && idx < namespaces.Count)
                        {
                            selectedNs = namespaces[idx];
                            view = View.Entities;
                            _entPage = 0;
                            entities = await entitiesLister.ListEntitiesAsync(selectedNs, ct);
                        }
                    }
                    else if (view == View.Entities)
                    {
                        var idx = index;
                        if (idx >= 0 && idx < entities.Count)
                        {
                            var e = entities[idx];
                            selectedEntity = e.Kind == EntityKind.Queue
                                ? new QueueEntity(selectedNs!, e.Path)
                                : new SubscriptionEntity(selectedNs!, e.Path.Split('/')[0], e.Path.Split('/')[2]);
                            view = View.Messages;
                            pageHistory.Clear();
                            nextFromSequence = 0;
                            try
                            {
                                (messageClient, receiver) = await EnsureReceiverAsync(selectedNs!, selectedEntity!, messageClient, receiver, ct);
                                messages.Clear();
                                messages.AddRange(await FetchMessagesPageAsync(receiver!, nextFromSequence, ct));
                            }
                            catch (Exception ex) when (IsUnauthorized(ex))
                            {
                                view = View.Entities;
                                status = "Unauthorized: require Azure Service Bus Data Receiver (Listen) on entity.";
                            }
                        }
                    }
                }
                else if (cmd.Kind == CommandKind.Help)
                {
                    // No-op; header shows basic help
                }
                else if (view == View.Messages && cmd.Kind == CommandKind.Open && cmd.Index is > 0)
                {
                    var seq = cmd.Index!.Value;
                    var m = messages.FirstOrDefault(mm => mm.SequenceNumber == seq);
                    try
                    {
                        if (m is null)
                        {
                            // Jump to a page that should include seq
                            (messageClient, receiver) = await EnsureReceiverAsync(selectedNs!, selectedEntity!, messageClient, receiver, ct);
                            var requested = Math.Max(1, Console.WindowHeight - 5);
                            var startSeq = seq > requested ? seq - requested + 1 : seq; // try to center seq on page if possible
                            pageHistory.Push(nextFromSequence);
                            nextFromSequence = startSeq;
                            messages.Clear();
                            messages.AddRange(await FetchMessagesPageAsync(receiver!, startSeq, ct));
                            m = messages.FirstOrDefault(mm => mm.SequenceNumber == seq);
                        }
                        if (m is not null)
                        {
                            var em = new EditorMessage(
                                m.SequenceNumber,
                                m.Enqueued,
                                m.MessageId,
                                m.Subject,
                                m.ContentType,
                                m.Body,
                                m.ApplicationProperties
                            );
                            await EditorLauncher.OpenMessageAsync(em, selectedEntity!, selectedNs!, _theme, ct);
                        }
                        else
                        {
                            status = $"Sequence {seq} not found (may be expired).";
                        }
                    }
                    catch (Exception ex) when (IsUnauthorized(ex))
                    {
                        view = View.Entities;
                        status = "Unauthorized: require Azure Service Bus Data Receiver (Listen) on entity.";
                    }
                    catch (Exception ex)
                    {
                        status = $"Open failed: {ex.Message}";
                    }
                }
                continue;
            }
            // Numeric shortcut: single digit selects without typing 'open'
            if (char.IsDigit(key.KeyChar))
            {
                var digit = key.KeyChar.ToString();
                Console.SetCursorPosition(0, Console.WindowHeight - 1);
                Console.Write($"Command (h for help)> {digit}");
                var rest = Console.ReadLine() ?? string.Empty;
                if (int.TryParse(digit + rest, out var n))
                {
                    var cmd = new ParsedCommand(CommandKind.Open, n);
                    // Re-run same as above, but reuse logic
                    if (view == View.Namespaces)
                    {
                        var idx = n - 1;
                        if (idx >= 0 && idx < namespaces.Count)
                        {
                            selectedNs = namespaces[idx];
                            view = View.Entities;
                            _entPage = 0;
                            entities = await entitiesLister.ListEntitiesAsync(selectedNs, ct);
                        }
                    }
                    else if (view == View.Entities)
                    {
                        var idx = n - 1;
                        if (idx >= 0 && idx < entities.Count)
                        {
                            var e = entities[idx];
                            selectedEntity = e.Kind == EntityKind.Queue
                                ? new QueueEntity(selectedNs!, e.Path)
                                : new SubscriptionEntity(selectedNs!, e.Path.Split('/')[0], e.Path.Split('/')[2]);
                            view = View.Messages;
                            pageHistory.Clear();
                            nextFromSequence = 0;
                            try
                            {
                                (messageClient, receiver) = await EnsureReceiverAsync(selectedNs!, selectedEntity!, messageClient, receiver, ct);
                                messages.Clear();
                                messages.AddRange(await FetchMessagesPageAsync(receiver!, nextFromSequence, ct));
                            }
                            catch (Exception ex) when (IsUnauthorized(ex))
                            {
                                view = View.Entities;
                                status = "Unauthorized: require Azure Service Bus Data Receiver (Listen) on entity.";
                            }
                        }
                    }
                }
                continue;
            }
        }

        if (receiver != null) await receiver.CloseAsync();
        if (messageClient != null) await messageClient.DisposeAsync();
    }

    private int _nsPage;
    private int _entPage;

    private static (int start, int count, int page, int totalPages, int pageSize) Page(int page, int total)
    {
        var height = Console.WindowHeight;
        var reserved = 5; // header + footer lines
        var pageSize = Math.Max(1, height - reserved);
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        page = Math.Clamp(page, 0, totalPages - 1);
        var start = page * pageSize;
        var count = Math.Max(0, Math.Min(pageSize, total - start));
        return (start, count, page, totalPages, pageSize);
    }

    private async Task<(ServiceBusClient client, ServiceBusReceiver? receiver)> EnsureReceiverAsync(SBNamespace ns, SBEntityId entity, ServiceBusClient? client, ServiceBusReceiver? rcv, CancellationToken ct)
    {
        client ??= new ServiceBusClient(ns.FullyQualifiedNamespace, _credential);
        var receiver = entity switch
        {
            QueueEntity q => rcv != null && rcv.EntityPath == q.Path ? rcv : client.CreateReceiver(q.Path),
            SubscriptionEntity s => rcv != null && rcv.EntityPath == s.Path ? rcv : client.CreateReceiver(s.TopicName, s.SubscriptionName),
            _ => rcv
        };
        return (client, receiver);
    }

    private async Task<List<MessageRow>> FetchMessagesPageAsync(ServiceBusReceiver rcv, long fromSeq, CancellationToken ct)
    {
        int requested = Math.Max(1, Console.WindowHeight - 5);
        if (fromSeq > 0)
        {
            var msgs = await rcv.PeekMessagesAsync(requested, fromSequenceNumber: fromSeq, cancellationToken: ct);
            var list = new List<MessageRow>(msgs.Count);
            foreach (var m in msgs)
            {
                var body = TryPreview(m.Body);
                list.Add(new MessageRow(
                    m.SequenceNumber,
                    m.EnqueuedTime,
                    m.MessageId,
                    m.Subject,
                    m.ContentType,
                    body,
                    m.Body,
                    new Dictionary<string, object>(m.ApplicationProperties)
                ));
            }
            return list;
        }
        else
        {
            var msgs = await rcv.PeekMessagesAsync(maxMessages: requested, fromSequenceNumber: null, cancellationToken: ct);
            var list = new List<MessageRow>(msgs.Count);
            foreach (var m in msgs)
            {
                var body = TryPreview(m.Body);
                list.Add(new MessageRow(
                    m.SequenceNumber,
                    m.EnqueuedTime,
                    m.MessageId,
                    m.Subject,
                    m.ContentType,
                    body,
                    m.Body,
                    new Dictionary<string, object>(m.ApplicationProperties)
                ));
            }
            return list;
        }
    }

    private static string TryPreview(BinaryData body)
    {
        try
        {
            var s = body.ToString();
            s = s.Replace('\n', ' ').Replace('\r', ' ');
            return TextTruncation.Truncate(s, Math.Max(20, Console.WindowWidth - 40));
        }
        catch
        {
            var bytes = body.ToArray();
            return TextTruncation.Truncate(Convert.ToHexString(bytes, 0, Math.Min(32, bytes.Length)), 40);
        }
    }

    private static bool IsUnauthorized(Exception ex)
    {
        if (ex is UnauthorizedAccessException) return true;
        if (ex is Azure.RequestFailedException rfe && (rfe.Status == 401 || rfe.Status == 403)) return true;
        if (ex is Azure.Messaging.ServiceBus.ServiceBusException sb && sb.Message.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase)) return true;
        if (ex.InnerException != null) return IsUnauthorized(ex.InnerException);
        return false;
    }

    // Status is rendered by Render() when provided

    private void Render(View view, List<SBNamespace> namespaces, SBNamespace? selectedNs, IReadOnlyList<EntityRow> entities, SBEntityId? selectedEntity, IReadOnlyList<MessageRow> messages, string? status)
    {
        Console.Clear();
        var title = view switch
        {
            View.Namespaces => "Service Bus: Select Namespace",
            View.Entities => $"Namespace: {selectedNs!.Name} — Select Queue/Subscription",
            View.Messages => selectedEntity is QueueEntity q
                ? $"{selectedNs!.Name} — Queue {q.QueueName}"
                : selectedEntity is SubscriptionEntity s ? $"{selectedNs!.Name} — Subscription {s.TopicName}/{s.SubscriptionName}" : "Messages",
            _ => "Service Bus"
        };
        string pageStr = string.Empty;
        if (view == View.Namespaces)
        {
            var pi = Page(_nsPage, namespaces.Count);
            pageStr = $"PAGE {pi.page + 1}/{Math.Max(1, pi.totalPages)}";
        }
        else if (view == View.Entities)
        {
            var pi = Page(_entPage, entities.Count);
            pageStr = $"PAGE {pi.page + 1}/{Math.Max(1, pi.totalPages)}";
        }
        var width = Console.WindowWidth;
        Console.WriteLine(TextTruncation.Truncate(title.PadRight(Math.Max(0, width - pageStr.Length)) + pageStr, width));
        Console.WriteLine();

        if (view == View.Namespaces)
        {
            var (start, count, _, _, _) = Page(_nsPage, namespaces.Count);
            for (int i = 0; i < count; i++)
            {
                var idx = start + i;
                var ns = namespaces[idx];
                Console.WriteLine($"{idx + 1,4}. {ns.Name,-30} {ns.ResourceGroup,-24} {ns.SubscriptionId}");
            }
        }
        else if (view == View.Entities)
        {
            var (start, count, _, _, _) = Page(_entPage, entities.Count);
            // Header
            Console.WriteLine("  #  Kind  Path                                    Status       Total    Active   DLQ");
            for (int i = 0; i < count; i++)
            {
                var idx = start + i;
                var e = entities[idx];
                var kind = e.Kind == EntityKind.Queue ? "Queue" : "Sub  ";
                Console.WriteLine($"{idx + 1,4}. {kind} {TextTruncation.Truncate(e.Path, 40),-40} {TextTruncation.Truncate(e.Status, 10),-10} {e.Total,8} {e.Active,8} {e.DeadLetter,8}");
            }
        }
        else if (view == View.Messages)
        {
            Console.WriteLine("Seq            Enqueued              MessageId           Subject           Preview");
            for (int i = 0; i < messages.Count; i++)
            {
                var m = messages[i];
                var enq = m.Enqueued?.ToString("u") ?? "";
                Console.WriteLine($"{m.SequenceNumber,12} {TextTruncation.Truncate(enq, 20),-20} {TextTruncation.Truncate(m.MessageId ?? string.Empty, 18),-18} {TextTruncation.Truncate(m.Subject ?? string.Empty, 16),-16} {TextTruncation.Truncate(m.Preview, Math.Max(20, Console.WindowWidth - 63))}");
            }
        }

        // Footer / prompt line
        Console.SetCursorPosition(0, Console.WindowHeight - 2);
        if (!string.IsNullOrEmpty(status))
        {
            var msg = TextTruncation.Truncate(status, Console.WindowWidth);
            Console.WriteLine(msg.PadRight(Console.WindowWidth));
        }
        else
        {
            Console.WriteLine("Use PageUp/PageDown. Type number+Enter to select, q to quit.");
        }
        Console.SetCursorPosition(0, Console.WindowHeight - 1);
        Console.Write("Command (h for help)> ");
    }
}
