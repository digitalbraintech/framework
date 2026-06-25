namespace DigitalBrain.Core;

[GenerateSerializer]
public record RestartResource(string ResourceName) : Synapse(nameof(RestartResource), DateTimeOffset.UtcNow);
