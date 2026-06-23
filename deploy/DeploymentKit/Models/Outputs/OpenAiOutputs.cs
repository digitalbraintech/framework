namespace DeploymentKit.Models.Outputs;

public class OpenAiOutputs
{
    public string Name { get; set; } = string.Empty;
    public Output<string> AccountName { get; set; } = null!;
    public Output<string> Endpoint { get; set; } = null!;
    public Output<string> PrimaryKey { get; set; } = null!;
    public Output<string> ChatDeploymentName { get; set; } = null!;
    public Output<string> ResourceId { get; set; } = null!;
}
