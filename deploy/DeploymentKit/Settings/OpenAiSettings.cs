using System.ComponentModel.DataAnnotations;

namespace DeploymentKit.Settings;

public class OpenAiSettings
{
    [Required(ErrorMessage = "OpenAI SKU name is required")]
    public string SkuName { get; set; } = "S0";

    [Required(ErrorMessage = "Chat model name is required")]
    public string ChatModelName { get; set; } = "gpt-4o";

    [Required(ErrorMessage = "Chat model version is required")]
    public string ChatModelVersion { get; set; } = "2024-08-06";

    [Required(ErrorMessage = "Chat deployment name is required")]
    [StringLength(64, MinimumLength = 1)]
    public string ChatDeploymentName { get; set; } = "chat";

    public string DeploymentSkuName { get; set; } = "Standard";

    [Range(1, 1000, ErrorMessage = "Deployment capacity must be between 1 and 1000")]
    public int DeploymentCapacity { get; set; } = 10;

    public bool EnablePublicNetworkAccess { get; set; } = true;

    public bool Enabled { get; set; } = true;
}
