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

// Packed integrations added via their Aspire extensions from the marketplace pack's SDK.
// No logic here for the bot or Flutter - just declare the resource from the pack.
// Use args to include (e.g. dotnet run brain.cs --telegram --flutter).
bool withTelegram = args.Any(a => a.Contains("telegram", StringComparison.OrdinalIgnoreCase));
bool withFlutter = args.Any(a => a.Contains("flutter", StringComparison.OrdinalIgnoreCase));

if (withTelegram)
{
    ctx.AddTelegramBot("telegram-bot");
}
if (withFlutter)
{
    var flutterPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(builder.AppHostDirectory, "..", "app"));
    if (System.IO.Directory.Exists(flutterPath))
    {
        ctx.AddFlutterClient("flutter-ui", flutterPath, "windows");
    }
}

builder.Build().Run();

// At runtime, IAspireNeuron can be used (e.g. from start or client) to start/orchestrate the project with the packed.