using Aspire.Hosting.DigitalBrain;

var builder = DistributedApplication.CreateBuilder(args);

// brain.cs : thin C# for "dotnet run brain.cs" (setup like dotnet run start.cs).
// Just uses IAspireNeuron to start Aspire project.
// Integrations (Telegram, Flutter) packed as marketplace NeuroPacks - no logic inside brain.cs .
// Pack provides the Aspire bits (see AddFlutterClient).
// Run via the QuickTest setup or equivalent that supports dotnet run brain.cs .

// Unified with fast start.cs path (memory kernel + surfaces) and full distributed here.
// See framework/start.cs for fast "dotnet run" INO + tasks + marketplace + UiSurfaces (Gmail etc).
// Experiences emit UiSurface (AuthButtonSurface etc) for sdk/flutter_demo + Telegram skeleton.
var ctx = builder.AddDigitalBrain("digitalbrain", options =>
{
    options.LlmModel = "qwen2.5-coder:1.5b";
    options.UseLocalMarketplace = true;
})
.WithOrleansDashboard(8080)
.WithMcp();

var silo = builder.AddProject<Projects.DigitalBrain_Silo>("silo");
ctx.WireKernelSilo(silo);  // Provides kernel cool features out of box (marketplace, surfaces, journals, 3 replicas HA, LLM for built-ins) via the Aspire package.

var startUi = builder.AddProject<Projects.DigitalBrain_Cli>("start-ui")
    .WithReference(ctx.OrleansClient)
    .WithExplicitStart();

// Flutter as marketplace pack (DigitalBrain.UI.AspireFlutter).
// The pack provides the Aspire integration. Use the extension from the pack's SDK.
// brain.cs is thin; the pack adds the resource when installed.
var flutterUiPath = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", "app"));
if (Directory.Exists(flutterUiPath))
{
    ctx.AddFlutterClient("flutter-ui", flutterUiPath, "windows");
}

if (ctx.EnableMcp)
{
    // Expose DigitalBrain MCP (stdio tools) as resource so aspire mcp call can discover registered tools: run_closed_loop, ask_ino, publish_to_marketplace, list_marketplace, etc.
    var mcp = builder.AddProject<Projects.DigitalBrain_Mcp>("mcp")
        .WithReference(ctx.OrleansClient)
        .WithReference((IResourceBuilder<IResourceWithConnectionString>)ctx.Llm);
}

// Telegram bot as packed marketplace integration (no logic in core or brain.cs).
// The pack provides it. Use the extension from the pack's integration.
ctx.AddTelegramBot("telegram-bot");

builder.AddProject<Projects.DigitalBrain_Gateway>("gateway")
    .WithReference(ctx.OrleansClient)
    .WithReference(ctx.ClusteringTable)
    .WithExternalHttpEndpoints();

silo.WithEnvironment("DIGITALBRAIN_USE_LOCAL_MARKETPLACE", ctx.UseLocalMarketplace ? "true" : "false");
silo.WithEnvironment("DIGITALBRAIN_SURFACES_ENABLED", "true");

// Inject Ollama LLM config so AddDigitalBrainChat registers IChatClient in the Aspire-hosted silo.
// Cloud path: override DigitalBrain__Llm__Provider=azureopenai via DIGITALBRAIN_ENV or appsettings.
silo.WithEnvironment("DigitalBrain__Llm__Provider", "ollama");
silo.WithEnvironment("DigitalBrain__Llm__Model", ctx.LlmModel);
silo.WithEnvironment("DigitalBrain__Llm__OllamaEndpoint",
    ReferenceExpression.Create($"http://{ctx.OllamaEndpoint.Property(EndpointProperty.Host)}:{ctx.OllamaEndpoint.Property(EndpointProperty.Port)}"));
if (ctx.EnableOrleansDashboard)
{
    silo.WithEnvironment("ORLEANS_DASHBOARD_PORT", (ctx.OrleansDashboardPort ?? 8080).ToString());
}

builder.Build().Run();
