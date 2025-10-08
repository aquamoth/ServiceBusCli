using Azure.Core;
using Azure.Messaging.ServiceBus;
using ServiceBusCli.Core;

namespace ServiceBusCli;

public sealed partial class BrowserApp
{
    private readonly TokenCredential _credential;
    private readonly IServiceBusDiscovery _discovery;
    private readonly Theme _theme;
    private long? _activeMessageCount; // runtime ActiveMessageCount for current entity
    private int _msgPage = 1; // messages view page index (1-based)
    private MessageMode _messageMode = MessageMode.Normal; // normal vs DLQ

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
    private enum MessageMode { Normal, DeadLetter }
    private sealed record ViewState(View View, SBNamespace? Namespace, SBEntityId? Entity, int NsPage, int EntPage);

    // moved to its own file for reuse in helpers/tests

    public async Task RunAsync(CancellationToken ct = default)
    {
        var namespaces = new List<SBNamespace>();
        await foreach (var ns in _discovery.ListNamespacesAsync(_azureSubscriptionId, ct)) namespaces.Add(ns);
        namespaces = SelectionHelper.SortNamespaces(namespaces).ToList();

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
        long nextFromSequence = 1; // explicit start of queue/subscription
        var pageHistory = new Stack<long>();
        var messages = new List<MessageRow>();
        string? status = null;
        var viewStack = new Stack<ViewState>();
        bool? sessionEnabled = null;

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

        var editor = new LineEditorEngine();
        editor.SetInitial(string.Empty);
        var history = new List<string>();
        var historyNav = new HistoryNavigator(history);
        bool needsRender = true;
        while (true)
        {
            if (needsRender)
            {
                var inputView = editor.GetView(Math.Max(1, Console.WindowWidth - "Command (h for help)> ".Length));
                Render(view, namespaces, selectedNs, entities, selectedEntity, messages, status, inputView);
                PositionPromptCursor(editor);
                needsRender = false;
            }
            var key = Console.ReadKey(true);
            // Only special hotkeys: ESC to navigate back, PageUp/PageDown for paging.
            if (key.Key == ConsoleKey.Escape)
            {
                if (viewStack.Count > 0)
                {
                    var back = viewStack.Pop();
                    view = back.View;
                    selectedNs = back.Namespace;
                    selectedEntity = back.Entity;
                    _nsPage = back.NsPage;
                    _entPage = back.EntPage;
                    messages.Clear();
                    pageHistory.Clear();
                    nextFromSequence = 1;
                    _msgPage = 1;
                }
                needsRender = true;
                continue;
            }
            // Editing: backspace in command buffer
            if (key.Key == ConsoleKey.Backspace)
            {
                historyNav.TransitionToDraftFromHistory(editor.Buffer.ToString());
                editor.Backspace();
                RedrawPrompt(editor);
                continue;
            }
            if (key.Key == ConsoleKey.Delete)
            {
                historyNav.TransitionToDraftFromHistory(editor.Buffer.ToString());
                editor.Delete();
                RedrawPrompt(editor);
                continue;
            }
            if (key.Key == ConsoleKey.PageDown)
            {
                if (view == View.Namespaces) _nsPage++;
                else if (view == View.Entities) _entPage++;
                else if (view == View.Messages)
                {
                    if (messages.Count > 0)
                    {
                        var currentFirst = messages.FirstOrDefault()?.SequenceNumber ?? nextFromSequence;
                        pageHistory.Push(currentFirst);
                        nextFromSequence = messages.Last().SequenceNumber + 1;
                            try
                            {
                            (messageClient, receiver) = await EnsureReceiverAsync(selectedNs!, selectedEntity!, _messageMode, messageClient, receiver, ct);
                            messages.Clear();
                            var req = GetMessagePageRequestRows(view, selectedNs, selectedEntity);
                            messages.AddRange(await FetchMessagesPageAsync(receiver!, nextFromSequence, req, ct));
                                _msgPage++;
                                // Do not refresh active count on every page to keep paging snappy
                            }
                            catch (Exception ex) when (IsUnauthorized(ex))
                            {
                                view = View.Entities;
                                status = "Unauthorized: require Azure Service Bus Data Receiver (Listen) on entity.";
                            }
                        }
                }
                needsRender = true;
                continue;
            }
            if (key.Key == ConsoleKey.PageUp)
            {
                if (view == View.Namespaces && _nsPage > 0) _nsPage--;
                else if (view == View.Entities && _entPage > 0) _entPage--;
                else if (view == View.Messages)
                {
                    if (pageHistory.Count > 0)
                    {
                        nextFromSequence = pageHistory.Pop();
                    }
                    else if (messages.Count > 0)
                    {
                        var pageSize = GetMessagePageRequestRows(view, selectedNs, selectedEntity);
                        var firstSeq = messages.First().SequenceNumber;
                        var prevStart = firstSeq > pageSize ? firstSeq - pageSize : 1;
                        nextFromSequence = prevStart;
                    }
                    else
                    {
                        continue;
                    }
                    try
                    {
                        (messageClient, receiver) = await EnsureReceiverAsync(selectedNs!, selectedEntity!, _messageMode, messageClient, receiver, ct);
                        messages.Clear();
                        var req = GetMessagePageRequestRows(view, selectedNs, selectedEntity);
                        messages.AddRange(await FetchMessagesPageAsync(receiver!, nextFromSequence, req, ct));
                        _msgPage = Math.Max(1, _msgPage - 1);
                        // Do not refresh active count on every page to keep paging snappy
                    }
                    catch (Exception ex) when (IsUnauthorized(ex))
                    {
                        view = View.Entities;
                        status = "Unauthorized: require Azure Service Bus Data Receiver (Listen) on entity.";
                    }
                }
                needsRender = true;
                continue;
            }
            if (key.Key == ConsoleKey.Enter)
            {
                // Parse and execute current command-line buffer
                var lineText = editor.Buffer.ToString();
                var cmd = CommandParser.Parse(lineText);
                editor.SetInitial(string.Empty);
                if (!string.IsNullOrWhiteSpace(lineText))
                {
                    if (history.Count == 0 || !string.Equals(history[^1], lineText, StringComparison.Ordinal))
                        history.Add(lineText);
                    historyNav.ResetToBottom();
                }
                if (cmd.Kind == CommandKind.Quit) break;
                if (view != View.Messages && cmd.Index is > 0 && (cmd.Kind == CommandKind.Open || cmd.Kind == CommandKind.Queue || cmd.Kind == CommandKind.Dlq))
                {
                    if (cmd.Index is null || cmd.Index.Value > int.MaxValue) { continue; }
                    var index = (int)cmd.Index.Value - 1; // global index (1-based display)
                    if (view == View.Namespaces)
                    {
                        var idx = index;
                        if (idx >= 0 && idx < namespaces.Count)
                        {
                            viewStack.Push(new ViewState(view, selectedNs, selectedEntity, _nsPage, _entPage));
                            viewStack.Push(new ViewState(view, selectedNs, selectedEntity, _nsPage, _entPage));
                            selectedNs = namespaces[idx];
                            view = View.Entities;
                            _entPage = 0;
                            entities = (await entitiesLister.ListEntitiesAsync(selectedNs, ct)).ToList();
                            entities = SelectionHelper.SortEntities(entities).ToList();
                        }
                    }
                    else if (view == View.Entities)
                    {
                        var idx = index;
                        if (idx >= 0 && idx < entities.Count)
                        {
                            var e = entities[idx];
                            bool openDlq = cmd.Kind == CommandKind.Dlq;
                            if (openDlq && e.Kind != EntityKind.Queue)
                            {
                                status = "DLQ is only available for queues.";
                                needsRender = true;
                                continue;
                            }
                            viewStack.Push(new ViewState(view, selectedNs, selectedEntity, _nsPage, _entPage));
                            selectedEntity = e.Kind == EntityKind.Queue
                                ? new QueueEntity(selectedNs!, e.Path)
                                : new SubscriptionEntity(selectedNs!, e.Path.Split('/')[0], e.Path.Split('/')[2]);
                            view = View.Messages;
                            pageHistory.Clear();
                            nextFromSequence = 1;
                            _msgPage = 1;
                            _messageMode = openDlq ? MessageMode.DeadLetter : MessageMode.Normal;
                            if (receiver != null) { await receiver.CloseAsync(); receiver = null; }
                            if (messageClient != null) { await messageClient.DisposeAsync(); messageClient = null; }
                            try
                            {
                                sessionEnabled = await DetermineSessionEnabledAsync(selectedNs!, selectedEntity!, ct);
                            }
                            catch { sessionEnabled = null; }
                            try
                            {
                                (messageClient, receiver) = await EnsureReceiverAsync(selectedNs!, selectedEntity!, _messageMode, messageClient, receiver, ct);
                                messages.Clear();
                                var req3 = GetMessagePageRequestRows(view, selectedNs, selectedEntity);
                                messages.AddRange(await FetchMessagesPageAsync(receiver!, nextFromSequence, req3, ct));
                                try { _activeMessageCount = await GetActiveMessageCountAsync(selectedNs!, selectedEntity!, ct); } catch { _activeMessageCount = null; }
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
                            (messageClient, receiver) = await EnsureReceiverAsync(selectedNs!, selectedEntity!, _messageMode, messageClient, receiver, ct);
                            var requested = Math.Max(1, Console.WindowHeight - 6);
                            var startSeq = seq > requested ? seq - requested + 1 : seq; // try to center seq on page if possible
                            pageHistory.Push(nextFromSequence);
                            nextFromSequence = startSeq;
                            messages.Clear();
                            var req2 = GetMessagePageRequestRows(view, selectedNs, selectedEntity);
                            messages.AddRange(await FetchMessagesPageAsync(receiver!, startSeq, req2, ct));
                            _msgPage++;
                            m = messages.FirstOrDefault(mm => mm.SequenceNumber == seq);
                            if (m is null)
                            {
                                // Fallback: exact single peek
                                var single = await receiver!.PeekMessagesAsync(1, fromSequenceNumber: seq, cancellationToken: ct);
                                var mm = single.FirstOrDefault();
                                if (mm is not null && mm.SequenceNumber == seq)
                                {
                                    m = new MessageRow(
                                        mm.SequenceNumber,
                                        mm.EnqueuedTime,
                                        mm.MessageId,
                                        mm.Subject,
                                        mm.SessionId,
                                        mm.ContentType,
                                        TryPreview(mm.Body),
                                        mm.Body,
                                        new Dictionary<string, object>(mm.ApplicationProperties)
                                    );
                                }
                            }
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
                else if (view == View.Messages && (cmd.Kind == CommandKind.Dlq || cmd.Kind == CommandKind.Queue))
                {
                    // Toggle between normal queue and Dead-Letter Queue for queues
                    try
                    {
                        if (selectedEntity is not QueueEntity)
                        {
                            status = cmd.Kind == CommandKind.Dlq
                                ? "DLQ is only available for queues."
                                : "Already in normal mode (subscriptions do not support 'queue'/'dlq' toggles).";
                            needsRender = true;
                            continue;
                        }
                        var targetMode = cmd.Kind == CommandKind.Dlq ? MessageMode.DeadLetter : MessageMode.Normal;
                        if (_messageMode == targetMode)
                        {
                            needsRender = true;
                            continue;
                        }
                        _messageMode = targetMode;
                        pageHistory.Clear();
                        nextFromSequence = 1;
                        _msgPage = 1;
                        if (receiver != null) { await receiver.CloseAsync(); receiver = null; }
                        (messageClient, receiver) = await EnsureReceiverAsync(selectedNs!, selectedEntity!, _messageMode, messageClient, receiver, ct);
                        messages.Clear();
                        var req = GetMessagePageRequestRows(view, selectedNs, selectedEntity);
                        messages.AddRange(await FetchMessagesPageAsync(receiver!, nextFromSequence, req, ct));
                    }
                    catch (Exception ex) when (IsUnauthorized(ex))
                    {
                        view = View.Entities;
                        status = "Unauthorized: require Azure Service Bus Data Receiver (Listen) on entity.";
                    }
                    catch (Exception ex)
                    {
                        status = $"Switch failed: {ex.Message}";
                    }
                    needsRender = true;
                    continue;
                }
                needsRender = true;
                continue;
            }
            // Ctrl word-nav/edit
            if (key.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                if (key.Key == ConsoleKey.LeftArrow) { editor.CtrlWordLeft(); RedrawPrompt(editor); continue; }
                if (key.Key == ConsoleKey.RightArrow) { editor.CtrlWordRight(); RedrawPrompt(editor); continue; }
                if (key.Key == ConsoleKey.Backspace) { historyNav.TransitionToDraftFromHistory(editor.Buffer.ToString()); editor.CtrlWordBackspace(); RedrawPrompt(editor); continue; }
                if (key.Key == ConsoleKey.Delete) { historyNav.TransitionToDraftFromHistory(editor.Buffer.ToString()); editor.CtrlWordDelete(); RedrawPrompt(editor); continue; }
            }
            // Arrow/Home/End
            if (key.Key == ConsoleKey.LeftArrow) { editor.Left(); RedrawPrompt(editor); continue; }
            if (key.Key == ConsoleKey.RightArrow) { editor.Right(); RedrawPrompt(editor); continue; }
            if (key.Key == ConsoleKey.Home) { editor.Home(); RedrawPrompt(editor); continue; }
            if (key.Key == ConsoleKey.End) { editor.End(); RedrawPrompt(editor); continue; }
            // History navigation
            if (key.Key == ConsoleKey.UpArrow)
            {
                var newText = historyNav.Up(editor.Buffer.ToString());
                if (!ReferenceEquals(newText, null)) { editor.SetInitial(newText); }
                RedrawPrompt(editor);
                continue;
            }
            if (key.Key == ConsoleKey.DownArrow)
            {
                var newText = historyNav.Down(editor.Buffer.ToString());
                if (!ReferenceEquals(newText, null)) { editor.SetInitial(newText); }
                RedrawPrompt(editor);
                continue;
            }
            // Default insert
            if (!char.IsControl(key.KeyChar)) { historyNav.TransitionToDraftFromHistory(editor.Buffer.ToString()); editor.Insert(key.KeyChar); RedrawPrompt(editor); continue; }
        }

        if (receiver != null) await receiver.CloseAsync();
        if (messageClient != null) await messageClient.DisposeAsync();
    }

    private int _nsPage;
    private int _entPage;

    private static (int start, int count, int page, int totalPages, int pageSize) Page(int page, int total)
    {
        var height = Console.WindowHeight;
        var reserved = 6; // title (1) + table header (3) + footer (2)
        var pageSize = Math.Max(1, height - reserved);
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        page = Math.Clamp(page, 0, totalPages - 1);
        var start = page * pageSize;
        var count = Math.Max(0, Math.Min(pageSize, total - start));
        return (start, count, page, totalPages, pageSize);
    }

    private static (int start, int count, int page, int totalPages, int pageSize) PageWithReserved(int page, int total, int extraReserved)
    {
        var height = Console.WindowHeight;
        var reserved = 6 + Math.Max(0, extraReserved); // add context lines
        var pageSize = Math.Max(1, height - reserved);
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        page = Math.Clamp(page, 0, totalPages - 1);
        var start = page * pageSize;
        var count = Math.Max(0, Math.Min(pageSize, total - start));
        return (start, count, page, totalPages, pageSize);
    }

    private async Task<(ServiceBusClient client, ServiceBusReceiver? receiver)> EnsureReceiverAsync(SBNamespace ns, SBEntityId entity, MessageMode mode, ServiceBusClient? client, ServiceBusReceiver? rcv, CancellationToken ct)
    {
        client ??= new ServiceBusClient(ns.FullyQualifiedNamespace, _credential);
        ServiceBusReceiver? receiver = rcv;
        if (entity is QueueEntity q)
        {
            if (receiver == null || receiver.EntityPath != q.Path)
            {
                if (mode == MessageMode.DeadLetter)
                {
                    var opts = new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter };
                    receiver = client.CreateReceiver(q.QueueName, opts);
                }
                else
                {
                    receiver = client.CreateReceiver(q.QueueName);
                }
            }
        }
        else if (entity is SubscriptionEntity s)
        {
            if (receiver == null || receiver.EntityPath != s.Path)
            {
                receiver = client.CreateReceiver(s.TopicName, s.SubscriptionName);
            }
        }
        return (client, receiver);
    }

    private int GetMessagePageRequestRows(View view, SBNamespace? ns, SBEntityId? entity)
    {
        int width = Console.WindowWidth;
        int ctx = RenderContextLinesForMeasure(view, ns, entity, width);
        // Reserve: title (1) + context (ctx) + header dashes+row (3) + footer (2) + safety (1)
        return Math.Max(1, Console.WindowHeight - (6 + 1) - ctx);
    }

    private int RenderContextLinesForMeasure(View view, SBNamespace? ns, SBEntityId? entity, int width)
    {
        // We constrain context to a single line in this UI
        return (view == View.Entities && ns != null) || (view == View.Messages && ns != null && entity != null) ? 1 : 0;
    }

    private async Task<List<MessageRow>> FetchMessagesPageAsync(ServiceBusReceiver rcv, long fromSeq, int requested, CancellationToken ct)
    {
        long start = Math.Max(1, fromSeq);
        var msgs = await rcv.PeekMessagesAsync(requested, fromSequenceNumber: start, cancellationToken: ct);
        var list = new List<MessageRow>(msgs.Count);
        foreach (var m in msgs)
        {
            var body = TryPreview(m.Body);
            list.Add(new MessageRow(
                m.SequenceNumber,
                m.EnqueuedTime,
                m.MessageId,
                m.Subject,
                m.SessionId,
                m.ContentType,
                body,
                m.Body,
                new Dictionary<string, object>(m.ApplicationProperties)
            ));
        }
        return list;
    }

    private static string TryPreview(BinaryData body)
    {
        try
        {
            var s = body.ToString();
            s = s.Replace('\n', ' ').Replace('\r', ' ');
            s = System.Text.RegularExpressions.Regex.Replace(s, "\\s+", " ").Trim();
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

    private void Render(View view, List<SBNamespace> namespaces, SBNamespace? selectedNs, IReadOnlyList<EntityRow> entities, SBEntityId? selectedEntity, IReadOnlyList<MessageRow> messages, string? status, string inputView)
    {
        Console.Clear();
        var title = view switch
        {
            View.Namespaces => "Service Bus: Select Namespace",
            View.Entities => "Select Queue/Subscription",
            View.Messages => "Messages",
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
        else if (view == View.Messages)
        {
            int pageSize = Math.Max(1, Console.WindowHeight - 6);
            int current = Math.Max(1, _msgPage);
            if (_activeMessageCount.HasValue)
            {
                int totalPages = Math.Max(1, (int)Math.Ceiling(_activeMessageCount.Value / (double)pageSize));
                pageStr = $"PAGE {current}/{totalPages}";
            }
            else
            {
                pageStr = $"PAGE {current}";
            }
        }
        var width = Console.WindowWidth;
        Console.WriteLine(TextTruncation.Truncate(title.PadRight(Math.Max(0, width - pageStr.Length)) + pageStr, width));
        // Render colored single-line context under the title (fits width)
        int ctxLines = RenderContextLines(view, selectedNs, selectedEntity, width);

        if (view == View.Namespaces)
        {
            var (start, count, _, _, _) = PageWithReserved(_nsPage, namespaces.Count, ctxLines);
            RenderNamespacesTable(namespaces, start, count);
        }
        else if (view == View.Entities)
        {
            var (start, count, _, _, _) = PageWithReserved(_entPage, entities.Count, ctxLines);
            RenderEntitiesTable(entities, start, count);
        }
        else if (view == View.Messages)
        {
            RenderMessagesTable(messages);
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
            string hint = view switch
            {
                View.Namespaces => "PageUp/PageDown, ESC. Type 'open <n>' to select namespace.",
                View.Entities => "PageUp/PageDown, ESC. Commands: 'queue <n>' or 'dlq <n>'.",
                View.Messages => "PageUp/PageDown, ESC. Commands: 'open <seq>', 'dlq', 'queue'; history Up/Down.",
                _ => "PageUp/PageDown, ESC."
            };
            Console.WriteLine(TextTruncation.Truncate(hint, Console.WindowWidth));
        }
        Console.SetCursorPosition(0, Console.WindowHeight - 1);
        var prompt = "Command (h for help)> ";
        var width2 = Console.WindowWidth;
        var contentWidth = Math.Max(1, width2 - prompt.Length);
        var line = prompt + TextTruncation.Truncate(inputView, contentWidth).PadRight(contentWidth);
        Console.Write(TextTruncation.Truncate(line, width2).PadRight(width2));
    }

    private int RenderContextLines(View view, SBNamespace? ns, SBEntityId? entity, int width)
    {
        var segments = new List<(string label, string value)>();
        if (view == View.Entities && ns != null)
            segments.Add(("Namespace:", ns.Name));
        else if (view == View.Messages && ns != null && entity != null)
        {
            segments.Add(("Namespace:", ns.Name));
            switch (entity)
            {
                case QueueEntity q:
                    segments.Add(("Queue:", q.QueueName));
                    break;
                case SubscriptionEntity s:
                    segments.Add(("Subscription:", $"{s.TopicName}/{s.SubscriptionName}"));
                    break;
            }
        }
        if (segments.Count == 0) return 0;

        int col = 0;
        for (int i = 0; i < segments.Count; i++)
        {
            var (label, value) = segments[i];
            if (label.StartsWith("Queue:") && _messageMode == MessageMode.DeadLetter)
            {
                value = value + " (DLQ)";
            }
            int needed = (col > 0 ? 2 : 0) + label.Length + 1 + value.Length; // gap + label + space + value
            if (needed > Math.Max(1, width - col))
            {
                // Need to truncate the value to fit on a single line
                if (col > 0)
                {
                    Console.Write("  ");
                    col += 2;
                }
                ColorConsole.Write(label + " ", _theme.Control);
                int rem = Math.Max(1, width - (col + label.Length + 1));
                WriteColorized(TextTruncation.Truncate(value, rem), rem);
                Console.WriteLine();
                return 1;
            }
            if (col > 0)
            {
                Console.Write("  ");
                col += 2;
            }
            ColorConsole.Write(label + " ", _theme.Control);
            WriteColorized(value, value.Length);
            col += label.Length + 1 + value.Length;
        }
        Console.WriteLine();
        return 1;
    }

    private void RedrawPrompt(LineEditorEngine editor)
    {
        try
        {
            var width = Console.WindowWidth;
            var prompt = "Command (h for help)> ";
            var contentWidth = Math.Max(1, width - prompt.Length);
            editor.EnsureVisible(contentWidth);
            var view = editor.GetView(contentWidth);
            Console.SetCursorPosition(0, Console.WindowHeight - 1);
            var line = prompt + view.PadRight(contentWidth);
            Console.Write(TextTruncation.Truncate(line, width).PadRight(width));
            PositionPromptCursor(editor);
        }
        catch { }
    }

    private void PositionPromptCursor(LineEditorEngine editor)
    {
        try
        {
            var prompt = "Command (h for help)> ";
            var col = prompt.Length + Math.Max(0, editor.Cursor - editor.ScrollStart);
            var row = Console.WindowHeight - 1;
            Console.SetCursorPosition(Math.Min(col, Math.Max(0, Console.WindowWidth - 1)), row);
        }
        catch { }
    }


    private async System.Threading.Tasks.Task<bool> DetermineSessionEnabledAsync(SBNamespace ns, SBEntityId entity, System.Threading.CancellationToken ct)
    {
        var admin = new Azure.Messaging.ServiceBus.Administration.ServiceBusAdministrationClient(ns.FullyQualifiedNamespace, _credential);
        try
        {
            if (entity is QueueEntity q)
            {
                var qp = await admin.GetQueueAsync(q.QueueName, ct);
                return qp.Value.RequiresSession;
            }
            else if (entity is SubscriptionEntity s)
            {
                var sp = await admin.GetSubscriptionAsync(s.TopicName, s.SubscriptionName, ct);
                return sp.Value.RequiresSession;
            }
        }
        catch { }
        return false;
    }

    private async System.Threading.Tasks.Task<long?> GetActiveMessageCountAsync(SBNamespace ns, SBEntityId entity, System.Threading.CancellationToken ct)
    {
        var admin = new Azure.Messaging.ServiceBus.Administration.ServiceBusAdministrationClient(ns.FullyQualifiedNamespace, _credential);
        try
        {
            if (entity is QueueEntity q)
            {
                var rp = await admin.GetQueueRuntimePropertiesAsync(q.QueueName, ct);
                return rp.Value.ActiveMessageCount;
            }
            else if (entity is SubscriptionEntity s)
            {
                var rp = await admin.GetSubscriptionRuntimePropertiesAsync(s.TopicName, s.SubscriptionName, ct);
                return rp.Value.ActiveMessageCount;
            }
        }
        catch { }
        return null;
    }

}
