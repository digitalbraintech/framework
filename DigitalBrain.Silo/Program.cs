using DigitalBrain.Protocol;
using DigitalBrain.Silo.Llm;

// Prototype silo host for DigitalBrain.

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

builder.AddKeyedRedisClient("redis");
builder.Services.AddDigitalBrainChat(builder.Configuration);

builder.UseOrleans(siloBuilder =>
{
    // Use localhost only for direct/fast-path runs; Aspire wires clustering+storage via env/refs (redis) when present.
    var hasRedis = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("REDIS_URI")) ||
                   !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ConnectionStrings__redis"));
    if (!hasRedis)
    {
        siloBuilder.UseLocalhostClustering();
    }

    // Centralized prototype journals (single source).
    siloBuilder.ConfigurePrototypeJournals();
});

var host = builder.Build();

// Bootstrap self-awareness (SystemStatusNeuron will connect MCP + fire Launched on activate)
var grainFactory = host.Services.GetService<IGrainFactory>();
if (grainFactory != null)
{
    var status = grainFactory.GetGrain<ISystemStatus>("status-main");
    _ = status.GetTimelineAsync();
    _ = grainFactory.GetGrain<IInoCodeEditor>("ino-editor-main").GetTimelineAsync();
    _ = grainFactory.GetGrain<IContextNeuron>("context-main").GetTimelineAsync();
    _ = grainFactory.GetGrain<IDbSupportNeuron>("db-main").GetTimelineAsync();
    // Closed loop activation via Mcp or INO using closed loops only (removed direct INeuron to avoid ambiguity; use mcp and closed loops to activate)
}

host.Run();
