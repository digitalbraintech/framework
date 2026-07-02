using DigitalBrain.Core;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.TestKit;

public abstract class NeuronTestBase : IAsyncLifetime
{
    private TestDigitalBrain _brain = null!;

    protected virtual void ConfigureSilo(ISiloBuilder builder) { }
    protected virtual void ConfigureClient(IClientBuilder builder) { }
    protected virtual short InitialSilosCount => 1;

    protected TestCluster Cluster => _brain.Cluster;

    protected TGrain Grain<TGrain>(string key) where TGrain : IGrainWithStringKey => _brain.Grain<TGrain>(key);
    protected Task FireAsync<T>(T synapse) where T : Synapse => _brain.FireAsync(synapse);
    protected Task DeliverAsync<T>(T synapse) where T : Synapse => _brain.DeliverAsync(synapse);

    public Task InitializeAsync()
    {
        _brain = new TestDigitalBrain(ConfigureSilo, ConfigureClient, InitialSilosCount);
        return _brain.InitializeAsync();
    }

    public Task DisposeAsync() => _brain.DisposeAsync();
}
