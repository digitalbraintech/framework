var builder = DistributedApplication.CreateBuilder(args);

// DigitalBrain Aspire resource project exists (DigitalBrain.Aspire) providing the foundation
// for the fluent AddDigitalBrain / WithLLM / WithTUI / WithMarketplace / AddExperience / replicas API (MVP).
// Current AppHost uses direct wiring for compatibility; the SDK will be enhanced to fully encapsulate.

// Core infrastructure + 3 replicas of the kernel (OS)
var redis = builder.AddRedis("redis");
var orleans = builder.AddOrleans("neuro")
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

