using Azure.Core;
using Azure.Identity;

namespace ServiceBusCli.Core;

public static class CredentialFactory
{
    public static TokenCredential Create(string mode, string? tenantId)
    {
        var m = (mode ?? "auto").ToLowerInvariant();
        var tenant = tenantId;
        return m switch
        {
            "device" => new DeviceCodeCredential(new DeviceCodeCredentialOptions { TenantId = tenant }),
            "browser" => new InteractiveBrowserCredential(new InteractiveBrowserCredentialOptions { TenantId = tenant }),
            "cli" => new AzureCliCredential(new AzureCliCredentialOptions { TenantId = tenant }),
            "vscode" => new VisualStudioCodeCredential(new VisualStudioCodeCredentialOptions { TenantId = tenant }),
            _ => new ChainedTokenCredential(
                new DefaultAzureCredential(new DefaultAzureCredentialOptions { VisualStudioCodeTenantId = tenant, SharedTokenCacheTenantId = tenant, AdditionallyAllowedTenants = { "*" } }),
                new AzureCliCredential(new AzureCliCredentialOptions { TenantId = tenant }),
                new DeviceCodeCredential(new DeviceCodeCredentialOptions { TenantId = tenant })
            )
        };
    }
}

