var builder = DistributedApplication.CreateBuilder(args);

// DigitalBrain setup - SDK (DigitalBrain.Aspire) provides AddDigitalBrain for encapsulation of wiring (see SDK).
// Explicit here for Aspire AppHost compilation compatibility. 3 replicas of the kernel (OS).

var redis = builder.AddRedis("redis");
var orleans = builder.AddOrleans("kernel")
    .WithClustering(redis)
    .WithGrainStorage("Default", redis);

var ollama = builder.AddOllama("ollama")
    .WithGPUSupport()
    .WithDataVolume();
var qwen = ollama.AddModel("qwen", "qwen2.5-coder:1.5b");

var silo = builder.AddProject<Projects.DigitalBrain_Silo>("silo")
    .WithReference(orleans)
    .WithReference(qwen)
    .WithReplicas(3);

var tui = builder.AddProject<Projects.DigitalBrain_Cli>("grok-cli")
    .WithReference(orleans.AsClient());

// Marketplace config for the system
silo.WithEnvironment("DIGITALBRAIN_USE_LOCAL_MARKETPLACE", "true");

builder.Build().Run();

