# DigitalBrain.Aspire

Aspire hosting package for the DigitalBrain **Kernel** — the minimal Orleans substrate required for the system to function.

## Layering (structure + distribution)

- **Core** (DigitalBrain.Core): stable abstractions — INeuron, Synapse (with SynapseId/CausationId/CorrelationId), dual journals, Checkpoint/Branch/Restore, IPackBehavior + typed dispatch, UiSurface/RfwCard as first-class synapses, trust, distribution contracts. This is the non-negotiable center. Everything is expressed through neurons and synapses.
- **Kernel** (this package + DigitalBrain.Kernel base): the Aspire-orchestrated minimal Orleans kernel runtime + built-in kernel features (journaled marketplace substrate, collectible-ALC embodiment host, kernel tasks, system status/self-healing via checkpoints, foundry for compile/embody, core orchestration). AddDigitalBrain wires clustering, durable journals (blobs), LLM, etc.
- **Experiences / INO / domain features**: published to the marketplace as signed typed-C# packs. Installed and updated into a running kernel exactly like any other pack. The kernel itself stays the stable base (3 replicas by default enable rolling updates and self-improvement without full downtime).

The kernel starts 3 instances by default (see DigitalBrainOptions.KernelReplicas) so the substrate remains available while packs are embodied, behaviors updated, or resources restarted.

## Usage

```csharp
var ctx = builder.AddDigitalBrain("digitalbrain", options =>
{
    options.LlmModel = "qwen2.5-coder:1.5b";
    options.UseLocalMarketplace = true;
    // KernelReplicas defaults to 3 for HA during updates/self-improvement
});

// Wire the kernel silo using the context — this provides the cool built-in kernel features
// (marketplace, embodiment, journals with causation, UI surfaces, tasks, self-status, 3-replica HA) out of the box.
var kernel = builder.AddProject<Projects.DigitalBrain_Kernel>("kernel");
ctx.WireKernelSilo(kernel);

var clientApp = builder.AddProject<Projects.YourClient>("client")
    .WithReference(ctx.OrleansClient);
```

See NeuroOSPrototype.AppHost/AppHost.cs for the canonical example (Flutter UI client, gateway, MCP wired as pure consumers of the kernel surfaces and synapses).

The returned context + resource enable future With* extensions while keeping the core model pure (UI events and surface updates remain Synapses delivered to neurons).

## Built-in kernel features (always present substrate)
- Dual durable journals + full causation on every Fire/Deliver.
- Checkpoint / Branch / Restore (time-travel and safe simulation inside the kernel).
- Marketplace (journal-driven publish/install of signed typed-C# packs) + collectible ALC embodiment host (GeneratedNeuron) that turns packs into live typed-Synapse handlers.
- KernelTask with journal-derived progress.
- SystemStatus + self-diagnosis (MCP to own Aspire + checkpoint-based simulation).
- HomeFeedBus + RfwCard/UiSurface streaming for dynamic UI that travels with packs.

Higher-level capabilities (INO, specific closed loops, domain experiences, custom UI surfaces) are published to the marketplace and embodied on demand. They receive updates independently of the kernel substrate.

## Distribution model
Just like any other pack, INO and other experiences are published to the marketplace and installed/updated into the already-running kernel. The kernel (3 replicas) provides the stable execution + journal environment. Self-improvement and pack upgrades can proceed while the system stays available.