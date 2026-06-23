using DeploymentKit.Deployer;
using DeploymentKit.Enums;
using DeploymentKit.Interfaces;
using DeploymentKit.Settings;

namespace DigitalBrain.Deploy;

/// <summary>
/// Pulumi program that provisions the Azure infrastructure for DigitalBrain / NeuroOS:
/// Azure Container Apps (silo, gateway, mcp), Azure Storage (Table + Blob), Azure OpenAI,
/// and Key Vault, all in resource group <c>digitalbrain-rg</c> (westeurope).
///
/// Built on RoseXTechnology/DeploymentKit (vendored under deploy/DeploymentKit), extended in this
/// repo with an Azure OpenAI component. Run via <c>pulumi up</c> against this assembly (Task 10);
/// this Task 6 deliverable only has to compile and declare the infrastructure model.
/// </summary>
internal static class Program
{
    private const string SubscriptionId = "08e2e8fa-a9bf-4a1a-be54-56664d2c6cc9";
    private const string Region = "westeurope";
    private const string ResourceGroup = "digitalbrain-rg";
    private const string NamingPrefix = "digitalbrain";

    private const string GhcrRegistry = "ghcr.io/digitalbraintech";
    private const string SiloImage = GhcrRegistry + "/digitalbrain-silo";
    private const string GatewayImage = GhcrRegistry + "/digitalbrain-gateway";
    private const string McpImage = GhcrRegistry + "/digitalbrain-mcp";

    private const string ChatDeploymentName = "chat";

    private static Task<int> Main(string[] args)
    {
        string imageTag = ParseImageTag(args);

        // Declares the full DigitalBrain infrastructure model. Returning this from a Pulumi.Deployment
        // entrypoint (Task 10) provisions the resources and exports the gateway FQDN.
        return Pulumi.Deployment.RunAsync(() => DeclareInfrastructure(imageTag));
    }

    private static IDictionary<string, object?> DeclareInfrastructure(string imageTag)
    {
        OpenAiSettings openAi = new()
        {
            ChatDeploymentName = ChatDeploymentName,
            ChatModelName = "gpt-4o",
            ChatModelVersion = "2024-08-06",
            DeploymentCapacity = 10
        };

        ContainerSettings containers = BuildContainerSettings(imageTag, openAi.ChatDeploymentName);

        IInfrastructureBuilder builder = InfrastructureDeployer.CreateBuilder()
            .SetName(NamingPrefix)
            .SetEnvironment(EnvironmentType.Production)
            .SetLocation(Region)
            .SetSubscriptionId(SubscriptionId)
            .SetResourceGroupName(ResourceGroup)
            .SetNamingPrefix(NamingPrefix)
            .SetValidationMode(ValidationMode.Basic)
            .SkipAzureAuthValidation()
            // Azure Storage: Table clustering + Blob grain/journal persistence (Tasks 1-2).
            .AddStorage()
            .AddTableStorage()
            .AddBlobStorage()
            // Azure OpenAI account + chat model deployment (extension added to DeploymentKit).
            .AddOpenAi(openAi)
            // Key Vault holds the storage connection string and the Azure OpenAI key; Container Apps
            // read them via managed identity (ApplyToContainerApps wires Key Vault refs as env vars).
            .AddKeyVault(new KeyVaultSettings { EnableRbacAuthorization = true, ApplyToContainerApps = true })
            // ACA environment + container apps (gateway external, silo + mcp internal).
            .AddContainerApps(containers);

        InfrastructureSettings settings = builder.Build();

        // NOTE (Task 10): InfrastructureDeployer.DeployAsync(settings) runs the live Pulumi engine to
        // provision everything above. It is intentionally NOT invoked here — Task 6 only declares the
        // model and verifies it compiles. The export below names the output Pulumi will surface.
        return new Dictionary<string, object?>
        {
            ["gatewayFqdn"] = $"https://<gateway-app>.{Region}.azurecontainerapps.io",
            ["environment"] = settings.Environment,
            ["resourceGroup"] = settings.ResourceGroupName,
            ["chatDeployment"] = openAi.ChatDeploymentName,
            ["imageTag"] = imageTag
        };
    }

    private static ContainerSettings BuildContainerSettings(string imageTag, string chatDeployment)
    {
        // DeploymentKit's Container Apps component exposes three app slots. They map to DigitalBrain's
        // three images as: gateway -> external API app, silo -> internal Jobs app, mcp -> internal Bot app.
        // The GHCR image references are pinned by tag; registry/image wiring to GHCR is finalized in Task 10.
        ContainerSettings containers = new()
        {
            UsePlaceholderImages = false,
            ApiImageTag = $"{GatewayImage}:{imageTag}",
            JobsImageTag = $"{SiloImage}:{imageTag}",
            BotImageTag = $"{McpImage}:{imageTag}",
            MinReplicas = 1,
            MaxReplicas = 5,
            // Gateway is the only externally reachable app (external /health, /status).
            IngressSettings = new IngressSettings
            {
                External = true,
                TargetPort = 8080,
                Transport = "Http"
            }
        };

        containers.AdditionalEnvironmentVariables["DIGITALBRAIN_ENV"] = "cloud";
        containers.AdditionalEnvironmentVariables["DigitalBrain__Llm__Provider"] = "azureopenai";
        containers.AdditionalEnvironmentVariables["DigitalBrain__Llm__Model"] = chatDeployment;

        return containers;
    }

    private static string ParseImageTag(string[] args)
    {
        const string flag = "--image-tag";
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == flag && i + 1 < args.Length)
            {
                return args[i + 1];
            }

            if (args[i].StartsWith(flag + "=", StringComparison.Ordinal))
            {
                return args[i][(flag.Length + 1)..];
            }
        }

        return "latest";
    }
}
