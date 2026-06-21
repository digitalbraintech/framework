var builder = DistributedApplication.CreateBuilder(args);

var redis = builder.AddRedis("redis");

var orleans = builder.AddOrleans("neuro")
    .WithClustering(redis)
    .WithGrainStorage("Default", redis);

var ollama = builder.AddOllama("ollama")
    .WithGPUSupport()
    .WithDataVolume();
var qwen = ollama.AddModel("qwen", "qwen2.5-coder:1.5b");

builder.AddProject<Projects.DigitalBrain_Silo>("silo")
    .WithReference(orleans)
    .WithReference(qwen);

builder.AddProject<Projects.DigitalBrain_Cli>("grok-cli")
    .WithReference(orleans.AsClient());

builder.Build().Run();
