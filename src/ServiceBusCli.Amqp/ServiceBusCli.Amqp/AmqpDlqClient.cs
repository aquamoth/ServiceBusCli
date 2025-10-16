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
using Azure.Core;

namespace ServiceBusCli.Amqp;

public sealed class AmqpDlqClient : IAmqpDlqClient
{
    private const string CbsAddress = "$cbs";

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
        // Use AMQP audience for CBS per Service Bus convention
        string audience = $"amqp://{host}/{addressPath}";
        // Use SASL-PLAIN with username = keyName, password = key
        // (CBS is not used in this minimal path; PLAIN suffices for Service Bus)
        var addr = new global::Amqp.Address(host, 5671, keyName, key);
        // AMQP connect attempt (no-op log)

        // Allow a bit more time: network + filtered receive
        var deadline = DateTime.UtcNow.AddSeconds(30);
        var conn = await ConnectWithRetryAsync(addr, deadline, ct).ConfigureAwait(false);
        var session = new global::Amqp.Session(conn);
        // Session opened

        // Create a receiver on DLQ (session filters are NOT supported on sub-queues).
        // Browse messages and match on session-id (group-id) + sequence number; accept only the target.
        var source = new global::Amqp.Framing.Source { Address = addressPath };
        var attach = new global::Amqp.Framing.Attach { Source = source, Target = new global::Amqp.Framing.Target() };
        global::Amqp.ReceiverLink? receiver = null;
        try
        {
            receiver = new global::Amqp.ReceiverLink(session, $"dlq-browse-{Guid.NewGuid():N}", attach, null);
            // Generous credit to reduce round-trips; DLQ without session filtering can require high credit.
            var credit = Math.Min(2000, Math.Max(200, maxReceive * 20));
            receiver.SetCredit(credit, false);

            global::Amqp.Message? targetMsg = null;
            while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
            {
                var remain = deadline - DateTime.UtcNow;
                var wait = remain > TimeSpan.FromMilliseconds(500) ? TimeSpan.FromMilliseconds(500) : remain;
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
            // Offload close handshake to background to avoid blocking the caller.
            _ = Task.Run(() =>
            {
                try { receiver?.Close(); } catch { }
                try { session.Close(); } catch { }
                try { conn.Close(); } catch { }
            });
        }
    }

    internal static long? TryGetSequenceNumber(global::Amqp.Message msg)
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

    internal static string? TryGetSessionId(global::Amqp.Message msg)
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

    internal static async Task<global::Amqp.Connection> ConnectWithRetryAsync(global::Amqp.Address addr, DateTime deadline, CancellationToken ct)
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

    public static string NormalizeHost(string nsHost)
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

public static class AmqpDlqClientAadExtensions
{
    // AAD (JWT via CBS) variant: uses SASL-ANONYMOUS and puts a JWT token on $cbs for the DLQ entity.
    public static async Task<bool> CompleteDlqSessionMessageWithAadAsync(
        this AmqpDlqClient client,
        TokenCredential credential,
        string fullyQualifiedNamespace,
        string queueName,
        string sessionId,
        long sequenceNumber,
        int maxReceive,
        CancellationToken ct,
        bool amqpVerbose = false,
        Action<string>? log = null,
        Action<string>? logError = null)
    {
        string host = NormalizeHost(fullyQualifiedNamespace);
        string addressPath = $"{queueName}/$DeadLetterQueue";
        string audience = $"sb://{host}/{addressPath}";

        var addr = new Address(host, 5671, null, null); // SASL-ANONYMOUS
        var deadline = DateTime.UtcNow.AddSeconds(30);
        var conn = await ConnectWithRetryAsync(addr, deadline, ct).ConfigureAwait(false);
        var session = new Session(conn);

        ReceiverLink? cbsReceiver = null;
        SenderLink? cbsSender = null;
        ReceiverLink? receiver = null;
        try
        {
            // Acquire AAD access token
            var token = await credential.GetTokenAsync(new TokenRequestContext(new[] { "https://servicebus.azure.net/.default" }), ct).ConfigureAwait(false);
            if (amqpVerbose) { log?.Invoke($"CBS init host={host} address={addressPath} audience={audience}"); log?.Invoke($"AAD token acquired; expires={token.ExpiresOn:u}"); }

            // Create CBS links
            cbsReceiver = new ReceiverLink(session, $"cbs-recv-{Guid.NewGuid():N}", "$cbs");
            // Grant credit so the broker can deliver the response
            cbsReceiver.SetCredit(5, false);
            if (amqpVerbose) log?.Invoke("CBS receiver credit set=5");
            cbsSender = new SenderLink(session, $"cbs-send-{Guid.NewGuid():N}", "$cbs");

            // Try CBS put-token with known token types (prefer 'jwt')
            if (!await PutCbsTokenAsync(cbsSender, cbsReceiver, audience, token.Token, "jwt", ct, amqpVerbose, log, logError).ConfigureAwait(false))
            {
                if (!await PutCbsTokenAsync(cbsSender, cbsReceiver, audience, token.Token, "servicebus.windows.net:jwt", ct, amqpVerbose, log, logError).ConfigureAwait(false))
                    return false;
            }

            // Browse DLQ and accept target (no session filters on sub-queues)
            var source = new Source { Address = addressPath };
            if (amqpVerbose && !string.IsNullOrEmpty(sessionId)) log?.Invoke("DLQ sub-queue does not support session links; browsing without filter");
            var attach = new Attach { Source = source, Target = new Target() };
            receiver = new ReceiverLink(session, $"dlq-browse-{Guid.NewGuid():N}", attach, null);
            var credit = Math.Min(2000, Math.Max(200, maxReceive * 20));
            receiver.SetCredit(credit, false);

            var browseDeadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < browseDeadline && !ct.IsCancellationRequested)
            {
                var remain = browseDeadline - DateTime.UtcNow;
                var wait = remain > TimeSpan.FromMilliseconds(500) ? TimeSpan.FromMilliseconds(500) : remain;
                var msg = await receiver.ReceiveAsync(wait).ConfigureAwait(false);
                if (msg == null) continue;
                var sid = TryGetSessionId(msg);
                var seq = TryGetSequenceNumber(msg);
                if (!string.IsNullOrEmpty(sessionId) && !string.Equals(sid, sessionId, StringComparison.Ordinal))
                {
                    receiver.Release(msg);
                    continue;
                }
                if (seq.HasValue && seq.Value == sequenceNumber)
                {
                    receiver.Accept(msg);
                    return true;
                }
                receiver.Release(msg);
            }
            return false;
        }
        catch (Exception ex)
        {
            if (amqpVerbose) logError?.Invoke("CBS/AAD path exception: " + ex.Message);
            return false;
        }
        finally
        {
            _ = Task.Run(() =>
            {
                try { receiver?.Close(); } catch { }
                try { cbsSender?.Close(); } catch { }
                try { cbsReceiver?.Close(); } catch { }
                try { session.Close(); } catch { }
                try { conn.Close(); } catch { }
            });
        }
    }

    private static long? TryGetSequenceNumber(global::Amqp.Message msg)
        => AmqpDlqClient.TryGetSequenceNumber(msg);
    private static string? TryGetSessionId(global::Amqp.Message msg)
        => AmqpDlqClient.TryGetSessionId(msg);
    private static async Task<Connection> ConnectWithRetryAsync(Address addr, DateTime deadline, CancellationToken ct)
        => await ServiceBusCli.Amqp.AmqpDlqClient.ConnectWithRetryAsync(addr, deadline, ct).ConfigureAwait(false);
    private static string NormalizeHost(string h) => ServiceBusCli.Amqp.AmqpDlqClient.NormalizeHost(h);

    private static async Task<bool> PutCbsTokenAsync(SenderLink sender, ReceiverLink receiver, string audience, string token, string tokenType, CancellationToken ct, bool verbose, Action<string>? log, Action<string>? logError)
    {
        var messageId = Guid.NewGuid().ToString();
        var start = DateTime.UtcNow;
        var put = new Message(token)
        {
            Properties = new Properties { MessageId = messageId, ReplyTo = receiver.Name },
            ApplicationProperties = new ApplicationProperties
            {
                ["operation"] = "put-token",
                ["type"] = tokenType,
                ["name"] = audience,
                ["expiration"] = DateTimeOffset.UtcNow.AddMinutes(60).ToUnixTimeSeconds()
            }
        };
        if (verbose) log?.Invoke($"CBS put-token start type={tokenType} audience={audience} messageId={messageId}");
        await sender.SendAsync(put).ConfigureAwait(false);

        var cbsDeadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < cbsDeadline && !ct.IsCancellationRequested)
        {
            var remain = cbsDeadline - DateTime.UtcNow;
            var msg = await receiver.ReceiveAsync(remain > TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : remain).ConfigureAwait(false);
            if (msg == null) continue;
            try
            {
                int status = 0;
                string? statusDesc = null;
                if (msg.ApplicationProperties?.Map is Map map && map.TryGetValue("status-code", out var scObj))
                {
                    if (scObj is int i) status = i; else int.TryParse(Convert.ToString(scObj), out status);
                    if (map.TryGetValue("status-description", out var sd)) statusDesc = Convert.ToString(sd);
                }
                var corr = msg.Properties?.CorrelationId?.ToString();
                if (verbose) log?.Invoke($"CBS response status={status} desc={statusDesc ?? ""} corr={corr} expected={messageId}");
                if ((status == 200 || status == 202) && string.Equals(corr, messageId, StringComparison.Ordinal))
                {
                    receiver.Accept(msg);
                    if (verbose) log?.Invoke($"CBS put-token success elapsed={(DateTime.UtcNow - start).TotalMilliseconds} ms");
                    return true;
                }
                // ignore non-matching or non-200
            }
            finally { }
        }
        if (verbose) logError?.Invoke($"CBS put-token timeout type={tokenType} audience={audience} elapsed={(DateTime.UtcNow - start).TotalMilliseconds} ms");
        return false;
    }
}
