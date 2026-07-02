using DigitalBrain.Core;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Foundry;
using DigitalBrain.Kernel.Gateway;
using DigitalBrain.Runtime.Grpc;
using DigitalBrain.Tests.TestSupport;
using DigitalBrain.TestKit;
using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans.Journaling;

namespace DigitalBrain.Tests.Gateway;

[Collection("signal-sink-host")]
public class GenericSendTests : NeuronTestBase
{
    private readonly HomeFeedBus _homeFeedBus = new();

    protected override void ConfigureSilo(ISiloBuilder builder) => builder
        .AddMemoryGrainStorageAsDefault()
        .AddMemoryStreams("Default")
        .AddMemoryStreams("HomeFeed")
        .AddMemoryStreams("DigitalBrainTimeline")
        .AddMemoryGrainStorage("PubSubStore")
        .ConfigureServices(services =>
        {
            services.AddKeyedScoped<IDurableList<Synapse>>("in-journal", (_, _) => new InMemoryDurableList<Synapse>());
            services.AddKeyedScoped<IDurableList<Synapse>>("out-journal", (_, _) => new InMemoryDurableList<Synapse>());
            services.AddScoped<NeuronJournals>();
            services.AddSingleton<IJournaledStateManager, TestJournaledStateManager>();
            services.AddSingleton<IPackEmbodiment, PackAlcEmbodier>();
            services.AddSingleton(_homeFeedBus);
        });

    private GatewayService NewService() =>
        new(Cluster.GrainFactory, new ConfigurationBuilder().Build(), _homeFeedBus,
            new SignalEgressBus(),
            new FakeHostEnvironment(),
            NullLogger<GatewayService>.Instance);

    [Fact]
    public async Task Send_UnknownTypeName_BroadcastsSignalToSubscribedHandlers()
    {
        var sink = Grain<ISignalSink>("sink-generic-1");
        await sink.ActivateAsync(); // prime the sink so it subscribes to the timeline

        var payload = System.Text.Encoding.UTF8.GetBytes("{\"chatId\":7,\"text\":\"hi\"}");
        await NewService().Send(new SynapseEnvelope
        {
            TypeName = TelegramSignals.MessageReceived,
            CorrelationId = "test-generic-1",
            Payload = global::Google.Protobuf.ByteString.CopyFrom(payload)
        }, TestServerCallContext.Create());

        // Poll until the sink receives the signal (broadcast is async)
        IReadOnlyList<Signal>? received = null;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (!cts.IsCancellationRequested)
        {
            received = await sink.GetReceivedSignalsAsync();
            if (received.Count > 0) break;
            await Task.Delay(50, cts.Token).ContinueWith(_ => { });
        }

        Assert.NotNull(received);
        var signal = Assert.Single(received, s => s.Name == TelegramSignals.MessageReceived);
        Assert.Equal(TelegramSignals.MessageReceived, signal.Name);
        Assert.True(signal.Props.TryGetValue("chatId", out var chatId), "Props should contain 'chatId'");
        Assert.True(chatId is 7 or 7L, $"chatId should be 7 (numeric), was {chatId} ({chatId?.GetType().Name})");
        Assert.Equal("hi", signal.Props["text"]);
    }

    // SECURITY: the generic fallback broadcasts an arbitrary named Signal onto the cluster timeline. It sits on
    // the same external gRPC service a browser reaches, so it is internal-only. An untrusted caller (no internal
    // key, non-Development) must be rejected before any forged egress/reply signal can ride the timeline.
    [Fact]
    public async Task Send_UnknownType_FromUntrustedCaller_InProduction_IsRejected()
    {
        var prodService = new GatewayService(
            Cluster.GrainFactory,
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["DigitalBrain:InternalServiceKey"] = "the-key" })
                .Build(),
            _homeFeedBus, new SignalEgressBus(), new FakeHostEnvironment("Production"),
            NullLogger<GatewayService>.Instance);

        var payload = global::Google.Protobuf.ByteString.CopyFrom(System.Text.Encoding.UTF8.GetBytes("{\"chatId\":7,\"text\":\"hi\"}"));

        // No x-internal-key header → forged egress injection rejected.
        var ex = await Assert.ThrowsAsync<RpcException>(() => prodService.Send(new SynapseEnvelope
        {
            TypeName = TelegramSignals.ReplyRequested,
            CorrelationId = "attacker-1",
            Payload = payload
        }, TestServerCallContext.Create()));
        Assert.Equal(StatusCode.Unauthenticated, ex.StatusCode);

        // Correct internal key → trusted in-cluster transport is admitted.
        var accepted = await prodService.Send(new SynapseEnvelope
        {
            TypeName = TelegramSignals.MessageReceived,
            CorrelationId = "transport-1",
            Payload = payload
        }, TestServerCallContext.WithHeaders(("x-internal-key", "the-key")));
        Assert.NotNull(accepted);
    }
}

[CollectionDefinition("signal-sink-host", DisableParallelization = true)]
public sealed class SignalSinkHostCollection;

public interface ISignalSink : INeuron
{
    Task ActivateAsync();
    Task<IReadOnlyList<Signal>> GetReceivedSignalsAsync();
}

[GrainType("digitalbrain.test.signal-sink")]
public sealed class SignalSinkGrain(
    Microsoft.Extensions.Logging.ILogger<SignalSinkGrain> logger,
    NeuronJournals journals)
    : Neuron(logger, journals), ISignalSink, IHandle<Signal>
{
    private readonly List<Signal> _received = new();

    public Task ActivateAsync() => Task.CompletedTask; // activation handled by OnActivateAsync

    public Task<IReadOnlyList<Signal>> GetReceivedSignalsAsync() =>
        Task.FromResult<IReadOnlyList<Signal>>(_received.ToList());

    public Task HandleAsync(Signal signal)
    {
        _received.Add(signal);
        return Task.CompletedTask;
    }
}
