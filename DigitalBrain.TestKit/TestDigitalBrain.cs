using DigitalBrain.Core;
using Microsoft.Extensions.Configuration;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.TestKit;

public sealed class TestDigitalBrain(
    Action<ISiloBuilder>? extendSilo = null,
    Action<IClientBuilder>? extendClient = null,
    short initialSilosCount = 1) : IDigitalBrain, IAsyncLifetime
{
    private readonly Action<ISiloBuilder>? _extendSilo = extendSilo;
    private readonly Action<IClientBuilder>? _extendClient = extendClient;
    private readonly short _initialSilosCount = initialSilosCount;
    private TestCluster? _cluster;

    public TestCluster Cluster => _cluster!;

    public async Task InitializeAsync()
    {
        var builder = new TestClusterBuilder(initialSilosCount: _initialSilosCount);
        builder.AddSiloBuilderConfigurator<NeuronTestSiloConfigurator>();

        // AddSiloBuilderConfigurator<T>() / AddClientBuilderConfigurator<T>() require parameterless T:
        // Orleans stores T's AssemblyQualifiedName and reflectively Activator.CreateInstance()s it inside
        // the test host process, so a closure-capturing configurator instance can't be passed directly.
        // Bridge the captured delegates through AsyncLocals that the Extend* configurators read when
        // Orleans reflectively constructs them during builder.Build()/DeployAsync() below, on this same
        // async flow.
        if (_extendSilo is not null)
        {
            builder.AddSiloBuilderConfigurator<ExtendSiloConfigurator>();
            ExtendSiloConfigurator.Current.Value = _extendSilo;
        }

        if (_extendClient is not null)
        {
            builder.AddClientBuilderConfigurator<ExtendClientBuilderConfigurator>();
            ExtendClientBuilderConfigurator.Current.Value = _extendClient;
        }

        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async Task DisposeAsync()
    {
        if (_cluster is not null)
            await _cluster.StopAllSilosAsync();
    }

    public TGrain Grain<TGrain>(string key) where TGrain : IGrainWithStringKey =>
        _cluster!.GrainFactory.GetGrain<TGrain>(key);

    public Task FireAsync<T>(T synapse) where T : Synapse =>
        Grain<INeuron>(synapse.SynapseId.ToString()).DeliverAsync(synapse);

    public Task DeliverAsync<T>(T synapse) where T : Synapse =>
        synapse.Receiver is { } r
            ? Grain<INeuron>(r.Value).DeliverAsync(synapse)
            : throw new InvalidOperationException("DeliverAsync requires synapse.Receiver to be set.");

    private sealed class ExtendSiloConfigurator : ISiloConfigurator
    {
        public static readonly AsyncLocal<Action<ISiloBuilder>?> Current = new();

        public void Configure(ISiloBuilder siloBuilder) => Current.Value?.Invoke(siloBuilder);
    }

    private sealed class ExtendClientBuilderConfigurator : IClientBuilderConfigurator
    {
        public static readonly AsyncLocal<Action<IClientBuilder>?> Current = new();

        public void Configure(IConfiguration configuration, IClientBuilder clientBuilder) =>
            Current.Value?.Invoke(clientBuilder);
    }
}
