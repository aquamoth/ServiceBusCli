using System.CommandLine;
using ServiceBusCli.Core;
using System.CommandLine.Invocation;

namespace ServiceBusCli;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        var nsOption = new Option<string?>("--namespace", description: "Service Bus namespace (name or FQDN)");
        var azureSubOption = new Option<string?>("--subscription", description: "Azure subscription ID to scope discovery");
        var queueOption = new Option<string?>("--queue", description: "Queue name");
        var topicOption = new Option<string?>("--topic", description: "Topic name");
        var topicSubOption = new Option<string?>("--topic-subscription", description: "Topic subscription name");
        var authOption = new Option<string>("--auth", () => "auto", "Auth mode: auto|device|browser|cli|vscode");
        var tenantOption = new Option<string?>("--tenant", description: "Entra tenant id");
        var themeOption = new Option<string>("--theme", () => "default", "Theme: default|mono|no-color|solarized");
        var noColor = new Option<bool>("--no-color", description: "Disable color output");

        var root = new RootCommand("Azure Service Bus CLI (preview)")
        {
            nsOption, azureSubOption, queueOption, topicOption, topicSubOption, authOption, tenantOption, themeOption, noColor
        };

        root.SetHandler(async (InvocationContext ctx) =>
        {
            var parse = ctx.ParseResult;
            var ns = parse.GetValueForOption(nsOption);
            var azSub = parse.GetValueForOption(azureSubOption);
            var q = parse.GetValueForOption(queueOption);
            var t = parse.GetValueForOption(topicOption);
            var tSub = parse.GetValueForOption(topicSubOption);
            var auth = parse.GetValueForOption(authOption);
            var tenant = parse.GetValueForOption(tenantOption);
            var theme = parse.GetValueForOption(themeOption);
            var disableColor = parse.GetValueForOption(noColor);

            var themeResolved = ThemePresets.Resolve(theme, disableColor);
            try { Console.Title = "ServiceBusCli"; } catch { /* ignored in some terminals */ }
            Console.WriteLine($"ServiceBusCli — theme: {themeResolved.Name}");
            Console.WriteLine($"Auth: {auth} Tenant: {tenant ?? "(default)"}");
            var cred = CredentialFactory.Create(auth!, tenant);
            var discovery = new ArmServiceBusDiscovery(cred);
            var app = new BrowserApp(cred, discovery, themeResolved, azSub, ns, q, t, tSub);
            await app.RunAsync();
        });

        return await root.InvokeAsync(args);
    }
}
