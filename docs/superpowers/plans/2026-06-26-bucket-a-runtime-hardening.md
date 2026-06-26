# Bucket A — Runtime Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Harden the DigitalBrain kernel for a real pilot: secure-by-default pack trust, a read/mutation-split MCP surface, rollback on failed kernel self-update, an explicit N+1 handler-growth proof, and pluggable checkpoint key sourcing.

**Architecture:** Five independent changes inside `brain/`. Each is pure C# verifiable by `dotnet build` + targeted `dotnet test`. No external infrastructure. Reuses existing primitives (`PackSignatureVerifier` ECDSA-nistP256, `AesNeuronStateProtector`, the pack→embody→dispatch chain, the rolling self-update handler).

**Tech Stack:** .NET (net11.0), Orleans 10.2, Aspire 13.4.6, xUnit 2.9, Reqnroll 3.3, ModelContextProtocol 1.4, `Microsoft.Extensions.Configuration`/`.Hosting`, BCL `System.Security.Cryptography`.

## Global Constraints

- Target framework: **net11.0**. Central package versions only (`Directory.Packages.props`); never add `Version="*"` or inline versions.
- **Context7 first:** before writing code against any framework/library API (ModelContextProtocol `WithTools`, Orleans grain/journal, `ConfigurationBinder.GetValue`, `IHostEnvironment`, BCL ECDSA/AES-GCM), look it up via Context7. Already verified for this plan: `WithTools<T>()` composes across calls (stdio registers both tool types; HTTP registers read-only).
- **No vacuous XML docs.** No `/// <summary>` that restates a signature. Small inline comments only where genuinely non-obvious. Self-explanatory names.
- Relative paths only; never reference user-profile paths.
- **Per-task verification ritual:** `dotnet build` → the task's targeted `dotnet test --filter` → on the final task also `aspire doctor` (MCP) + a combined high-severity run.
- Commit only when a task's steps say to. `brain/` is the git repo root for all paths below.
- Build/test working directory is `brain/`. Test project: `DigitalBrain.Tests`.

---

### Task 1: A4 — Explicit N+1 handler-growth proof

Characterization test that makes the marketplace's core guarantee explicit: installing a pack adds exactly one responder to a synapse that previously had none. Mirrors the existing `Full_Install_Embody_RealCompiledCode_Emits_PackEmission` (DigitalBrain.Tests/UnitTest1.cs:187-213). Test-only; should pass against current behavior. If it fails, that is a real regression in the embodiment chain.

**Files:**
- Create: `DigitalBrain.Tests/Distribution/HandlerGrowthTests.cs`

**Interfaces:**
- Consumes: `IMarketplaceNeuron` (`market-*` key), `IGeneratedNeuron` (`generated-<packname-lower>` key), synapses `PublishToMarketplace`, `InstallFromMarketplace`, `ExperienceUsed`, `PackEmission` — all from `DigitalBrain.Core`. Test cluster wired by `NeuronTestSiloConfigurator` (DigitalBrain.Tests/TestSupport).
- Produces: nothing consumed by later tasks.

- [ ] **Step 1: Write the test**

Create `DigitalBrain.Tests/Distribution/HandlerGrowthTests.cs`:

```csharp
using DigitalBrain.Core;
using DigitalBrain.Kernel;
using DigitalBrain.Tests.TestSupport;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.Tests.Distribution;

public class HandlerGrowthTests
{
    [Fact]
    public async Task Installing_A_Pack_Adds_Exactly_One_Responder_To_A_Previously_Unhandled_Synapse()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<NeuronTestSiloConfigurator>();
        var cluster = builder.Build();
        await cluster.DeployAsync();
        try
        {
            const string packCode = """
                public sealed class Echoer : DigitalBrain.Core.IPackBehavior
                {
                    public string Respond(string input) => "echo:" + (input ?? string.Empty);
                }
                """;

            var gen = cluster.GrainFactory.GetGrain<IGeneratedNeuron>("generated-echopackn1");

            // Before install: no embodied behavior, so no PackEmission responders.
            await gen.FireAsync(new ExperienceUsed("EchoPackN1", "before"));
            var before = (await gen.GetTimelineAsync()).OfType<PackEmission>().Count();
            Assert.Equal(0, before);

            var market = cluster.GrainFactory.GetGrain<IMarketplaceNeuron>("market-n1");
            await market.FireAsync(new PublishToMarketplace("EchoPackN1", "1.0", Code: packCode, OwnerId: "tester", IsPrivate: false, CommissionRate: 0.0));
            await market.FireAsync(new InstallFromMarketplace("EchoPackN1", "1.0", BuyerId: "n1-user"));

            // After install: the embodied pack is now exactly one new responder.
            await gen.FireAsync(new ExperienceUsed("EchoPackN1", "after"));
            var after = (await gen.GetTimelineAsync()).OfType<PackEmission>().Count();

            Assert.Equal(before + 1, after);
            var emission = (await gen.GetTimelineAsync()).OfType<PackEmission>().Single();
            Assert.Equal("EchoPackN1", emission.Pack);
            Assert.Equal("echo:after", emission.Output);
        }
        finally
        {
            await cluster.StopAllSilosAsync();
        }
    }
}
```

- [ ] **Step 2: Run the test, expect PASS**

Run: `dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~HandlerGrowthTests"`
Expected: PASS (1 test). A failure here means the embody chain regressed — stop and investigate, do not "fix" the test.

- [ ] **Step 3: Commit**

```bash
git add DigitalBrain.Tests/Distribution/HandlerGrowthTests.cs
git commit -m "test(distribution): explicit N+1 handler-growth proof for pack install"
```

---

### Task 2: A2 — Split MCP tools into read-only vs mutation surfaces

Refactor the single `DigitalBrainTools` partial class into a shared base plus two `[McpServerToolType]` classes. Register both on the stdio transport (local/trusted); register only the read class on the kernel's HTTP transport (remotely reachable). No behavior change inside any tool — methods move verbatim, only `grains.` → `Grains.`.

**Tool classification (final):**
- **Read** → `DigitalBrainReadTools`: `ping_digitalbrain`, `get_timeline`, `list_marketplace`, `get_workbench_surfaces`.
- **Mutation** → `DigitalBrainMutationTools`: `ask_llm_neuron`, `fire_synapse`, `ask_ino`, `ino_code_editor`, `update_context_filter`, `db_example`, `cluster_3d_activity`, `run_closed_loop`, `dart_ui_inspect_and_reload`, `run_code_foundry`, `ingest_company_source`, `invoke_company_skill`, `create_company_skill`, `visualize_data`, `fire_ui_action`, `publish_to_marketplace`, `install_from_marketplace`.

**Files:**
- Create: `DigitalBrain.Mcp.Tools/DigitalBrainToolsBase.cs`
- Create: `DigitalBrain.Mcp.Tools/DigitalBrainReadTools.cs`
- Create: `DigitalBrain.Mcp.Tools/DigitalBrainMutationTools.cs`
- Delete: `DigitalBrain.Mcp.Tools/DigitalBrainTools.cs`, `DigitalBrain.Mcp.Tools/DigitalBrainTools.Neurons.cs`, `DigitalBrain.Mcp.Tools/DigitalBrainTools.Ui.cs`
- Modify: `DigitalBrain.Mcp/Program.cs:33-37`
- Modify: `DigitalBrain.Kernel/Program.cs:46-50`
- Modify: `DigitalBrain.Tests/Mcp/DigitalBrainToolsTests.cs`
- Create: `DigitalBrain.Tests/Mcp/McpTransportSplitTests.cs`

**Interfaces:**
- Produces: `DigitalBrain.Mcp.Tools.DigitalBrainToolsBase(IGrainFactory grains)` with `protected IGrainFactory Grains { get; }` and protected helpers `SurfaceJsonOptions`, `SplitIds`, `GetPublishedPacksWithLocalSeedsAsync`, `ReadLatestPublishedPacksAsync`, `PackKey`, `Explain`, `ResolveNeuron`, `ReadObject`, `ReadString`, `ReadElement`. `DigitalBrainReadTools(IGrainFactory) : DigitalBrainToolsBase` and `DigitalBrainMutationTools(IGrainFactory) : DigitalBrainToolsBase`, both `[McpServerToolType]`.
- Consumes: ModelContextProtocol `WithTools<T>()` (composes across calls — verified via Context7).

- [ ] **Step 1: Create the shared base class**

Create `DigitalBrain.Mcp.Tools/DigitalBrainToolsBase.cs` by moving every member currently in `DigitalBrain.Mcp.Tools/DigitalBrainTools.cs` (lines 12-132) into a non-tool base class. Change the type from `[McpServerToolType] public partial class DigitalBrainTools(IGrainFactory grains)` to `public abstract class DigitalBrainToolsBase`, add an explicit constructor and a protected `Grains` property, and make every helper `protected` (they are currently `private`). Replace every `grains.` reference inside the helpers with `Grains.`:

```csharp
using DigitalBrain.Core;
using Orleans;
using System.Text.Json;

namespace DigitalBrain.Mcp.Tools;

// Shared, transport-agnostic helpers for the DigitalBrain MCP tool surfaces. Reached through an in-process
// IGrainFactory when co-hosted in the silo (HTTP) and the Orleans-client IGrainFactory in the stdio server.
// No fabricated fallbacks: real responses or honest errors only.
public abstract class DigitalBrainToolsBase(IGrainFactory grains)
{
    protected IGrainFactory Grains { get; } = grains;

    protected static readonly JsonSerializerOptions SurfaceJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    protected static IEnumerable<string> SplitIds(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(id => !string.IsNullOrWhiteSpace(id));

    protected async Task<IReadOnlyList<NeuroPack>> GetPublishedPacksWithLocalSeedsAsync(IMarketplaceNeuron marketplace)
    {
        await marketplace.FireAsync(new ListPublished());
        var published = await ReadLatestPublishedPacksAsync(marketplace);
        var publishedKeys = published.Select(PackKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingLocalPacks = MarketplaceSeeds.LocalUiPacks
            .Where(pack => !publishedKeys.Contains(PackKey(pack)))
            .ToArray();

        if (missingLocalPacks.Length == 0)
        {
            return published;
        }

        foreach (var pack in missingLocalPacks)
        {
            await marketplace.FireAsync(MarketplaceSeeds.ToPublishCommand(pack));
        }

        await marketplace.FireAsync(new ListPublished());
        return await ReadLatestPublishedPacksAsync(marketplace);
    }

    protected static async Task<IReadOnlyList<NeuroPack>> ReadLatestPublishedPacksAsync(IMarketplaceNeuron marketplace)
    {
        var timeline = await marketplace.GetTimelineAsync();
        return timeline.OfType<PublishedList>().LastOrDefault()?.Packs ?? Array.Empty<NeuroPack>();
    }

    protected static string PackKey(NeuroPack pack) => $"{pack.Name}@{pack.Version}";

    protected static string Explain(Exception exception)
    {
        var root = exception.GetBaseException();
        return root.Message == exception.Message
            ? exception.Message
            : $"{exception.Message} ({root.Message})";
    }

    protected INeuron ResolveNeuron(string neuronId)
    {
        if (neuronId.StartsWith("task-", StringComparison.OrdinalIgnoreCase))
        {
            return Grains.GetGrain<INeuron>(neuronId);
        }

        return neuronId switch
        {
            "aspire-main" => Grains.GetGrain<IAspireNeuron>(neuronId),
            "closedloop-main" => Grains.GetGrain<IClosedLoopNeuron>(neuronId),
            "compiler-main" => Grains.GetGrain<ICompiler>(neuronId),
            "context-main" => Grains.GetGrain<IContextNeuron>(neuronId),
            "company-main" => Grains.GetGrain<ICompanyKnowledgeNeuron>(neuronId),
            "company-skill-main" => Grains.GetGrain<ICompanySkillOrchestratorNeuron>(neuronId),
            "chart-main" => Grains.GetGrain<IDataVisualizationNeuron>(neuronId),
            "db-main" => Grains.GetGrain<IDbSupportNeuron>(neuronId),
            "foundry-main" => Grains.GetGrain<ICodeFoundryLoopNeuron>(neuronId),
            "ino-editor-main" => Grains.GetGrain<IInoCodeEditor>(neuronId),
            "ino-main" => Grains.GetGrain<IInoNeuron>(neuronId),
            "llm-main" => Grains.GetGrain<ILlmNeuron>(neuronId),
            "market-main" => Grains.GetGrain<IMarketplaceNeuron>(neuronId),
            "status-main" => Grains.GetGrain<ISystemStatus>(neuronId),
            _ => Grains.GetGrain<IDemoNeuron>(neuronId)
        };
    }

    protected static JsonElement ReadObject(JsonElement element, string propertyName)
    {
        var value = ReadElement(element, propertyName);
        return value.HasValue && value.Value.ValueKind == JsonValueKind.Object ? value.Value : default;
    }

    protected static string? ReadString(JsonElement element, string propertyName)
    {
        var value = ReadElement(element, propertyName);
        if (!value.HasValue)
        {
            return null;
        }

        return value.Value.ValueKind switch
        {
            JsonValueKind.String => value.Value.GetString(),
            JsonValueKind.Number => value.Value.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => null
        };
    }

    protected static JsonElement? ReadElement(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (property.NameEquals(propertyName) ||
                string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        return null;
    }
}
```

- [ ] **Step 2: Create the read-tools class**

Create `DigitalBrain.Mcp.Tools/DigitalBrainReadTools.cs`. Move the four read tool methods **verbatim** from their current locations, changing only `grains.` → `Grains.`:
- `PingDigitalBrain` — from `DigitalBrainTools.Neurons.cs:11-12`
- `GetTimeline` — from `DigitalBrainTools.Neurons.cs:38-47`
- `GetWorkbenchSurfaces` — from `DigitalBrainTools.Ui.cs:11-44`
- `ListMarketplace` — from `DigitalBrainTools.Ui.cs:170-178`

Wrapper:

```csharp
using DigitalBrain.Core;
using ModelContextProtocol.Server;
using Orleans;
using System.ComponentModel;
using System.Text.Json;

namespace DigitalBrain.Mcp.Tools;

// Read-only DigitalBrain MCP tools: observe cluster state without side effects. Safe to expose over the
// kernel's HTTP transport (remotely reachable). Mutation tools live in DigitalBrainMutationTools (stdio-only).
[McpServerToolType]
public sealed class DigitalBrainReadTools(IGrainFactory grains) : DigitalBrainToolsBase(grains)
{
    // ...the four read tool methods moved here verbatim (grains. -> Grains.)...
}
```

- [ ] **Step 3: Create the mutation-tools class**

Create `DigitalBrain.Mcp.Tools/DigitalBrainMutationTools.cs`. Move the remaining tool methods **verbatim** (changing `grains.` → `Grains.`):
- From `DigitalBrainTools.Neurons.cs`: `AskLlmNeuron` (14-26), `FireSynapse` (28-36), `AskIno` (49-51), `InoCodeEditor` (53-59), `UpdateContextFilter` (61-71), `DbExample` (73-80), `Cluster3D` (82-92), `RunClosedLoop` (94-102), `DartUIInspect` (104-108), `RunCodeFoundry` (110-128), `IngestCompanySource` (130-139), `InvokeCompanySkill` (141-155), `CreateCompanySkill` (157-167).
- From `DigitalBrainTools.Ui.cs`: `VisualizeData` (46-67), `FireUiAction` (69-142), `PublishToMarketplace` (144-156), `InstallFromMarketplace` (158-168).

Wrapper:

```csharp
using DigitalBrain.Core;
using ModelContextProtocol.Server;
using Orleans;
using System.ComponentModel;
using System.Text.Json;

namespace DigitalBrain.Mcp.Tools;

// Mutating DigitalBrain MCP tools: fire side-effecting synapses, spend LLM tokens, or change marketplace/cluster
// state. Registered on the stdio transport only (local/trusted); withheld from the kernel's HTTP transport
// pending a remote auth decision.
[McpServerToolType]
public sealed class DigitalBrainMutationTools(IGrainFactory grains) : DigitalBrainToolsBase(grains)
{
    // ...the seventeen mutation tool methods moved here verbatim (grains. -> Grains.)...
}
```

- [ ] **Step 4: Delete the old partial files**

```bash
git rm DigitalBrain.Mcp.Tools/DigitalBrainTools.cs DigitalBrain.Mcp.Tools/DigitalBrainTools.Neurons.cs DigitalBrain.Mcp.Tools/DigitalBrainTools.Ui.cs
```

- [ ] **Step 5: Update stdio registration (both tool sets)**

In `DigitalBrain.Mcp/Program.cs`, replace lines 33-37:

```csharp
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<DigitalBrainReadTools>()
    .WithTools<DigitalBrainMutationTools>();
builder.Services.AddSingleton<DigitalBrainReadTools>();
builder.Services.AddSingleton<DigitalBrainMutationTools>();
```

- [ ] **Step 6: Update kernel HTTP registration (read-only)**

In `DigitalBrain.Kernel/Program.cs`, replace lines 46-50 (keep the explanatory comment above it, update it to say read-only):

```csharp
// Co-host the MCP tool surface in-process. Only read-only tools are exposed over HTTP (remotely reachable);
// mutation tools are stdio-only (local/trusted) pending a remote auth decision.
builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithTools<DigitalBrain.Mcp.Tools.DigitalBrainReadTools>();
builder.Services.AddSingleton<DigitalBrain.Mcp.Tools.DigitalBrainReadTools>();
```

- [ ] **Step 7: Update the existing MCP tools test**

In `DigitalBrain.Tests/Mcp/DigitalBrainToolsTests.cs`: the publish call now lives on `DigitalBrainMutationTools` and list on `DigitalBrainReadTools`. Replace the single `DigitalBrainTools` usage. Find the construction (around line 17-40) and split it:

```csharp
var mutationTools = new DigitalBrainMutationTools(cluster.GrainFactory);
var readTools = new DigitalBrainReadTools(cluster.GrainFactory);

await mutationTools.PublishToMarketplace("McpPack", "1.0", "public class P {}", "mcp-user", false, 0.15);
var listing = await readTools.ListMarketplace();
Assert.Contains("McpPack", listing);
```

(Adjust the surrounding test-cluster setup lines only as needed to reference the two new variables; do not change the assertions' intent.)

- [ ] **Step 8: Write the classification test**

Create `DigitalBrain.Tests/Mcp/McpTransportSplitTests.cs`:

```csharp
using System.Linq;
using System.Reflection;
using DigitalBrain.Mcp.Tools;
using ModelContextProtocol.Server;
using Xunit;

namespace DigitalBrain.Tests.Mcp;

public class McpTransportSplitTests
{
    private static string[] ToolNames<T>() =>
        typeof(T).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Select(m => m.GetCustomAttribute<McpServerToolAttribute>())
            .Where(a => a is not null)
            .Select(a => a!.Name!)
            .ToArray();

    [Fact]
    public void ReadTools_Expose_Only_ReadOnly_Tool_Names()
    {
        var read = ToolNames<DigitalBrainReadTools>();
        Assert.Equal(
            new[] { "get_timeline", "get_workbench_surfaces", "list_marketplace", "ping_digitalbrain" }.OrderBy(n => n),
            read.OrderBy(n => n));
    }

    [Fact]
    public void MutationTool_Names_Never_Leak_Into_The_Read_Surface()
    {
        var read = ToolNames<DigitalBrainReadTools>().ToHashSet();
        var mutation = ToolNames<DigitalBrainMutationTools>();

        Assert.Contains("publish_to_marketplace", mutation);
        Assert.Contains("install_from_marketplace", mutation);
        Assert.Contains("run_code_foundry", mutation);
        Assert.All(mutation, name => Assert.DoesNotContain(name, read));
    }
}
```

- [ ] **Step 9: Build and run the MCP tests**

Run: `dotnet build DigitalBrain.Mcp.Tools && dotnet build DigitalBrain.Kernel && dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~Mcp"`
Expected: build clean; MCP tests PASS (the split test + the updated publish/list test).

- [ ] **Step 10: Commit**

```bash
git add DigitalBrain.Mcp.Tools DigitalBrain.Mcp/Program.cs DigitalBrain.Kernel/Program.cs DigitalBrain.Tests/Mcp
git commit -m "feat(mcp): split read vs mutation tools; HTTP exposes read-only, stdio keeps all"
```

---

### Task 3: A5 — Pluggable checkpoint key provider

Decouple the AES checkpoint key source behind `ICheckpointKeyProvider` so a cloud key source can drop in later, and make a missing key fail-fast in Production (silent PassThrough stays dev-only).

**Files:**
- Create: `DigitalBrain.Core/ICheckpointKeyProvider.cs`
- Create: `DigitalBrain.Kernel/Kernel/CheckpointKeyProviders.cs`
- Modify: `DigitalBrain.Kernel/Kernel/KernelServices.cs` (whole file)
- Modify: `DigitalBrain.Kernel/Program.cs:65`
- Create: `DigitalBrain.Tests/Kernel/CheckpointKeyingTests.cs`

**Interfaces:**
- Produces: `DigitalBrain.Core.ICheckpointKeyProvider { byte[]? GetKey(); }`; `DigitalBrain.Kernel.ConfigCheckpointKeyProvider(IConfiguration)`; `AddKernelSecurity(IServiceCollection, IConfiguration, IHostEnvironment)`.
- Consumes: existing `AesNeuronStateProtector(byte[])`, `PassThroughNeuronStateProtector()`, `CheckpointProtector` (DigitalBrain.Kernel/Kernel).

- [ ] **Step 1: Write the failing test**

Create `DigitalBrain.Tests/Kernel/CheckpointKeyingTests.cs`:

```csharp
using System;
using DigitalBrain.Core;
using DigitalBrain.Kernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace DigitalBrain.Tests.Kernel;

public class CheckpointKeyingTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] pairs)
    {
        var dict = new System.Collections.Generic.Dictionary<string, string?>();
        foreach (var (key, value) in pairs) dict[key] = value;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    private sealed class FakeEnv(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    [Fact]
    public void ConfigProvider_Returns_The_Configured_Key()
    {
        var key = Convert.ToBase64String(new byte[32]);
        var provider = new ConfigCheckpointKeyProvider(Config(("DigitalBrain:Checkpoint:Key", key)));
        Assert.Equal(new byte[32], provider.GetKey());
    }

    [Fact]
    public void With_Key_Registers_Aes_Protector_That_RoundTrips()
    {
        var key = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKernelSecurity(Config(("DigitalBrain:Checkpoint:Key", key)), new FakeEnv("Production"));
        using var sp = services.BuildServiceProvider();

        var protector = sp.GetRequiredService<INeuronStateProtector>();
        var plaintext = new byte[] { 1, 2, 3, 4, 5 };
        Assert.Equal(plaintext, protector.Unprotect(protector.Protect(plaintext)));
        Assert.IsType<AesNeuronStateProtector>(protector);
    }

    [Fact]
    public void Production_Without_Key_Fails_Fast()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        Assert.Throws<InvalidOperationException>(() =>
            services.AddKernelSecurity(Config(), new FakeEnv("Production")));
    }

    [Fact]
    public void Development_Without_Key_Falls_Back_To_PassThrough()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKernelSecurity(Config(), new FakeEnv("Development"));
        using var sp = services.BuildServiceProvider();
        Assert.IsType<PassThroughNeuronStateProtector>(sp.GetRequiredService<INeuronStateProtector>());
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~CheckpointKeyingTests"`
Expected: FAIL to compile — `ConfigCheckpointKeyProvider` and the 3-arg `AddKernelSecurity` do not exist yet.

- [ ] **Step 3: Add the provider interface**

Create `DigitalBrain.Core/ICheckpointKeyProvider.cs`:

```csharp
namespace DigitalBrain.Core;

// Source of the symmetric key for checkpoint encryption. Config-backed today; a Key Vault implementation
// drops in here without touching AddKernelSecurity. Null means "no key available".
public interface ICheckpointKeyProvider
{
    byte[]? GetKey();
}
```

- [ ] **Step 4: Add the config provider**

Create `DigitalBrain.Kernel/Kernel/CheckpointKeyProviders.cs`:

```csharp
using DigitalBrain.Core;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Kernel;

// Reads the AES checkpoint key (base64) from DigitalBrain:Checkpoint:Key (env/appsettings/Key Vault-mapped config).
public sealed class ConfigCheckpointKeyProvider(IConfiguration configuration) : ICheckpointKeyProvider
{
    public byte[]? GetKey()
    {
        var keyBase64 = configuration["DigitalBrain:Checkpoint:Key"];
        return string.IsNullOrWhiteSpace(keyBase64) ? null : Convert.FromBase64String(keyBase64);
    }
}
```

- [ ] **Step 5: Rewrite AddKernelSecurity to use the provider + fail-fast**

Replace the body of `DigitalBrain.Kernel/Kernel/KernelServices.cs`:

```csharp
using DigitalBrain.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Kernel;

public static class KernelServices
{
    // Registers checkpoint encryption. The key comes from ICheckpointKeyProvider (config today, Key Vault later).
    // AES-GCM when a key is present; in Production a missing key fails fast; in dev it falls back to PassThrough
    // with a loud warning so the absence of encryption is never silent.
    public static IServiceCollection AddKernelSecurity(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddSingleton<ICheckpointKeyProvider>(new ConfigCheckpointKeyProvider(configuration));
        var key = new ConfigCheckpointKeyProvider(configuration).GetKey();

        if (key is not null)
        {
            services.AddSingleton<INeuronStateProtector>(new AesNeuronStateProtector(key));
        }
        else if (environment.IsProduction())
        {
            throw new InvalidOperationException(
                "DigitalBrain:Checkpoint:Key is required in Production (checkpoints must be encrypted). " +
                "Supply it from Key Vault via an ICheckpointKeyProvider.");
        }
        else
        {
            services.AddSingleton<INeuronStateProtector>(sp =>
            {
                sp.GetService<ILoggerFactory>()?.CreateLogger("KernelSecurity").LogWarning(
                    "No DigitalBrain:Checkpoint:Key configured — checkpoints are NOT encrypted (PassThrough). " +
                    "Configure a key (Key Vault) before production.");
                return new PassThroughNeuronStateProtector();
            });
        }

        services.AddSingleton<CheckpointProtector>();
        return services;
    }
}
```

- [ ] **Step 6: Update the call site**

In `DigitalBrain.Kernel/Program.cs` line 65, pass the environment:

```csharp
builder.Services.AddKernelSecurity(builder.Configuration, builder.Environment);
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet build DigitalBrain.Kernel && dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~CheckpointKeyingTests"`
Expected: PASS (4 tests).

- [ ] **Step 8: Commit**

```bash
git add DigitalBrain.Core/ICheckpointKeyProvider.cs DigitalBrain.Kernel/Kernel/CheckpointKeyProviders.cs DigitalBrain.Kernel/Kernel/KernelServices.cs DigitalBrain.Kernel/Program.cs DigitalBrain.Tests/Kernel/CheckpointKeyingTests.cs
git commit -m "feat(kernel): pluggable checkpoint key provider; fail-fast on missing key in Production"
```

---

### Task 4: A3 — Rolling self-update rollback/abort

Add a rollback path to the kernel self-update handler: when a replica's verify fails, stop, restore the pre-update checkpoint, emit a `kernel-rolling-rollback` surface, and skip `kernel-rolling-complete`. Failure is injected deterministically via a new field on the command. Verified by a direct neuron unit test (more honest than the existing Reqnroll step, which fakes the surfaces).

**Files:**
- Modify: `DigitalBrain.Kernel/SystemNeurons.cs` (KernelUiSurfaceKinds at 21-28; PerformKernelSelfUpdate at 40; HandleAsync(PerformKernelSelfUpdate) at 106-186)
- Create: `DigitalBrain.Tests/Kernel/RollingUpdateRollbackTests.cs`

**Interfaces:**
- Produces: `KernelUiSurfaceKinds.RollingRollback` = `"kernel-rolling-rollback"`; `PerformKernelSelfUpdate(string Version = "", int FailAtReplica = 0)`.
- Consumes: existing `IAspireNeuron` (`aspire-*` key), `CreateCheckpointAsync`, `RestoreCheckpointAsync`, `UiSurface`, `UiSurfaceKeys`, `UiSurfaceLayouts`.

- [ ] **Step 1: Write the failing test**

Create `DigitalBrain.Tests/Kernel/RollingUpdateRollbackTests.cs`:

```csharp
using System.Linq;
using DigitalBrain.Core;
using DigitalBrain.Kernel;
using DigitalBrain.Tests.TestSupport;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.Tests.Kernel;

public class RollingUpdateRollbackTests
{
    [Fact]
    public async Task Verify_Failure_Rolls_Back_And_Does_Not_Complete()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<NeuronTestSiloConfigurator>();
        var cluster = builder.Build();
        await cluster.DeployAsync();
        try
        {
            var aspire = cluster.GrainFactory.GetGrain<IAspireNeuron>("aspire-rollback");
            await aspire.FireAsync(new PerformKernelSelfUpdate("rollback-test", FailAtReplica: 2));

            var timeline = await aspire.GetTimelineAsync();
            var kinds = timeline.OfType<UiSurface>().Select(s => s.Kind).ToArray();

            Assert.Contains(KernelUiSurfaceKinds.RollingRollback, kinds);
            Assert.DoesNotContain(KernelUiSurfaceKinds.RollingComplete, kinds);
            // Replica 1 drained before the failure at replica 2; replica 3 never started.
            Assert.Contains(timeline.OfType<UiSurface>(),
                s => s.Kind == KernelUiSurfaceKinds.RollingDrain && Equals(s.Props.GetValueOrDefault("replica"), 1));
            Assert.DoesNotContain(timeline.OfType<UiSurface>(),
                s => s.Kind == KernelUiSurfaceKinds.RollingDrain && Equals(s.Props.GetValueOrDefault("replica"), 3));
        }
        finally
        {
            await cluster.StopAllSilosAsync();
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~RollingUpdateRollbackTests"`
Expected: FAIL to compile — `KernelUiSurfaceKinds.RollingRollback` and the `FailAtReplica` parameter do not exist yet.

- [ ] **Step 3: Add the rollback surface kind**

In `DigitalBrain.Kernel/SystemNeurons.cs`, add to `KernelUiSurfaceKinds` (after line 27 `RollingComplete`):

```csharp
    public const string RollingRollback = "kernel-rolling-rollback";
```

- [ ] **Step 4: Add the failure-injection field to the command**

In `DigitalBrain.Kernel/SystemNeurons.cs`, change line 40:

```csharp
// FailAtReplica is a deterministic test/verification seam: 0 = never fail; N = the rollout aborts when replica N fails verify.
[GenerateSerializer]
public record PerformKernelSelfUpdate(string Version = "", int FailAtReplica = 0) : Synapse(nameof(PerformKernelSelfUpdate), DateTimeOffset.UtcNow);
```

- [ ] **Step 5: Implement the rollback branch in the handler**

In `DigitalBrain.Kernel/SystemNeurons.cs`, inside `HandleAsync(PerformKernelSelfUpdate cmd)`, immediately after the verify surface is fired for the replica (after line 162, the closing of the `if (bus is not null)` block that broadcasts the verify card, still inside the `for` loop), insert:

```csharp
            if (cmd.FailAtReplica == replica)
            {
                await RestoreCheckpointAsync(preUpdateCheckpoint);
                var rollbackProps = new Dictionary<string, object?>
                {
                    [UiSurfaceKeys.SurfaceId] = $"{KernelUiSurfaceKinds.RollingRollback}-{replica}",
                    [UiSurfaceKeys.Emitter] = Self.Value,
                    [UiSurfaceKeys.Title] = $"Rollback at Replica {replica}/3",
                    [UiSurfaceKeys.Priority] = 90,
                    [UiSurfaceKeys.Layout] = UiSurfaceLayouts.Panel,
                    ["replica"] = replica,
                    ["phase"] = "rolledback",
                    ["version"] = version,
                    ["checkpointId"] = preUpdateCheckpoint.SynapseId,
                    ["reason"] = "verify-failed"
                };
                await FireAsync(new UiSurface(KernelUiSurfaceKinds.RollingRollback, rollbackProps));
                if (bus is not null)
                {
                    bus.Broadcast(new RfwCard("digitalbrain", "KernelRollingRollbackCard", System.Text.Json.JsonSerializer.Serialize(new { replica, phase = "rolledback", version })));
                }
                return; // Abort: do not process further replicas, do not emit RollingComplete.
            }
```

- [ ] **Step 6: Run the test to verify it passes**

Run: `dotnet build DigitalBrain.Kernel && dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~RollingUpdateRollbackTests"`
Expected: PASS (1 test).

- [ ] **Step 7: Verify the happy path still passes (no regression)**

Run: `dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~NeuronCore"`
Expected: PASS — existing kernel self-update scenario unaffected (`FailAtReplica` defaults to 0).

- [ ] **Step 8: Commit**

```bash
git add DigitalBrain.Kernel/SystemNeurons.cs DigitalBrain.Tests/Kernel/RollingUpdateRollbackTests.cs
git commit -m "feat(kernel): rollback + abort on failed replica during rolling self-update"
```

---

### Task 5: A1 — Secure-by-default pack trust

Flip unsigned-pack rejection to default-ON so production rejects unsigned/tampered packs without configuration, while the system's own trusted artifacts (UI seeds, kernel pack) install because they are signed with a built-in trusted-publisher key. The TestCluster runs in explicit local-dev (permissive) mode so existing scenarios stay green; new tests prove the secure default.

> **Trust-anchor note:** `TrustedPublisher`'s private key is a **local development trust anchor for first-party seeds only** — not a production secret. Real/remote publishers use their own keys via `PackSignatureVerifier`. Production cloud trust keying pairs with the deferred Key Vault work.

**Files:**
- Create: `DigitalBrain.Core/Trust/TrustedPublisher.cs`
- Modify: `DigitalBrain.Core/MarketplaceSeeds.cs` (`ToPublishCommand` at 66-74)
- Modify: `DigitalBrain.Kernel/SystemNeurons.cs` (`RejectUnsignedPacks` getter at 330-331)
- Modify: `DigitalBrain.Tests/TestSupport/NeuronTestSiloConfigurator.cs`
- Modify: `brain/start.cs` (kernel publish at line 197)
- Modify: `DigitalBrain.Kernel/appsettings.Development.json`
- Create: `DigitalBrain.Tests/Trust/TrustedSeedInstallTests.cs`

**Interfaces:**
- Produces: `DigitalBrain.Core.TrustedPublisher` with `string PublicKeyBase64`, `NeuroPack Sign(NeuroPack)`, `PublishToMarketplace SignPublishCommand(PublishToMarketplace)`.
- Consumes: `PackSignatureVerifier` (`GenerateKeyPair`, `Sign`, `CanonicalContent`, `VerifyPack`), `NeuroPack`, `PublishToMarketplace`, `ConfigurationBinder.GetValue<bool>(key, defaultValue)`.

- [ ] **Step 1: Generate the trusted dev keypair**

Run this one-off file-based C# snippet from `brain/` to mint a fixed ECDSA-nistP256 keypair (do NOT reuse the example values; generate fresh):

```bash
cat > /tmp/genkey.cs <<'EOF'
using System.Security.Cryptography;
using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
System.Console.WriteLine("PRIV=" + System.Convert.ToBase64String(ecdsa.ExportPkcs8PrivateKey()));
System.Console.WriteLine("PUB="  + System.Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo()));
EOF
dotnet run /tmp/genkey.cs
```

Copy the printed `PRIV=` and `PUB=` base64 values into Step 2.

- [ ] **Step 2: Add the TrustedPublisher**

Create `DigitalBrain.Core/Trust/TrustedPublisher.cs` (paste the generated keys into the two constants):

```csharp
namespace DigitalBrain.Core;

// Built-in trusted-publisher identity for FIRST-PARTY seeds and the kernel pack only. Lets secure-by-default
// (reject unsigned) ship without breaking the system's own preinstalled packs. NOT a production secret:
// third-party/remote publishers sign with their own keys via PackSignatureVerifier; real cloud trust keying
// is a separate (deferred) Key Vault concern.
public static class TrustedPublisher
{
    private const string PrivateKeyBase64 = "REPLACE_WITH_GENERATED_PRIV";
    public const string PublicKeyBase64 = "REPLACE_WITH_GENERATED_PUB";

    public static NeuroPack Sign(NeuroPack pack) =>
        PackSignatureVerifier.SignPack(pack, PrivateKeyBase64, PublicKeyBase64);

    public static PublishToMarketplace SignPublishCommand(PublishToMarketplace command)
    {
        var content = PackSignatureVerifier.CanonicalContent(
            command.PackName, command.Version, command.Code, PublicKeyBase64);
        return command with
        {
            AuthorPublicKeyBase64 = PublicKeyBase64,
            SignatureBase64 = PackSignatureVerifier.Sign(content, PrivateKeyBase64)
        };
    }
}
```

- [ ] **Step 3: Write the failing test**

Create `DigitalBrain.Tests/Trust/TrustedSeedInstallTests.cs`:

```csharp
using System.Linq;
using DigitalBrain.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using DigitalBrain.Tests.TestSupport;
using Orleans.Hosting;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.Tests.Trust;

public class TrustedSeedInstallTests
{
    private sealed class StrictConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            new NeuronTestSiloConfigurator().Configure(siloBuilder);
            siloBuilder.ConfigureServices(services =>
            {
                var configuration = new ConfigurationBuilder()
                    .AddInMemoryCollection(new System.Collections.Generic.Dictionary<string, string?>
                    {
                        ["DigitalBrain:Marketplace:RejectUnsignedPacks"] = "true"
                    })
                    .Build();
                services.AddSingleton<IConfiguration>(configuration);
            });
        }
    }

    [Fact]
    public void Trusted_Publisher_Signs_Seeds_So_They_Verify()
    {
        var signed = MarketplaceSeeds.ToPublishCommand(MarketplaceSeeds.LocalUiPacks[0]);
        var pack = new NeuroPack(signed.PackName, signed.Version, signed.OwnerId, signed.IsPrivate,
            signed.CommissionRate, signed.Code, signed.Description, signed.AuthorPublicKeyBase64, signed.SignatureBase64, signed.Price);
        Assert.True(PackSignatureVerifier.VerifyPack(pack));
    }

    [Fact]
    public async Task Under_Strict_Default_Signed_Seed_Installs_But_Unsigned_Is_Rejected()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<StrictConfigurator>();
        var cluster = builder.Build();
        await cluster.DeployAsync();
        try
        {
            var market = cluster.GrainFactory.GetGrain<IMarketplaceNeuron>("market-trusted");

            var seed = MarketplaceSeeds.ToPublishCommand(MarketplaceSeeds.LocalUiPacks[0]);
            await market.FireAsync(seed);
            await market.FireAsync(new InstallFromMarketplace(seed.PackName, seed.Version, "buyer"));

            await market.FireAsync(new PublishToMarketplace("UnsignedPack", "1.0", Code: "public class U {}", OwnerId: "stranger"));
            await market.FireAsync(new InstallFromMarketplace("UnsignedPack", "1.0", "buyer"));

            var installed = (await market.GetTimelineAsync()).OfType<NeuroPackInstalled>().Select(i => i.Pack.Name).ToArray();
            Assert.Contains(seed.PackName, installed);
            Assert.DoesNotContain("UnsignedPack", installed);
        }
        finally
        {
            await cluster.StopAllSilosAsync();
        }
    }
}
```

- [ ] **Step 4: Run the test to verify it fails**

Run: `dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~TrustedSeedInstallTests"`
Expected: FAIL — seeds are not yet signed (`Trusted_Publisher_Signs_Seeds...` fails verification) and unsigned rejection is not yet default.

- [ ] **Step 5: Sign seeds at publish-command construction**

In `DigitalBrain.Core/MarketplaceSeeds.cs`, change `ToPublishCommand` (lines 66-74) to sign via `TrustedPublisher`:

```csharp
    public static PublishToMarketplace ToPublishCommand(NeuroPack pack) =>
        TrustedPublisher.SignPublishCommand(new(
            pack.Name,
            pack.Version,
            pack.Code,
            pack.OwnerId,
            pack.IsPrivate,
            pack.CommissionRate,
            pack.Description));
```

- [ ] **Step 6: Make unsigned rejection the secure default**

In `DigitalBrain.Kernel/SystemNeurons.cs`, replace the `RejectUnsignedPacks` getter (lines 330-331). The key-absent default is now `true`; `?? true` covers a fully absent `IConfiguration`:

```csharp
    private bool RejectUnsignedPacks =>
        ServiceProvider.GetService<IConfiguration>()?.GetValue("DigitalBrain:Marketplace:RejectUnsignedPacks", true) ?? true;
```

- [ ] **Step 7: Put the TestCluster in explicit local-dev (permissive) mode**

In `DigitalBrain.Tests/TestSupport/NeuronTestSiloConfigurator.cs`, register a permissive `IConfiguration` inside the existing `ConfigureServices` block (add after line 34 `services.AddSingleton<HomeFeedBus>();`), so existing scenarios that install unsigned local packs keep working:

```csharp
                services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(
                    new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                        .AddInMemoryCollection(new Dictionary<string, string?>
                        {
                            ["DigitalBrain:Marketplace:RejectUnsignedPacks"] = "false"
                        })
                        .Build());
```

- [ ] **Step 8: Sign the kernel pack publish in start.cs**

In `brain/start.cs` line 197, wrap the kernel publish with the trusted signature so kernel self-update installs under the secure default:

```csharp
                await marketGrain.FireAsync(DigitalBrain.Core.TrustedPublisher.SignPublishCommand(
                    new PublishToMarketplace(KernelPack.Name, version, "", "digitalbraintech", false, 0.0, KernelPack.Description)));
```

- [ ] **Step 9: Add the dev escape hatch to Development settings**

In `DigitalBrain.Kernel/appsettings.Development.json`, add the permissive flag under a `DigitalBrain:Marketplace` section (merge into existing JSON; create the nesting if absent):

```json
{
  "DigitalBrain": {
    "Marketplace": {
      "RejectUnsignedPacks": false
    }
  }
}
```

- [ ] **Step 10: Run the new + existing trust/marketplace tests**

Run: `dotnet build DigitalBrain.Core && dotnet build DigitalBrain.Kernel && dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~Trust|FullyQualifiedName~Marketplace|FullyQualifiedName~NeuronCore|FullyQualifiedName~Mcp|FullyQualifiedName~HandlerGrowth"`
Expected: PASS — new strict tests pass; existing unsigned-install scenarios still pass (TestCluster permissive); the N+1 and MCP tests still pass.

- [ ] **Step 11: Commit**

```bash
git add DigitalBrain.Core/Trust/TrustedPublisher.cs DigitalBrain.Core/MarketplaceSeeds.cs DigitalBrain.Kernel/SystemNeurons.cs DigitalBrain.Tests/TestSupport/NeuronTestSiloConfigurator.cs DigitalBrain.Tests/Trust/TrustedSeedInstallTests.cs start.cs DigitalBrain.Kernel/appsettings.Development.json
git commit -m "feat(trust): reject unsigned packs by default; sign first-party seeds + kernel pack"
```

---

### Task 6: Combined verification + docs

Final ritual across the whole bucket, plus a CONTINUITY note.

**Files:**
- Modify: `brain/CONTINUITY.md` (append a session entry)

- [ ] **Step 1: Full high-severity test run**

Run: `dotnet build && dotnet test DigitalBrain.Tests`
Expected: PASS except the 2 known env-flaky `GatewayGrpcWireTests` (pre-existing socket-bind, not a regression — confirm only those two, if any, fail).

- [ ] **Step 2: Aspire doctor**

Use the aspire MCP `doctor` tool (or `aspire doctor`). Expected: all checks pass.

- [ ] **Step 3: Aspire smoke (optional, infra available)**

Run `aspire run` from `brain/`; confirm kernel + resources reach Running in the dashboard; stop. Confirms the secure-default + MCP-split changes don't break boot.

- [ ] **Step 4: Append CONTINUITY note**

Add a dated entry to `brain/CONTINUITY.md` summarizing Bucket A (reject-unsigned default-on + trusted seeds; MCP read/mutation split; rolling rollback; N+1 proof; pluggable checkpoint keying) and the verification performed.

- [ ] **Step 5: Commit**

```bash
git add CONTINUITY.md
git commit -m "docs: record Bucket A runtime hardening completion + verification"
```

---

## Self-Review

**Spec coverage:**
- A1 (reject-unsigned default + signed seeds + escape hatch) → Task 5. ✓
- A2 (MCP read/mutation split) → Task 2. ✓
- A3 (rolling rollback/abort) → Task 4 (direct neuron unit test; deviation from spec's "Reqnroll" noted — chosen because the existing Reqnroll step fakes the surfaces). ✓
- A4 (explicit N+1 proof) → Task 1. ✓
- A5 (pluggable checkpoint keying + fail-fast) → Task 3. ✓
- Verification ritual + Context7 + central packages + naming/comment rules → Global Constraints + Task 6. ✓

**Placeholder scan:** The only intentional placeholders are the two key constants in `TrustedPublisher` (`REPLACE_WITH_GENERATED_*`), filled by Task 5 Step 1's generation command — not a plan gap. The A2 "move verbatim from file:lines" instructions point to exact existing source ranges, not vague descriptions.

**Type consistency:** `Grains` property used consistently across base + both tool classes (Task 2). `ICheckpointKeyProvider.GetKey()`, `ConfigCheckpointKeyProvider`, and 3-arg `AddKernelSecurity(services, configuration, environment)` match between Task 3's interface block, implementation, call site, and tests. `PerformKernelSelfUpdate(string Version, int FailAtReplica)` and `KernelUiSurfaceKinds.RollingRollback` match between Task 4's handler edit and test. `TrustedPublisher.SignPublishCommand`/`Sign`/`PublicKeyBase64` match between Task 5's class, `MarketplaceSeeds`, `start.cs`, and tests.

**Order:** 1 (A4) → 2 (A2) → 3 (A5) → 4 (A3) → 5 (A1) → 6 (verify). A1 lands last because it changes the TestCluster trust mode; earlier tasks' tests run under the current permissive behavior and continue to pass after A1 sets the TestCluster explicitly permissive.
