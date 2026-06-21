using DigitalBrain.Protocol;

// Prototype silo host for DigitalBrain.

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

builder.AddKeyedRedisClient("redis");
builder.AddOllamaApiClient("qwen");

builder.UseOrleans(siloBuilder =>
{
    siloBuilder.UseLocalhostClustering();

    // Centralized prototype journals (single source).
    siloBuilder.ConfigurePrototypeJournals();

    // Grain storage still required for DurableGrain base.
    siloBuilder.AddMemoryGrainStorageAsDefault();
});

var host = builder.Build();

// Bootstrap self-awareness (SystemStatusNeuron will connect MCP + fire Launched on activate)
var grainFactory = host.Services.GetService<IGrainFactory>();
if (grainFactory != null)
{
    var status = grainFactory.GetGrain<ISystemStatus>("status-main");
    _ = status.GetTimelineAsync();
}

host.Run();
