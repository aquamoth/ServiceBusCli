using System;
using System.Threading;
using System.Threading.Tasks;
using Amqp;

namespace ServiceBusCli.Amqp;

public static class AmqpVerifier
{
    public static async Task<(bool ok, string message, string? host, string? policy)> TryVerifySasConnectionAsync(string? connectionString, int timeoutMs = 4000, CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                return (false, "No connection string provided.", null, null);
            string? host = null, policy = null, key = null;
            foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var idx = part.IndexOf('=');
                if (idx <= 0) continue;
                var k = part[..idx].Trim();
                var v = part[(idx + 1)..].Trim();
                if (k.Equals("Endpoint", StringComparison.OrdinalIgnoreCase))
                {
                    var v2 = v.Replace("sb://", string.Empty).Replace("https://", string.Empty).Replace("http://", string.Empty);
                    if (v2.EndsWith("/")) v2 = v2[..^1];
                    host = v2;
                }
                else if (k.Equals("SharedAccessKeyName", StringComparison.OrdinalIgnoreCase)) policy = v;
                else if (k.Equals("SharedAccessKey", StringComparison.OrdinalIgnoreCase)) key = v;
            }
            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(policy) || string.IsNullOrWhiteSpace(key))
                return (false, "Invalid connection string (missing Endpoint/KeyName/Key).", host, policy);

            var addr = new Address(host, 5671, policy, key);
            var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(Math.Max(1000, timeoutMs));
            var factory = new ConnectionFactory();
            var conn = await factory.CreateAsync(addr, cts.Token).ConfigureAwait(false);
            try { conn.Close(); } catch { }
            return (true, $"AMQP SAS connected to {host} as {policy}.", host, policy);
        }
        catch (Exception ex)
        {
            return (false, $"AMQP SAS connect failed: {ex.GetType().Name}: {ex.Message}", null, null);
        }
    }
}
