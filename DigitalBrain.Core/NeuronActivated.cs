namespace DigitalBrain.Core;

[GenerateSerializer]
public record NeuronActivated(NeuronId Neuron) : Synapse(nameof(NeuronActivated), DateTimeOffset.UtcNow);
