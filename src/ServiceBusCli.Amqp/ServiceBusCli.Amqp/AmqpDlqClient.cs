using System;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Amqp;
using Amqp.Framing;
using Amqp.Types;

namespace ServiceBusCli.Amqp;

public sealed class AmqpDlqClient : IAmqpDlqClient
{
    public async Task<bool> CompleteDlqSessionMessageAsync(
        string fullyQualifiedNamespace,
        string queueName,
        string sessionId,
        long sequenceNumber,
        int maxReceive,
        string? connectionString,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return false;
        var (endpoint, keyName, key) = ParseConnectionString(connectionString!);
        if (string.IsNullOrEmpty(keyName) || string.IsNullOrEmpty(key)) return false;

        string host = NormalizeHost(fullyQualifiedNamespace); // ensure plain FQDN
        string addressPath = $"{queueName}/$DeadLetterQueue";
        string audience = $"sb://{host}/{addressPath}";
        // Use SASL-PLAIN with username = keyName, password = key
        // (CBS is not used in this minimal path; PLAIN suffices for Service Bus)
        var addr = new global::Amqp.Address(host, 5671, keyName, key);
        // AMQP connect attempt (no-op log)

        // Allow a bit more time: network + filtered receive
        var deadline = DateTime.UtcNow.AddSeconds(30);
        var conn = await ConnectWithRetryAsync(addr, deadline, ct).ConfigureAwait(false);
        var session = new global::Amqp.Session(conn);
        // Session opened

        // Create a receiver on DLQ WITHOUT session filter (not supported on sub-queues).
        // Browse messages and match on session-id (group-id) + sequence number; accept only the target.
        var source = new global::Amqp.Framing.Source { Address = addressPath };
        var attach = new global::Amqp.Framing.Attach { Source = source, Target = new global::Amqp.Framing.Target() };
        global::Amqp.ReceiverLink? receiver = null;
        try
        {
            receiver = new global::Amqp.ReceiverLink(session, $"dlq-browse-{Guid.NewGuid():N}", attach, null);
            // Generous credit to reduce round-trips; bounded to avoid flooding
            var credit = Math.Min(500, Math.Max(50, maxReceive * 5));
            receiver.SetCredit(credit, false);

            global::Amqp.Message? targetMsg = null;
            while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
            {
                var remain = deadline - DateTime.UtcNow;
                var wait = remain > TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : remain;
                var msg = await receiver.ReceiveAsync(wait).ConfigureAwait(false);
                if (msg == null)
                {
                    if (DateTime.UtcNow >= deadline) break;
                    continue;
                }

                var sid = TryGetSessionId(msg);
                var seq = TryGetSequenceNumber(msg);
                if (!string.IsNullOrEmpty(sessionId) && !string.Equals(sid, sessionId, StringComparison.Ordinal))
                {
                    receiver.Release(msg);
                    continue;
                }
                if (seq.HasValue && seq.Value == sequenceNumber)
                {
                    targetMsg = msg;
                    break;
                }
                receiver.Release(msg);
            }

            if (targetMsg != null)
            {
                receiver.Accept(targetMsg);
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
        finally
        {
            try { receiver?.Close(); } catch { }
            try { session.Close(); } catch { }
            try { conn.Close(); } catch { }
        }
    }

    private static long? TryGetSequenceNumber(global::Amqp.Message msg)
    {
        try
        {
            if (msg.MessageAnnotations?.Map is global::Amqp.Types.Map m)
            {
                var key = new global::Amqp.Types.Symbol("x-opt-sequence-number");
                if (m.TryGetValue(key, out var v))
                {
                    if (v is long l) return l;
                    if (v is int i) return i;
                    if (v is decimal d) return (long)d;
                    if (long.TryParse(Convert.ToString(v), out var p)) return p;
                }
            }
        }
        catch { }
        return null;
    }

    private static string? TryGetSessionId(global::Amqp.Message msg)
    {
        try
        {
            return msg.Properties?.GroupId;
        }
        catch { return null; }
    }

    private static (string? endpoint, string? keyName, string? key) ParseConnectionString(string cs)
    {
        string? endpoint = null, keyName = null, key = null;
        var parts = cs.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in parts)
        {
            var idx = p.IndexOf('=');
            if (idx <= 0) continue;
            var k = p.Substring(0, idx).Trim();
            var v = p[(idx + 1)..].Trim();
            if (k.Equals("Endpoint", StringComparison.OrdinalIgnoreCase)) endpoint = v;
            else if (k.Equals("SharedAccessKeyName", StringComparison.OrdinalIgnoreCase)) keyName = v;
            else if (k.Equals("SharedAccessKey", StringComparison.OrdinalIgnoreCase)) key = v;
        }
        return (endpoint, keyName, key);
    }

    private static string BuildSasToken(string audience, string keyName, string key, TimeSpan ttl)
    {
        var exp = DateTimeOffset.UtcNow.Add(ttl).ToUnixTimeSeconds();
        var encodedAudience = WebUtility.UrlEncode(audience);
        var toSign = encodedAudience + "\n" + exp;
        using var hmac = new HMACSHA256(Convert.FromBase64String(key));
        var sig = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(toSign)));
        var encodedSig = WebUtility.UrlEncode(sig);
        return $"SharedAccessSignature sr={encodedAudience}&sig={encodedSig}&se={exp}&skn={WebUtility.UrlEncode(keyName)}";
    }

    private static async Task<global::Amqp.Connection> OpenConnectionAsync(global::Amqp.Address address, CancellationToken ct)
    {
        var factory = new global::Amqp.ConnectionFactory();
        var conn = await factory.CreateAsync(address, ct).ConfigureAwait(false);
        return conn;
    }

    private static async Task<global::Amqp.Connection> ConnectWithRetryAsync(global::Amqp.Address addr, DateTime deadline, CancellationToken ct)
    {
        Exception? last = null;
        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            try
            {
                var remain = deadline - DateTime.UtcNow;
                if (remain <= TimeSpan.Zero) break;
                var attempt = remain > TimeSpan.FromSeconds(2) ? TimeSpan.FromSeconds(2) : remain;
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(attempt);
                return await OpenConnectionAsync(addr, cts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                last = ex;
                var remain = deadline - DateTime.UtcNow;
                if (remain <= TimeSpan.Zero) break;
                await Task.Delay(remain > TimeSpan.FromMilliseconds(500) ? 500 : (int)remain.TotalMilliseconds, ct).ConfigureAwait(false);
            }
        }
        throw new TimeoutException($"AMQP connect failed within deadline: {last?.Message}");
    }

    private static string NormalizeHost(string nsHost)
    {
        var v = (nsHost ?? string.Empty).Trim();
        v = v.Replace("https://", string.Empty).Replace("http://", string.Empty).Replace("sb://", string.Empty);
        if (v.EndsWith("/")) v = v[..^1];
        var slash = v.IndexOf('/');
        if (slash >= 0) v = v[..slash];
        var colon = v.IndexOf(':');
        if (colon >= 0) v = v[..colon];
        return v;
    }
}
