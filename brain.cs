// brain.cs - thin C# for "dotnet run brain.cs" (like dotnet run start.cs via QuickTest setup).
// Spins local DigitalBrain as Aspire project.
// JUST uses IAspireNeuron (via client or inside) to start.
// Integrations (Telegram bot, Flutter) are packed NeuroPacks from marketplace - NO logic inside this .cs .
// The pack provides the Aspire resource (using extensions like AddFlutterClient).
// Args for packed: dotnet run brain.cs --telegram --flutter

using Aspire.Hosting.DigitalBrain;
using System;
using System.Linq;

var builder = DistributedApplication.CreateBuilder(args);

var ctx = builder.AddDigitalBrain("digitalbrain");

// Service-to-service secret gating the secrets-returning GetPackConfig RPC and the internal-only generic Send.
// Shared (same value) between the kernel and any internal transport; auto-generated when absent. Must be wired
// the same way as AppHost.cs, or an internal transport's pull/forward is denied outside Development.
var internalServiceKey = builder.AddParameter(
    "internal-service-key",
    () => builder.Configuration["Parameters:internal-service-key"] ?? Guid.NewGuid().ToString("N"),
    secret: true);

var kernel = builder.AddProject<Projects.DigitalBrain_Kernel>("kernel");
ctx.WireKernelSilo(kernel);
kernel.WithEnvironment("DigitalBrain__InternalServiceKey", internalServiceKey);

// Packed integrations added via their Aspire extensions from the marketplace pack's SDK.
// No logic here for the bot or Flutter - just declare the resource from the pack.
// Defaults: Windows Flutter client on (for fast local dev thin host). Telegram still opt-in via --telegram (pack model).
bool withTelegram = args.Any(a => a.Contains("telegram", StringComparison.OrdinalIgnoreCase));
bool withFlutter = true; // P0 default for local Windows client on dotnet run brain.cs ; remove flag requirement

if (withTelegram)
{
    // Real transport resource (the echo placeholder is gone). Optional secret token -> no-op without it.
    var telegramBotToken = builder.AddParameter(
        "telegram-bot-token",
        () => builder.Configuration["Parameters:telegram-bot-token"] ?? string.Empty,
        secret: true);
    var telegramTransport = builder.AddProject<Projects.DigitalBrain_Telegram_Transport>("telegram-bot");
    ctx.WireTelegramTransport(telegramTransport, kernel, telegramBotToken, internalServiceKey);
}

// Always default Windows thin client (UI full impl stays marketplace NeuroPack).
var flutterPath = ResolveFlutterAppPath(builder);
if (!string.IsNullOrEmpty(flutterPath))
{
    ctx.AddFlutterClient("flutter-ui", flutterPath, "windows");
}
else
{
    Console.WriteLine("[brain.cs] Flutter path not resolved; default client skipped (set DIGITALBRAIN_FLUTTER_APP_PATH).");
}

static string? ResolveFlutterAppPath(IDistributedApplicationBuilder b)
{
    var flutterPathEnv = Environment.GetEnvironmentVariable("DIGITALBRAIN_FLUTTER_APP_PATH");
    if (!string.IsNullOrWhiteSpace(flutterPathEnv) && System.IO.Directory.Exists(flutterPathEnv))
        return System.IO.Path.GetFullPath(flutterPathEnv);

    var appHostDir = b.AppHostDirectory;
    var candidates = new[]
    {
        System.IO.Path.GetFullPath(System.IO.Path.Combine(appHostDir, "..", "app")),
        System.IO.Path.GetFullPath(System.IO.Path.Combine(appHostDir, "..", "..", "app")),
        System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "app")),
        System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "..", "app")),
    };

    foreach (var c in candidates)
    {
        if (System.IO.Directory.Exists(c) && System.IO.File.Exists(System.IO.Path.Combine(c, "pubspec.yaml")))
            return c;
    }

    var dir = new System.IO.DirectoryInfo(appHostDir);
    for (int i = 0; i < 6 && dir?.Parent != null; i++)
    {
        var candidate = System.IO.Path.Combine(dir.FullName, "app");
        if (System.IO.Directory.Exists(candidate) && System.IO.File.Exists(System.IO.Path.Combine(candidate, "pubspec.yaml")))
            return System.IO.Path.GetFullPath(candidate);
        dir = dir.Parent;
    }
    return null;
}

builder.Build().Run();

// At runtime, IAspireNeuron can be used (e.g. from start or client) to start/orchestrate the project with the packed.