using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using ServiceBusCli.Core;

namespace ServiceBusCli;

public static class EditorLauncher
{
    public static async Task OpenMessageAsync(EditorMessage row, SBEntityId entity, SBNamespace ns, Theme theme, CancellationToken ct)
    {
        string title = entity switch
        {
            QueueEntity q => $"Queue {q.QueueName}",
            SubscriptionEntity s => $"Subscription {s.TopicName}/{s.SubscriptionName}",
            _ => "Entity"
        };

        var sb = new StringBuilder();
        sb.AppendLine($"# Azure Service Bus — {title}");
        sb.AppendLine($"# Namespace: {ns.Name} ({ns.FullyQualifiedNamespace})");
        sb.AppendLine("#");
        sb.AppendLine($"MessageId: {row.MessageId ?? string.Empty}");
        sb.AppendLine($"Subject: {row.Subject ?? string.Empty}");
        sb.AppendLine($"SequenceNumber: {row.SequenceNumber}");
        if (row.Enqueued is DateTimeOffset enq)
            sb.AppendLine($"Enqueued: {enq:u}");
        sb.AppendLine($"ContentType: {row.ContentType ?? string.Empty}");
        var props = row.ApplicationProperties;
        if (props is not null && props.Count > 0)
        {
            sb.AppendLine("ApplicationProperties:");
            foreach (var kvp in props.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine($"  {kvp.Key}: {kvp.Value}");
            }
        }
        sb.AppendLine("---");
        sb.AppendLine(FormatBody(row.Body, row.ContentType));

        // Write temp file
        var ext = GuessExtension(row.ContentType);
        var path = Path.Combine(Path.GetTempPath(), $"sbmsg-{row.SequenceNumber}-{Guid.NewGuid():N}.{ext}");
        await File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8, ct);

        // Launch editor
        var (cmd, args) = ResolveEditor(path);
        var psi = new ProcessStartInfo(cmd, args)
        {
            UseShellExecute = false,
            RedirectStandardInput = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start editor '{cmd}'.");
        await p.WaitForExitAsync(ct);
    }

    private static (string exe, string args) ResolveEditor(string filepath)
    {
        var visual = Environment.GetEnvironmentVariable("VISUAL");
        var editor = Environment.GetEnvironmentVariable("EDITOR");
        var chosen = visual ?? editor;
        if (!string.IsNullOrWhiteSpace(chosen))
        {
            return (chosen!, EscapeArg(filepath));
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return ("notepad", EscapeArg(filepath));
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // -W waits until the app is closed; -t opens in default text editor
            return ("open", $"-W -t {EscapeArg(filepath)}");
        }
        // Linux/Unix: use vi as a safe blocking editor fallback
        return ("vi", EscapeArg(filepath));
    }

    private static string EscapeArg(string path)
    {
        if (path.Contains(' ')) return $"\"{path}\"";
        return path;
    }

    private static string GuessExtension(string? contentType)
    {
        if (string.IsNullOrEmpty(contentType)) return "txt";
        var ct = contentType.ToLowerInvariant();
        if (ct.Contains("json")) return "json";
        if (ct.Contains("xml")) return "xml";
        return "txt";
    }

    public static string FormatBody(BinaryData body, string? contentType)
    {
        // Try JSON first
        try
        {
            using var doc = JsonDocument.Parse(body);
            var opts = new JsonSerializerOptions { WriteIndented = true };
            return JsonSerializer.Serialize(doc.RootElement, opts);
        }
        catch { /* ignore */ }

        // Fallback to UTF-8 string
        try
        {
            return body.ToString();
        }
        catch
        {
            // Hex dump prefix
            var bytes = body.ToArray();
            var len = Math.Min(bytes.Length, 1024);
            return $"<binary {bytes.Length} bytes>\n" + Convert.ToHexString(bytes, 0, len);
        }
    }
}
