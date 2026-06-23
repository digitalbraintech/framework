using DigitalBrain.Protocol;

namespace DigitalBrain.Silo.Gateway;

public static class NeuronResolver
{
    public static INeuron Resolve(IGrainFactory grains, string neuronId)
    {
        if (string.IsNullOrWhiteSpace(neuronId))
            throw new ArgumentException("neuronId is required", nameof(neuronId));

        if (neuronId.StartsWith("task-", StringComparison.OrdinalIgnoreCase))
            return grains.GetGrain<IKernelTask>(neuronId);

        return neuronId switch
        {
            "aspire-main" => grains.GetGrain<IAspireNeuron>(neuronId),
            "closedloop-main" => grains.GetGrain<IClosedLoopNeuron>(neuronId),
            "compiler-main" => grains.GetGrain<ICompiler>(neuronId),
            "context-main" => grains.GetGrain<IContextNeuron>(neuronId),
            "chart-main" => grains.GetGrain<IDataVisualizationNeuron>(neuronId),
            "db-main" => grains.GetGrain<IDbSupportNeuron>(neuronId),
            "foundry-main" => grains.GetGrain<ICodeFoundryLoopNeuron>(neuronId),
            "ino-editor-main" => grains.GetGrain<IInoCodeEditor>(neuronId),
            "ino-main" => grains.GetGrain<IInoNeuron>(neuronId),
            "llm-main" => grains.GetGrain<ILlmNeuron>(neuronId),
            "market-main" => grains.GetGrain<IMarketplaceNeuron>(neuronId),
            "status-main" => grains.GetGrain<ISystemStatus>(neuronId),
            _ => grains.GetGrain<IDemoNeuron>(neuronId)
        };
    }
}
