# Group 4 Test Harness Dedup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Migrate the 9 remaining `DigitalBrain.Tests/` files that build ad-hoc `TestClusterBuilder` per test method onto `NeuronTestBase`, deleting the duplicated build/deploy/dispose ceremony, with zero changes to `NeuronTestBase`/`TestDigitalBrain`.

**Architecture:** Files where every fact shares one cluster config migrate directly (`class X : NeuronTestBase`, optional single `ConfigureSilo` override) — identical in shape to Group 3's "mechanical" tier. Files where facts genuinely diverge into two different configs split via a nested test class, following the existing `UnitTest1.cs` precedent (`IsolatedReplayTest`, `StrictConfigNeuronTest`). Two sub-cases of nesting: (a) the nested class only *adds* config on top of the outer class's default (same as the `UnitTest1.cs` precedent — nested class extends the outer class), not needed in this slice since every divergence here is a *replacement*, not an addition; (b) the nested class needs a *different, mutually exclusive* config from its sibling facts — here the nested class extends `NeuronTestBase` **directly** (not the outer class), so it can't accidentally inherit an unwanted `ConfigureSilo` override. A fact that builds no cluster at all today gets a nested **plain** class with no base type, so it keeps its current zero-cluster execution cost.

**Tech Stack:** .NET (net11.0), Orleans 10.2 `Orleans.TestingHost`, xunit.

## Global Constraints

- Full spec: `docs/specs/2026-07-02-group4-test-harness-dedup-design.md`. Read it before starting if anything below is ambiguous.
- Mechanical, behavior-preserving. Same test coverage, same assertions, same outcomes — only the harness plumbing changes.
- **Zero changes to `DigitalBrain.TestKit/NeuronTestBase.cs` or `DigitalBrain.TestKit/TestDigitalBrain.cs`.** Every file in this slice is migratable with the hooks that already exist (`ConfigureSilo`, `ConfigureClient`, `InitialSilosCount`, `Cluster`).
- A `ConfigureSilo` override adds only the **incremental** config on top of what `NeuronTestSiloConfigurator` already applies — do **not** call `new NeuronTestSiloConfigurator().Configure(builder)` again inside an override. `TestDigitalBrain.InitializeAsync` already applies it unconditionally before any subclass override runs (confirmed by `UnitTest1.cs`'s existing `StrictConfigNeuronTest.ConfigureSilo`, which does the same — no re-invocation).
- Relative paths only. NEVER `C:\Users` paths.
- Self-explanatory names; NO vacuous `/// <summary>`. Keep existing inline "why" comments (SECURITY notes, invariant notes) verbatim or lightly adapted if the refactor changes *how* the invariant is expressed (not *whether* it holds) — never delete a comment that documents a real invariant just because the surrounding code moved.
- Verification ritual after **every task**: `dotnet build` then `dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "<targeted>" --no-build --logger "console;verbosity=minimal"`. `aspire doctor` NOT required (no AppHost/hosting files touched).
- Branch: `spec/group4-test-harness-dedup` (already created).
- Delete bias: net LOC should go down (removing `TestClusterBuilder`/`DeployAsync`/try-finally-`StopAllSilosAsync` boilerplate consistently outweighs the few added lines from nested-class wrapper syntax).
- Use `Edit` only after reading the exact current file content first (already done for every file in this plan — the "Before" context is drawn from verified current content; the "Step 1: Replace in full" code blocks below are the target content).
- Fresh implementer + reviewer subagent per task.
- Update `CONTINUITY.md` and `.superpowers/sdd/progress.md` at the end of the round (not per-task).

---

### Task 1: Convert the 4 Tier 1 files (direct migration, zero overrides)

**Files:**
- Modify: `DigitalBrain.Tests/Ui/MarketplaceFilterRoundtripTests.cs`
- Modify: `DigitalBrain.Tests/Kernel/BroadcastReactivityTests.cs`
- Modify: `DigitalBrain.Tests/Distribution/CatalogMaterializationTests.cs`
- Modify: `DigitalBrain.Tests/Distribution/PackBroadcastReactivityTests.cs`

**Interfaces:**
- Consumes: `NeuronTestBase.Grain<T>(key)` (existing, unchanged since Group 3).
- Produces: nothing new — these 4 files are independent of every other task in this plan.

- [ ] **Step 1: Replace `DigitalBrain.Tests/Ui/MarketplaceFilterRoundtripTests.cs` in full**

```csharp
using DigitalBrain.Core;
using DigitalBrain.TestKit;

namespace DigitalBrain.Tests.Ui;

public class MarketplaceFilterRoundtripTests : NeuronTestBase
{
    [Fact]
    public async Task Filtering_by_tier_reemits_a_surface_listing_only_matching_bundles()
    {
        var market = Grain<IMarketplaceNeuron>("market-facet-1");
        // hello-world is a KitExperience → Content tier when materialized at publish.
        // hello-world demo removed (bloat delete).
        await market.FireAsync(new PublishToMarketplace(
            "plain", "1.0.0", Code: "public class P : DigitalBrain.Core.IPackBehavior { public string Respond(string i) => i; }",
            OwnerId: "tester", CommissionRate: 0.0));

        await market.FireAsync(new FilterMarketplace(Tier: "Content"));

        var surface = (await market.GetTimelineAsync())
            .OfType<UiSurface>()
            .Last(s => s.Kind == UiSurfaceKinds.MarketplaceList);
        var items = (System.Collections.Generic.Dictionary<string, object?>[])surface.Props["packs"]!;

        // hello-world demo removed; assert on remaining content if any.
        // Assert.Contains(items, i => i["name"]?.ToString() == "...");
        Assert.DoesNotContain(items, i => i["name"]?.ToString() == "plain");
    }
}
```

- [ ] **Step 2: Replace `DigitalBrain.Tests/Kernel/BroadcastReactivityTests.cs` in full**

```csharp
using DigitalBrain.Core;
using DigitalBrain.Kernel;
using DigitalBrain.TestKit;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Tests.Kernel;

#pragma warning disable ORLEANSEXP005 // Alpha/experimental journalling APIs

[GenerateSerializer]
public record Ping(string Note) : Synapse(nameof(Ping), DateTimeOffset.UtcNow, IsBroadcast: true);

public interface IPingSink : INeuron
{
    Task EnsureActiveAsync();
    Task<int> ReceivedCountAsync();
}

public interface IPingEmitter : INeuron
{
    Task EnsureActiveAsync();
    Task EmitPingAsync(string note);
}

public sealed class PingSink(ILogger<PingSink> logger, NeuronJournals journals) : Neuron(logger, journals), IPingSink, IHandle<Ping>
{
    private int _received;

    public Task EnsureActiveAsync() => Task.CompletedTask;

    public Task<int> ReceivedCountAsync() => Task.FromResult(_received);

    public Task HandleAsync(Ping synapse)
    {
        _received++;
        return Task.CompletedTask;
    }
}

public sealed class PingEmitter(ILogger<PingEmitter> logger, NeuronJournals journals) : Neuron(logger, journals), IPingEmitter
{
    public Task EnsureActiveAsync() => Task.CompletedTask;

    public Task EmitPingAsync(string note) => Broadcast(new Ping(note));
}

public class BroadcastReactivityTests : NeuronTestBase
{
    private static async Task WaitForCountAsync(Func<Task<int>> getCount, int expected)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            if (await getCount() >= expected)
                return;
            await Task.Delay(50);
        }
    }

    [Fact]
    public async Task Broadcast_reaches_every_activated_handler()
    {
        var a = Grain<IPingSink>("a");
        var b = Grain<IPingSink>("b");
        await a.EnsureActiveAsync();
        await b.EnsureActiveAsync();
        var emitter = Grain<IPingEmitter>("e");
        await emitter.EmitPingAsync("hello");
        await WaitForCountAsync(() => a.ReceivedCountAsync(), 1);
        await WaitForCountAsync(() => b.ReceivedCountAsync(), 1);
        Assert.Equal(1, await a.ReceivedCountAsync());
        Assert.Equal(1, await b.ReceivedCountAsync());
    }
}
```

- [ ] **Step 3: Replace `DigitalBrain.Tests/Distribution/CatalogMaterializationTests.cs` in full**

```csharp
using DigitalBrain.Core;
using DigitalBrain.TestKit;

namespace DigitalBrain.Tests.Distribution;

public class CatalogMaterializationTests : NeuronTestBase
{
    [Fact]
    public async Task Publishing_a_kit_bundle_materializes_its_manifest_into_the_catalog()
    {
        var market = Grain<IMarketplaceNeuron>("market-catalog-1");
        // hello-world demo pack removed.
        await market.FireAsync(new ListPublished());

        var listed = (await market.GetTimelineAsync()).OfType<PublishedList>().Last().Packs;
        // hello-world specific asserts removed.
    }
}
```

- [ ] **Step 4: Replace `DigitalBrain.Tests/Distribution/PackBroadcastReactivityTests.cs` in full**

```csharp
using DigitalBrain.Core;
using DigitalBrain.Kernel;
using DigitalBrain.TestKit;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Tests.Distribution;

#pragma warning disable ORLEANSEXP005 // Alpha/experimental journalling APIs

// The real "N+1 reacts to a BROADCAST without restart" proof. An embodied pack whose manifest declares "Ping"
// must react to a broadcast Signal("Ping", ...) — without GeneratedNeuron statically declaring IHandle<Signal>.
// Installing a SECOND pack that also handles "Ping" makes the same broadcast reach N+1 embodied handlers.
public class PackBroadcastReactivityTests : NeuronTestBase
{
    // Pack source compiled by the real embodier. Manifest handles "Ping"; Handle emits one PackEmission per
    // Signal("Ping", ...) and nothing otherwise. References only DigitalBrain.Core. Built by concat so no
    // escaped quotes are needed inside the raw string.
    private static string EchoPingPackSource(string packName)
    {
        const string q = "\"";
        return """
            using System.Collections.Generic;
            using DigitalBrain.Core;

            public sealed class PingEcho : IPackBehavior
            {
                public string Respond(string input) => "echo:" + (input ?? string.Empty);

                public PackManifest GetManifest() =>
                    new PackManifest(new[] { new SynapseType(
            """ + q + "Ping" + q + """
                    ) });

                public IReadOnlyList<Synapse> Handle(Synapse synapse)
                {
                    if (synapse is Signal sig && sig.Name ==
            """ + q + "Ping" + q + """
                    )
                    {
                        return new Synapse[] { new PackEmission(
            """ + q + packName + q + ", synapse.Type, " + q + "pong" + q + """
                        ) };
                    }
                    return System.Array.Empty<Synapse>();
                }
            }
            """;
    }

    public interface IPingBroadcaster : INeuron
    {
        Task EnsureActiveAsync();
        Task EmitPingAsync(string note);
    }

    public sealed class PingBroadcaster(ILogger<PackBroadcastReactivityTests.PingBroadcaster> logger, NeuronJournals journals) : Neuron(logger, journals), IPingBroadcaster
    {
        public Task EnsureActiveAsync() => Task.CompletedTask;

        public Task EmitPingAsync(string note) =>
            Broadcast(new Signal("Ping", new Dictionary<string, object?> { ["note"] = note }));
    }

    private static async Task<int> CountEmissionsAsync(IGeneratedNeuron grain) =>
        (await grain.GetTimelineAsync()).OfType<PackEmission>().Count();

    private static async Task<int> WaitForEmissionDeltaAsync(
        Func<Task<int>> totalEmissions, int baseline, int expectedDelta)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var current = await totalEmissions();
            if (current - baseline >= expectedDelta)
                return current;
            await Task.Delay(50);
        }
        return await totalEmissions();
    }

    [Fact]
    public async Task Embodied_Pack_Reacts_To_Broadcast_And_Adds_One_Responder_Per_Installed_Pack()
    {
        var market = Grain<IMarketplaceNeuron>("market-pack-broadcast");

        const string pack1 = "PingEchoPackOne";
        await market.FireAsync(new PublishToMarketplace(pack1, "1.0", Code: EchoPingPackSource(pack1), OwnerId: "tester", IsPrivate: false, CommissionRate: 0.0));
        await market.FireAsync(new InstallFromMarketplace(pack1, "1.0", BuyerId: "broadcast-user"));

        var gen1 = Grain<IGeneratedNeuron>("generated-" + pack1.ToLowerInvariant());

        // Snapshot AFTER install (install auto-activates the pack once via ExperienceUsed — that is not the broadcast we measure).
        var afterInstall1 = await CountEmissionsAsync(gen1);

        var emitter = Grain<IPingBroadcaster>("ping-broadcaster");
        await emitter.EnsureActiveAsync();
        await emitter.EmitPingAsync("first");

        var afterBroadcast1 = await WaitForEmissionDeltaAsync(() => CountEmissionsAsync(gen1), afterInstall1, 1);
        Assert.Equal(1, afterBroadcast1 - afterInstall1);

        var lastEmission = (await gen1.GetTimelineAsync()).OfType<PackEmission>().Last();
        Assert.Equal(pack1, lastEmission.Pack);
        Assert.Equal("Ping", lastEmission.Input);
        Assert.Equal("pong", lastEmission.Output);

        // Install a SECOND pack also handling "Ping". The SAME broadcast must now reach N+1 embodied handlers.
        const string pack2 = "PingEchoPackTwo";
        await market.FireAsync(new PublishToMarketplace(pack2, "1.0", Code: EchoPingPackSource(pack2), OwnerId: "tester", IsPrivate: false, CommissionRate: 0.0));
        await market.FireAsync(new InstallFromMarketplace(pack2, "1.0", BuyerId: "broadcast-user"));

        var gen2 = Grain<IGeneratedNeuron>("generated-" + pack2.ToLowerInvariant());

        var totalAfterInstall2 = await CountEmissionsAsync(gen1) + await CountEmissionsAsync(gen2);

        await emitter.EmitPingAsync("second");

        Task<int> TotalAcrossBothAsync() =>
            Task.Run(async () => await CountEmissionsAsync(gen1) + await CountEmissionsAsync(gen2));

        var totalAfterBroadcast2 = await WaitForEmissionDeltaAsync(TotalAcrossBothAsync, totalAfterInstall2, 2);
        Assert.Equal(2, totalAfterBroadcast2 - totalAfterInstall2);
    }
}
```

- [ ] **Step 5: Build**

Run: `dotnet build Brain.slnx`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 6: Run targeted tests**

Run: `dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "FullyQualifiedName~MarketplaceFilterRoundtripTests|FullyQualifiedName~BroadcastReactivityTests|FullyQualifiedName~CatalogMaterializationTests|FullyQualifiedName~PackBroadcastReactivityTests" --no-build --logger "console;verbosity=minimal"`
Expected: 4 passed, 0 failed.

- [ ] **Step 7: Commit**

```bash
git add DigitalBrain.Tests/Ui/MarketplaceFilterRoundtripTests.cs DigitalBrain.Tests/Kernel/BroadcastReactivityTests.cs DigitalBrain.Tests/Distribution/CatalogMaterializationTests.cs DigitalBrain.Tests/Distribution/PackBroadcastReactivityTests.cs
git commit -m "test(cleanup): migrate 4 mechanical Group 4 files to NeuronTestBase"
```

---

### Task 2: Convert LlmResponderTests.cs (one ConfigureSilo override)

**Files:**
- Modify: `DigitalBrain.Tests/Kernel/LlmResponderTests.cs`

**Interfaces:**
- Consumes: `NeuronTestBase.ConfigureSilo` override (existing).
- Produces: nothing new for later tasks.

**Correction (found during Task 2 implementation, verified empirically):** `using Orleans.TestingHost;` IS required here, unlike the mechanical Task 1 files. The rule is precise: `Orleans.TestingHost` is needed only by files that explicitly reference the `ISiloConfigurator` **type name** (e.g. declaring `class X : ISiloConfigurator`) — not merely by overriding `ConfigureSilo(ISiloBuilder builder)`, whose `ISiloBuilder` parameter type resolves via Orleans SDK global usings regardless. `LlmResponderTests.cs` keeps `LlmResponderSiloConfigurator : ISiloConfigurator` as a standalone class (so its static wiring stays reusable/testable on its own), so it needs the import. Confirmed by removing the import and rebuilding: `error CS0246: The type or namespace name 'ISiloConfigurator' could not be found`. (`GatewayServiceTests.cs`/`ExperienceStepDispatchTests.cs` from Group 3 don't need it because they inline their config directly into the `ConfigureSilo` override body and never write `ISiloConfigurator` as a type name.) The target code below already includes the corrected import.

- [ ] **Step 1: Replace `DigitalBrain.Tests/Kernel/LlmResponderTests.cs` in full**

```csharp
using DigitalBrain.Core;
using DigitalBrain.Kernel;
using DigitalBrain.TestKit;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;

namespace DigitalBrain.Tests.Kernel;

// Emitter grain that broadcasts AskLlm so the responder can receive it from the timeline.
public interface IAskLlmEmitter : INeuron
{
    Task BroadcastAskAsync(string prompt, string replyType, IReadOnlyDictionary<string, object?> replyProps);
}

public sealed class AskLlmEmitter(Microsoft.Extensions.Logging.ILogger<AskLlmEmitter> logger, NeuronJournals journals) : Neuron(logger, journals), IAskLlmEmitter
{
    public Task BroadcastAskAsync(string prompt, string replyType, IReadOnlyDictionary<string, object?> replyProps) =>
        Broadcast(new AskLlm(prompt, replyType, replyProps));
}

// Deterministic fake: returns "ANSWER:" + prompt, zero external I/O.
internal sealed class AnswerPrefixChatClient : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var prompt = string.Concat(messages.Select(m => m.Text));
        var reply = new ChatMessage(ChatRole.Assistant, "ANSWER:" + prompt);
        return Task.FromResult(new ChatResponse(reply));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Streaming not used.");

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}

// Test-local configurator: registers the fake IChatClient on top of the always-applied base silo wiring;
// kept separate from NeuronTestSiloConfigurator so other NeuronTestBase subclasses that don't apply this
// ConfigureSilo override get no IChatClient (their deterministic fallback).
public sealed class LlmResponderSiloConfigurator : ISiloConfigurator
{
    public void Configure(ISiloBuilder siloBuilder) =>
        siloBuilder.ConfigureServices(services =>
            services.AddSingleton<IChatClient, AnswerPrefixChatClient>());
}

public class LlmResponderTests : NeuronTestBase
{
    protected override void ConfigureSilo(ISiloBuilder builder) =>
        new LlmResponderSiloConfigurator().Configure(builder);

    [Fact]
    public async Task AskLlm_broadcast_triggers_reply_Signal_with_llm_text()
    {
        // Activate responder so it subscribes to the timeline before the ask arrives.
        var responder = Grain<ILlmResponderNeuron>("responder-1");
        await responder.GetTimelineAsync();

        var emitter = Grain<IAskLlmEmitter>("emitter-1");
        var replyProps = new Dictionary<string, object?> { ["chatId"] = 7 };
        await emitter.BroadcastAskAsync("hi", "ReplyX", replyProps);

        // Poll the responder's timeline: stream delivery + grain dispatch cross the silo scheduler,
        // so a fixed delay is flaky under load. Bounded wait keeps the test fast when it lands quickly.
        Signal? signal = null;
        for (var attempt = 0; attempt < 20 && signal is null; attempt++)
        {
            await Task.Delay(50);
            var timeline = await responder.GetTimelineAsync();
            signal = timeline.OfType<Signal>().FirstOrDefault(s => s.Name == "ReplyX");
        }

        Assert.NotNull(signal);
        Assert.Equal("ReplyX", signal.Name);
        Assert.Equal(7, signal.Props["chatId"]);
        Assert.Equal("ANSWER:hi", signal.Props["text"]);
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build Brain.slnx`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Run targeted tests**

Run: `dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "FullyQualifiedName~LlmResponderTests" --no-build --logger "console;verbosity=minimal"`
Expected: 1 passed, 0 failed. (Substring filter does not match `LlmResponderScopedConfigTests` — "LlmResponderTests" is not a contiguous substring of "LlmResponderScopedConfigTests".)

- [ ] **Step 4: Commit**

```bash
git add DigitalBrain.Tests/Kernel/LlmResponderTests.cs
git commit -m "test(cleanup): migrate LlmResponderTests to NeuronTestBase"
```

---

### Task 3: Convert PublishGateTests.cs (first nested-class split — establishes the pattern)

**Files:**
- Modify: `DigitalBrain.Tests/Trust/PublishGateTests.cs`

**Interfaces:**
- Consumes: `NeuronTestBase.ConfigureSilo` override (existing).
- Produces: the "nested class extends `NeuronTestBase` directly, not the outer class" pattern that Task 4 and Task 5 reuse for their own mutually-exclusive-config splits.

**Correction (found during Task 3 implementation, verified empirically):** the nested test class MUST be declared `public`, not `private`. The original design reasoning cited `UnitTest1.cs`'s `IsolatedReplayTest`/`StrictConfigNeuronTest` as precedent for "private nested test classes" — that reading was wrong. Those two classes are `private` but have **no `[Fact]` methods of their own**; they're plain `NeuronTestBase` subclasses manually instantiated inside other facts on the outer class (`new IsolatedReplayTest()` at `UnitTest1.cs:54`, `new StrictConfigNeuronTest()` at `UnitTest1.cs:347`) to get a fresh isolated cluster with different config, not independently-discovered test classes. A nested class that itself carries a `[Fact]` is a genuinely new pattern for this codebase and IS subject to xUnit's own analyzer rule: `error xUnit1000: Test classes must be public`. Verified by building with `private` (fails) and `public` (succeeds, and the 3 facts — 2 outer + 1 nested — are correctly discovered and pass via `dotnet test --filter "FullyQualifiedName~PublishGateTests"`). The target code below already uses `public sealed class DefaultUngatedTests`. This same correction applies to Task 4's `SignatureVerificationTests`/`MarketplaceSeedsPackagingTests` and Task 5's `NullScopedFactoryFallbackTests` — all four nested classes below are `public`, not `private`.

- [ ] **Step 1: Replace `DigitalBrain.Tests/Trust/PublishGateTests.cs` in full**

```csharp
using DigitalBrain.Core;
using DigitalBrain.TestKit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Tests.Trust;

public class PublishGateTests : NeuronTestBase
{
    private const string PackCode =
        "public class P : DigitalBrain.Core.IPackBehavior { public string Respond(string i) => i; }";

    protected override void ConfigureSilo(ISiloBuilder builder) => builder.ConfigureServices(services =>
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DigitalBrain:Marketplace:RejectUnsignedPacks"] = "false",
                ["DigitalBrain:Marketplace:GatePublishing"] = "true"
            })
            .Build();
        services.AddSingleton<IConfiguration>(configuration);
    });

    private static async Task<IReadOnlyList<NeuroPack>> ListedAsync(IMarketplaceNeuron market)
    {
        await market.FireAsync(new ListPublished());
        return (await market.GetTimelineAsync()).OfType<PublishedList>().Last().Packs;
    }

    [Fact]
    public async Task Gate_on_admits_a_trusted_publisher()
    {
        var market = Grain<IMarketplaceNeuron>("market-gate-trusted");
        var signed = TrustedPublisher.SignPublishCommand(
            new PublishToMarketplace("trusted-pack", "1.0.0", Code: PackCode));
        await market.FireAsync(signed);

        Assert.Contains(await ListedAsync(market), p => p.Name == "trusted-pack");
    }

    [Fact]
    public async Task Gate_on_rejects_a_stranger()
    {
        var market = Grain<IMarketplaceNeuron>("market-gate-stranger");
        var (priv, pub) = PackSignatureVerifier.GenerateKeyPair();
        var stranger = PackSignatureVerifier.SignPack(
            new NeuroPack("stranger-pack", "1.0.0", Code: PackCode), priv, pub);
        await market.FireAsync(new PublishToMarketplace(
            stranger.Name, stranger.Version, Code: stranger.Code,
            AuthorPublicKeyBase64: stranger.AuthorPublicKeyBase64, SignatureBase64: stranger.SignatureBase64));

        Assert.DoesNotContain(await ListedAsync(market), p => p.Name == "stranger-pack");
    }

    // Gating is opt-in per-cluster (the outer class's ConfigureSilo enables it) — this nested class extends
    // NeuronTestBase directly, not PublishGateTests, so it gets the plain default silo config instead of
    // inheriting the gated one.
    public sealed class DefaultUngatedTests : NeuronTestBase
    {
        [Fact]
        public async Task Gate_off_by_default_admits_unsigned()
        {
            var market = Grain<IMarketplaceNeuron>("market-gate-off");
            await market.FireAsync(new PublishToMarketplace("unsigned-pack", "1.0.0", Code: PackCode));

            Assert.Contains(await ListedAsync(market), p => p.Name == "unsigned-pack");
        }
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build Brain.slnx`
Expected: `Build succeeded. 0 Error(s)` (confirms the nested class can reach the outer class's private `PackCode`/`ListedAsync` despite extending `NeuronTestBase` directly instead of `PublishGateTests` — nested-class access to enclosing-type private members is lexical, not inheritance-based).

- [ ] **Step 3: Run targeted tests**

Run: `dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "FullyQualifiedName~PublishGateTests" --no-build --logger "console;verbosity=minimal"`
Expected: 3 passed, 0 failed (2 outer + 1 nested — the substring filter matches the nested class too, since its FullyQualifiedName is `...PublishGateTests+DefaultUngatedTests....`).

- [ ] **Step 4: Commit**

```bash
git add DigitalBrain.Tests/Trust/PublishGateTests.cs
git commit -m "test(cleanup): migrate PublishGateTests to NeuronTestBase, split ungated fact into nested class"
```

---

### Task 4: Convert TrustedSeedInstallTests.cs and HandlerGrowthTests.cs (nested plain-class-for-unit-test pattern)

**Files:**
- Modify: `DigitalBrain.Tests/Trust/TrustedSeedInstallTests.cs`
- Modify: `DigitalBrain.Tests/Distribution/HandlerGrowthTests.cs`

**Interfaces:**
- Consumes: `NeuronTestBase.ConfigureSilo` override (existing). Neither file needs the "nested class extends `NeuronTestBase` directly" pattern from Task 3 — both files' cluster-using facts stay on the outer class unchanged; only their no-cluster facts split out.
- Produces: the "nested **plain** class, no base type, for a fact that builds no cluster" pattern — same shape reused by both files in this task.

- [ ] **Step 1: Replace `DigitalBrain.Tests/Trust/TrustedSeedInstallTests.cs` in full**

```csharp
using DigitalBrain.Core;
using DigitalBrain.TestKit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Tests.Trust;

public class TrustedSeedInstallTests : NeuronTestBase
{
    protected override void ConfigureSilo(ISiloBuilder builder) => builder.ConfigureServices(services =>
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DigitalBrain:Marketplace:RejectUnsignedPacks"] = "true"
            })
            .Build();
        services.AddSingleton<IConfiguration>(configuration);
    });

    [Fact]
    public async Task Under_Strict_Default_Signed_Seed_Installs_But_Unsigned_Is_Rejected()
    {
        var market = Grain<IMarketplaceNeuron>("market-trusted");

        var seed = MarketplaceSeeds.ToPublishCommand(MarketplaceSeeds.LocalUiPacks[0]);
        await market.FireAsync(seed);
        await market.FireAsync(new InstallFromMarketplace(seed.PackName, seed.Version, "buyer"));

        await market.FireAsync(new PublishToMarketplace("UnsignedPack", "1.0", Code: "public class U {}", OwnerId: "stranger"));
        await market.FireAsync(new InstallFromMarketplace("UnsignedPack", "1.0", "buyer"));

        var installed = (await market.GetTimelineAsync()).OfType<NeuroPackInstalled>().Select(i => i.Pack.Name).ToArray();
        Assert.Contains(seed.PackName, installed);
        Assert.DoesNotContain("UnsignedPack", installed);
    }

    // Pure unit test — no TestCluster today, and none needed: only exercises MarketplaceSeeds/
    // PackSignatureVerifier statics. A plain nested class (no NeuronTestBase) keeps it at that cost.
    public sealed class SignatureVerificationTests
    {
        [Fact]
        public void Trusted_Publisher_Signs_Seeds_So_They_Verify()
        {
            var signed = MarketplaceSeeds.ToPublishCommand(MarketplaceSeeds.LocalUiPacks[0]);
            var pack = new NeuroPack(signed.PackName, signed.Version, signed.OwnerId, signed.IsPrivate,
                signed.CommissionRate, signed.Code, signed.Description, signed.AuthorPublicKeyBase64, signed.SignatureBase64, signed.Price);
            Assert.True(PackSignatureVerifier.VerifyPack(pack));
        }
    }
}
```

- [ ] **Step 2: Replace `DigitalBrain.Tests/Distribution/HandlerGrowthTests.cs` in full**

```csharp
using DigitalBrain.Core;
using DigitalBrain.TestKit;

namespace DigitalBrain.Tests.Distribution;

public class HandlerGrowthTests : NeuronTestBase
{
    [Fact]
    public async Task Installing_A_Pack_Adds_Exactly_One_Responder_To_A_Previously_Unhandled_Synapse()
    {
        const string packCode = """
            public sealed class Echoer : DigitalBrain.Core.IPackBehavior
            {
                public string Respond(string input) => "echo:" + (input ?? string.Empty);
            }
            """;

        var gen = Grain<IGeneratedNeuron>("generated-echopackn1");

        // Before install: firing the trigger produces no responder.
        await gen.FireAsync(new ExperienceUsed("EchoPackN1", "before"));
        var beforeResponders = (await gen.GetTimelineAsync()).OfType<PackEmission>().Count();
        Assert.Equal(0, beforeResponders);

        var market = Grain<IMarketplaceNeuron>("market-n1");
        await market.FireAsync(new PublishToMarketplace("EchoPackN1", "1.0", Code: packCode, OwnerId: "tester", IsPrivate: false, CommissionRate: 0.0));
        await market.FireAsync(new InstallFromMarketplace("EchoPackN1", "1.0", BuyerId: "n1-user"));

        // Snapshot AFTER install (install auto-activates the pack once — that emission is not what we measure).
        var afterInstall = (await gen.GetTimelineAsync()).OfType<PackEmission>().Count();

        // A single post-install broadcast must reach exactly one new responder (the embodied pack).
        await gen.FireAsync(new ExperienceUsed("EchoPackN1", "after"));
        var afterFire = (await gen.GetTimelineAsync()).OfType<PackEmission>().Count();
        Assert.Equal(1, afterFire - afterInstall);

        var lastEmission = (await gen.GetTimelineAsync()).OfType<PackEmission>().Last();
        Assert.Equal("EchoPackN1", lastEmission.Pack);
        Assert.Equal("echo:after", lastEmission.Output);
    }

    // Pure unit test — no TestCluster today, and none needed: only exercises MarketplaceSeeds statics.
    // A plain nested class (no NeuronTestBase) keeps it at that cost.
    public sealed class MarketplaceSeedsPackagingTests
    {
        [Fact]
        public void Dev_Can_Package_And_Publish_Dummy_Distributions_Using_Seeds_Helpers()
        {
            // This exercises the core dev-on-DigitalBrain case: packaging a new kernel version or behavior pack
            // for publish (share to marketplace) and install.
            var kernelCmd = MarketplaceSeeds.KernelPublishCommand("0.4.0-dev");
            Assert.Equal("kernel", kernelCmd.PackName);
            Assert.Contains("0.4.0-dev", kernelCmd.Version);

            // DummyBehaviorPackPublish removed (demo bloat delete).
            // In real dev: fire to market grain (local or via remote proxy to private marketplace repo).
            // Here we just validate the "packaging" step produces valid commands.
        }
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build Brain.slnx`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 4: Run targeted tests**

Run: `dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "FullyQualifiedName~TrustedSeedInstallTests|FullyQualifiedName~HandlerGrowthTests" --no-build --logger "console;verbosity=minimal"`
Expected: 4 passed, 0 failed (2 + 2).

- [ ] **Step 5: Commit**

```bash
git add DigitalBrain.Tests/Trust/TrustedSeedInstallTests.cs DigitalBrain.Tests/Distribution/HandlerGrowthTests.cs
git commit -m "test(cleanup): migrate TrustedSeedInstallTests/HandlerGrowthTests to NeuronTestBase, extract no-cluster facts to plain nested classes"
```

---

### Task 5: Convert LlmResponderScopedConfigTests.cs (two ConfigureSilo-bearing classes in one file — most complex, done last)

**Files:**
- Modify: `DigitalBrain.Tests/Kernel/LlmResponderScopedConfigTests.cs`

**Interfaces:**
- Consumes: `NeuronTestBase.ConfigureSilo` override (existing); the "nested class extends `NeuronTestBase` directly" pattern from Task 3.
- Produces: nothing new for later tasks (last file in the migration).

**Note (see Task 2's correction):** this file keeps `ScopedLlmResponderSiloConfigurator : ISiloConfigurator` and `NullScopedLlmResponderSiloConfigurator : ISiloConfigurator` as standalone classes (both hold a static `Factory` field the facts reference directly), so — same as `LlmResponderTests.cs` in Task 2 — it needs `using Orleans.TestingHost;`. Already included in the target code below.

- [ ] **Step 1: Replace `DigitalBrain.Tests/Kernel/LlmResponderScopedConfigTests.cs` in full**

```csharp
using DigitalBrain.Core;
using DigitalBrain.Core.Config;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Config;
using DigitalBrain.Kernel.Llm;
using DigitalBrain.TestKit;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;

namespace DigitalBrain.Tests.Kernel;

// Records the (provider, apiKey) it is asked for and returns a stub that prefixes "SCOPED:".
// Lets the test prove the responder read provider/key from the store and used the scoped client,
// never the global IChatClient.
public sealed class RecordingScopedChatClientFactory : IScopedChatClientFactory
{
    public readonly List<(string Provider, string? ApiKey)> Requests = new();

    public IChatClient? Create(string provider, string? apiKey)
    {
        Requests.Add((provider, apiKey));
        return new ScopedPrefixChatClient();
    }
}

internal sealed class ScopedPrefixChatClient : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var prompt = string.Concat(messages.Select(m => m.Text));
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "SCOPED:" + prompt)));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Streaming not used.");

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}

// Emitter that broadcasts an AskLlm carrying a config reference.
public interface IScopedAskLlmEmitter : INeuron
{
    Task BroadcastScopedAskAsync(
        string prompt, string replyType, IReadOnlyDictionary<string, object?> replyProps,
        string? configPack, string? configScope);

    // Stores config through the silo's IPackConfigStore. The fast-path store keeps config in a per-silo
    // in-memory backing dictionary encrypted with that silo's ephemeral DataProtection keys, so the emitter
    // and responder must share a silo to read the same plaintext — the tests rely on NeuronTestBase's
    // single-silo default (a 2-silo cluster placed them nondeterministically, which made these tests flaky
    // before this was pinned).
    Task StoreConfigAsync(string scope, string pack, Dictionary<string, string> values);
}

public sealed class ScopedAskLlmEmitter(Microsoft.Extensions.Logging.ILogger<ScopedAskLlmEmitter> logger, NeuronJournals journals) : Neuron(logger, journals), IScopedAskLlmEmitter
{
    public Task BroadcastScopedAskAsync(
        string prompt, string replyType, IReadOnlyDictionary<string, object?> replyProps,
        string? configPack, string? configScope) =>
        Broadcast(new AskLlm(prompt, replyType, replyProps, configPack, configScope));

    public Task StoreConfigAsync(string scope, string pack, Dictionary<string, string> values) =>
        ServiceProvider.GetRequiredService<IPackConfigStore>().SetAsync(scope, pack, values);
}

// Wires the global AnswerPrefixChatClient (proves it is NOT used on the scoped path), a real
// in-memory PackConfigStore, and the recording scoped factory shared via a static so the test can assert on it.
public sealed class ScopedLlmResponderSiloConfigurator : ISiloConfigurator
{
    public static readonly RecordingScopedChatClientFactory Factory = new();

    public void Configure(ISiloBuilder siloBuilder) =>
        siloBuilder.ConfigureServices(services =>
        {
            services.AddSingleton<IChatClient, AnswerPrefixChatClient>();
            services.AddSingleton<IScopedChatClientFactory>(Factory);
            services.AddPackConfigStore(blobsForKeyRing: null);
        });
}

// Returns null for every Create call — simulates the graceful-fallback path (e.g. openai with no key).
public sealed class NullScopedChatClientFactory : IScopedChatClientFactory
{
    public readonly List<(string Provider, string? ApiKey)> Requests = new();

    public IChatClient? Create(string provider, string? apiKey)
    {
        Requests.Add((provider, apiKey));
        return null;
    }
}

// Wires the NullScopedChatClientFactory + global AnswerPrefixChatClient + real in-memory PackConfigStore.
// The null factory forces the responder to fall back to the global client.
public sealed class NullScopedLlmResponderSiloConfigurator : ISiloConfigurator
{
    public static readonly NullScopedChatClientFactory Factory = new();

    public void Configure(ISiloBuilder siloBuilder) =>
        siloBuilder.ConfigureServices(services =>
        {
            services.AddSingleton<IChatClient, AnswerPrefixChatClient>();
            services.AddSingleton<IScopedChatClientFactory>(Factory);
            services.AddPackConfigStore(blobsForKeyRing: null);
        });
}

public class LlmResponderScopedConfigTests : NeuronTestBase
{
    protected override void ConfigureSilo(ISiloBuilder builder) =>
        new ScopedLlmResponderSiloConfigurator().Configure(builder);

    [Fact]
    public async Task AskLlm_with_ConfigPack_uses_scoped_client_from_stored_provider_and_key()
    {
        ScopedLlmResponderSiloConfigurator.Factory.Requests.Clear();

        const string pack = "DigitalBrain.Telegram.Responder";
        const string scope = "default";

        var responder = Grain<ILlmResponderNeuron>("responder-scoped-1");
        await responder.GetTimelineAsync();

        var emitter = Grain<IScopedAskLlmEmitter>("emitter-scoped-1");
        await emitter.StoreConfigAsync(scope, pack, new Dictionary<string, string>
        {
            ["llm_provider"] = "openai",
            ["llm_key"] = "sk-test",
        });
        var replyProps = new Dictionary<string, object?> { ["chatId"] = 7 };
        await emitter.BroadcastScopedAskAsync("hi", "TelegramReplyRequested", replyProps, pack, scope);

        Signal? signal = null;
        for (var attempt = 0; attempt < 20 && signal is null; attempt++)
        {
            await Task.Delay(50);
            var timeline = await responder.GetTimelineAsync();
            signal = timeline.OfType<Signal>().FirstOrDefault(s => s.Name == "TelegramReplyRequested");
        }

        Assert.NotNull(signal);
        Assert.Equal("SCOPED:hi", signal.Props["text"]);

        var request = Assert.Single(ScopedLlmResponderSiloConfigurator.Factory.Requests);
        Assert.Equal("openai", request.Provider);
        Assert.Equal("sk-test", request.ApiKey);
    }

    [Fact]
    public async Task AskLlm_without_ConfigPack_uses_global_client()
    {
        ScopedLlmResponderSiloConfigurator.Factory.Requests.Clear();

        var responder = Grain<ILlmResponderNeuron>("responder-global-1");
        await responder.GetTimelineAsync();

        var emitter = Grain<IScopedAskLlmEmitter>("emitter-global-1");
        var replyProps = new Dictionary<string, object?> { ["chatId"] = 9 };
        await emitter.BroadcastScopedAskAsync("hi", "ReplyGlobal", replyProps, configPack: null, configScope: null);

        Signal? signal = null;
        for (var attempt = 0; attempt < 20 && signal is null; attempt++)
        {
            await Task.Delay(50);
            var timeline = await responder.GetTimelineAsync();
            signal = timeline.OfType<Signal>().FirstOrDefault(s => s.Name == "ReplyGlobal");
        }

        Assert.NotNull(signal);
        Assert.Equal("ANSWER:hi", signal.Props["text"]);
        Assert.Empty(ScopedLlmResponderSiloConfigurator.Factory.Requests);
    }

    // NullScopedLlmResponderSiloConfigurator is mutually exclusive with ScopedLlmResponderSiloConfigurator
    // (the outer class's ConfigureSilo) — this nested class extends NeuronTestBase directly so it applies
    // its own ConfigureSilo instead of inheriting the outer one.
    public sealed class NullScopedFactoryFallbackTests : NeuronTestBase
    {
        protected override void ConfigureSilo(ISiloBuilder builder) =>
            new NullScopedLlmResponderSiloConfigurator().Configure(builder);

        [Fact]
        public async Task AskLlm_scoped_factory_returns_null_falls_back_to_global_client()
        {
            NullScopedLlmResponderSiloConfigurator.Factory.Requests.Clear();

            const string pack = "DigitalBrain.Telegram.Responder";
            const string scope = "default";

            var responder = Grain<ILlmResponderNeuron>("responder-nullfactory-1");
            await responder.GetTimelineAsync();

            var emitter = Grain<IScopedAskLlmEmitter>("emitter-nullfactory-1");
            // Store openai config but with no key — factory will be asked and return null.
            await emitter.StoreConfigAsync(scope, pack, new Dictionary<string, string>
            {
                ["llm_provider"] = "openai",
                ["llm_key"] = "",
            });
            var replyProps = new Dictionary<string, object?> { ["chatId"] = 42 };
            await emitter.BroadcastScopedAskAsync("hi", "TelegramReplyFallback", replyProps, pack, scope);

            // Should still get a reply — from the global AnswerPrefixChatClient, not silence.
            Signal? signal = null;
            for (var attempt = 0; attempt < 20 && signal is null; attempt++)
            {
                await Task.Delay(50);
                var timeline = await responder.GetTimelineAsync();
                signal = timeline.OfType<Signal>().FirstOrDefault(s => s.Name == "TelegramReplyFallback");
            }

            Assert.NotNull(signal);
            Assert.Equal("ANSWER:hi", signal.Props["text"]);
            // Factory was called (it attempted to build) but returned null.
            var request = Assert.Single(NullScopedLlmResponderSiloConfigurator.Factory.Requests);
            Assert.Equal("openai", request.Provider);
        }
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build Brain.slnx`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Run targeted tests**

Run: `dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "FullyQualifiedName~LlmResponderScopedConfigTests" --no-build --logger "console;verbosity=minimal"`
Expected: 3 passed, 0 failed (2 outer + 1 nested).

- [ ] **Step 4: Commit**

```bash
git add DigitalBrain.Tests/Kernel/LlmResponderScopedConfigTests.cs
git commit -m "test(cleanup): migrate LlmResponderScopedConfigTests to NeuronTestBase, split null-factory fact into nested class"
```

---

### Task 6: Final whole-suite verification, branch review, and ledger update

**Files:** none (verification + docs only)

- [ ] **Step 1: Confirm no manual TestClusterBuilder/IAsyncLifetime boilerplate remains in DigitalBrain.Tests (outside the intentionally-out-of-scope GatewayGrpcWireTests.cs)**

Run: `grep -rn "TestClusterBuilder\| : IAsyncLifetime" DigitalBrain.Tests --include=*.cs`
Expected: zero hits. (`GatewayGrpcWireTests.cs` uses `IClassFixture<WebApplicationFactory<Program>>`, not `TestClusterBuilder`/`IAsyncLifetime`, so it produces no hits either — this closes out the entire test-harness-dedup initiative across Groups 1–4.)

- [ ] **Step 2: Full build**

Run: `dotnet build Brain.slnx`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Full DigitalBrain.Tests suite**

Run: `dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --no-build --logger "console;verbosity=minimal"`
Expected: 0 failed. Total pass count should be the pre-slice baseline plus 0 net new/removed tests (same 15 facts across these 9 files, just redistributed across 4 new nested classes) — same tests, same assertions, different harness.

- [ ] **Step 4: Prepare diff range for reviewer**

Run: `git diff master...spec/group4-test-harness-dedup --stat`
Expected: the 9 converted test files + 2 new docs files (spec + this plan). No `DigitalBrain.TestKit` files, no AppHost/hosting files — `aspire doctor` not required.

- [ ] **Step 5: Whole-branch review**

Fresh reviewer subagent reviews the full diff against this plan and the spec's guardrails: delete bias (net LOC down), self-explanatory names (esp. the 4 new nested-class names: `DefaultUngatedTests`, `SignatureVerificationTests`, `MarketplaceSeedsPackagingTests`, `NullScopedFactoryFallbackTests`), no vacuous comments, no behavior change (same assertions), no re-invocation of `NeuronTestSiloConfigurator` inside any `ConfigureSilo` override, zero changes to `NeuronTestBase`/`TestDigitalBrain`. Target 0 critical/important findings; address any that surface with a fresh implementer pass, then re-verify (build + full suite).

- [ ] **Step 6: Finishing-a-development-branch**

Invoke the `finishing-a-development-branch` skill to decide merge/PR/cleanup for `spec/group4-test-harness-dedup`.

- [ ] **Step 7: Update ledger**

Append a round entry to `CONTINUITY.md` and `.superpowers/sdd/progress.md` summarizing: 9 files migrated (15 facts total, redistributed into 4 new nested classes), zero `NeuronTestBase`/`TestDigitalBrain` changes needed, the Group 1–4 test-harness-dedup initiative is now fully closed out (only `GatewayGrpcWireTests.cs` remains on a different, intentionally-out-of-scope pattern), and the still-open deferred items (`UnitTest1.cs` domain split, `core-bloat-delete-design.md` closeout, fresh dead-code scan) carried forward unchanged.
