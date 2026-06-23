using DeploymentKit.Models.Outputs;
using DeploymentKit.Settings;

namespace DeploymentKit.Interfaces;

/// <summary>
/// Service interface for provisioning an Azure OpenAI (Cognitive Services) account
/// together with a chat model deployment.
/// </summary>
public interface IOpenAiService : IInfrastructureService
{
    new Task<OpenAiOutputs> CreateAsync(InfrastructureSettings settings, Input<string> resourceGroup, CancellationToken cancellationToken = default);
}
