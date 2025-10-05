using System.CommandLine;
using ServiceBusCli.Core;

namespace ServiceBusCli;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        var nsOption = new Option<string?>("--namespace", description: "Service Bus namespace (name or ARM id)");
        var queueOption = new Option<string?>("--queue", description: "Queue name");
        var topicOption = new Option<string?>("--topic", description: "Topic name");
        var subOption = new Option<string?>("--subscription", description: "Subscription name");
        var authOption = new Option<string>("--auth", () => "auto", "Auth mode: auto|device|browser|cli|vscode");
        var tenantOption = new Option<string?>("--tenant", description: "Entra tenant id");
        var themeOption = new Option<string>("--theme", () => "default", "Theme: default|mono|no-color|solarized");
        var noColor = new Option<bool>("--no-color", description: "Disable color output");

        var root = new RootCommand("Azure Service Bus CLI (preview)")
        {
            nsOption, queueOption, topicOption, subOption, authOption, tenantOption, themeOption, noColor
        };

        root.SetHandler(async (ns, q, t, s, auth, tenant, theme, disableColor) =>
        {
            var themeResolved = ThemePresets.Resolve(theme, disableColor);
            try { Console.Title = "ServiceBusCli"; } catch { /* ignored in some terminals */ }
            Console.WriteLine($"ServiceBusCli — theme: {themeResolved.Name}");
            Console.WriteLine($"Auth: {auth} Tenant: {tenant ?? "(default)"}");
            var cred = CredentialFactory.Create(auth, tenant);
            var discovery = new ArmServiceBusDiscovery(cred);
            var selected = await SelectionUi.SelectEntityAsync(discovery, ns, q, t, s);
            if (selected is null)
            {
                Console.WriteLine("No entity selected. Exiting.");
                return;
            }
            Console.WriteLine($"Selected: {selected.DisplayName}");
            Console.WriteLine("Next step: list and page through messages, with bottom command line.");
        }, nsOption, queueOption, topicOption, subOption, authOption, tenantOption, themeOption, noColor);

        return await root.InvokeAsync(args);
    }
}
