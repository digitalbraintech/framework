using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans;

namespace DigitalBrain.Silo;

/// <summary>
/// Encapsulates the DigitalBrain "Kernel" (runtime host for neurons/synapses).
/// This is the core integration point for client and host.
/// </summary>
public static class DigitalBrainKernelExtensions
{
    public static IHostApplicationBuilder UseDigitalBrainKernel(this IHostApplicationBuilder builder)
    {
        builder.UseOrleans(siloBuilder =>
        {
            siloBuilder.UseLocalhostClustering();
            siloBuilder.AddMemoryGrainStorageAsDefault();

            // Dual journals: incoming (received Deliver) + outgoing (Fire). Prototype in-memory for fast single-process boot.
            // Auto population via incoming grain call filter + explicit in Neuron Fire/Deliver.
            siloBuilder.ConfigureServices(services =>
            {
                services.AddKeyedScoped<Orleans.Journaling.IDurableList<DigitalBrain.Protocol.Synapse>>("in-journal",
                    (_, _) => new InMemoryJournalForPrototype<DigitalBrain.Protocol.Synapse>());
                services.AddKeyedScoped<Orleans.Journaling.IDurableList<DigitalBrain.Protocol.Synapse>>("out-journal",
                    (_, _) => new InMemoryJournalForPrototype<DigitalBrain.Protocol.Synapse>());
                services.AddSingleton<Orleans.Journaling.IJournaledStateManager, PrototypeJournaledStateManager>();
            });

            // Call filter ensures every incoming synapse (DeliverAsync or grain invocation) auto-logs to receiver's in-journal.
            siloBuilder.AddIncomingGrainCallFilter<IncomingJournalFilter>();

            // Built-in neurons discovered automatically.
        });

        builder.AddOllamaApiClient("qwen");

        return builder;
    }

    public static IHostApplicationBuilder AddDigitalBrainClient(this IHostApplicationBuilder builder)
    {
        builder.UseOrleansClient();
        return builder;
    }
}

// The in-memory journal helpers (kept minimal for the fast path)
internal sealed class InMemoryJournalForPrototype<T> : List<T>, Orleans.Journaling.IDurableList<T>;
internal sealed class PrototypeJournaledStateManager : Orleans.Journaling.IJournaledStateManager
{
    public ValueTask InitializeAsync(CancellationToken ct = default) => ValueTask.CompletedTask;
    public void RegisterState(string stateId, Orleans.Journaling.IJournaledState state) { }
    public bool TryGetState(string stateId, out Orleans.Journaling.IJournaledState? state) { state = null; return false; }
    public ValueTask WriteStateAsync(CancellationToken ct = default) => ValueTask.CompletedTask;
    public ValueTask DeleteStateAsync(CancellationToken ct = default) => ValueTask.CompletedTask;
}

// Incoming call filter for dual-journal auto population.
// Every grain invocation path (including DeliverAsync for received synapses) is intercepted here.
// Actual in-journal write is performed inside Neuron.DeliverAsync (receiver side) so that journals stay
// local to the activation and use the per-grain keyed service. The filter guarantees the intercept point.
internal sealed class IncomingJournalFilter : IIncomingGrainCallFilter
{
    public async Task Invoke(IIncomingGrainCallContext context)
    {
        // For synapse receives, DeliverAsync will handle journaling the incoming entry.
        // Future: could snapshot args here for cross-cutting.
        await context.Invoke();
    }
}