using DigitalBrain.Protocol;

#pragma warning disable ORLEANSEXP005 // Alpha/experimental journalling APIs

namespace DigitalBrain.Silo;

public class DemoNeuron : Neuron, IDemoNeuron, IHandle<DemoMessageSynapse>
{
    private string _last = string.Empty;

    public DemoNeuron(ILogger<DemoNeuron> logger)
        : base(logger)
    {
    }

    public async Task HandleAsync(DemoMessageSynapse synapse)
    {
        _last = synapse.Text;
        Logger.LogInformation("Demo received via IHandle: {Text}", synapse.Text);
        await FireAsync(new NeuronTelemetry(Self, "message-handled"));
    }

    public Task<string> GetLastMessageAsync() => Task.FromResult(_last);
}

// DemoMessageSynapse moved to Protocol for CLI/shared use