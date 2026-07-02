# Group 3 Test Harness Dedup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Migrate the 10 remaining `DigitalBrain.Tests/` files that hand-roll `IAsyncLifetime` + `Orleans.TestingHost.TestClusterBuilder` to inherit `NeuronTestBase`, deleting the duplicated ceremony and the static-field bus bridges those files use today.

**Architecture:** Three small additive members go on `NeuronTestBase`/`TestDigitalBrain` first (`ConfigureClient`, `InitialSilosCount`, `Cluster`), each proven by the very next file that needs it. Then each target file converts to `: NeuronTestBase`, moving any custom silo wiring into a `ConfigureSilo` override that captures state via instance fields (no more `static Shared*Bus` bridge — `ConfigureSilo` already runs as an instance-bound delegate per `TestDigitalBrain.cs`'s existing `AsyncLocal` bridge).

**Tech Stack:** .NET (net11.0), Orleans 10.2 `Orleans.TestingHost`, xunit.

## Global Constraints

- Full spec: `docs/specs/2026-07-02-group3-test-harness-dedup-design.md`. Read it before starting if anything below is ambiguous.
- Mechanical, behavior-preserving. Same test coverage, same assertions, same outcomes — only the harness plumbing changes.
- Preserve every `[Collection(...)]` / `[CollectionDefinition(..., DisableParallelization = true)]` attribute exactly as it exists today.
- Relative paths only. NEVER `C:\Users` paths.
- Self-explanatory names; NO vacuous `/// <summary>`. Keep existing inline "why" comments (SECURITY notes, drain-order notes, etc.) verbatim — they document real invariants, not the refactor.
- Verification ritual after **every task**: `dotnet build` then `dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "<targeted>" --no-build --logger "console;verbosity=minimal"`. `aspire doctor` NOT required (no AppHost/hosting files touched).
- Branch: `spec/group3-test-harness-dedup` (already created).
- Delete bias: net LOC should go down (removing `_cluster`/`InitializeAsync`/`DisposeAsync`/static-bridge boilerplate consistently outweighs the few added lines in `NeuronTestBase`).
- Use `Edit` only after reading the exact current file content first (already done for every file in this plan — the "Before" blocks below are the verified current content).
- Fresh implementer + reviewer subagent per task.
- Update `CONTINUITY.md` and `.superpowers/sdd/progress.md` at the end of the round (not per-task).

---

### Task 1: Extend NeuronTestBase and TestDigitalBrain with three additive hooks

**Files:**
- Modify: `DigitalBrain.TestKit/TestDigitalBrain.cs` (full rewrite, 56 lines → ~70 lines)
- Modify: `DigitalBrain.TestKit/NeuronTestBase.cs` (full rewrite, 23 lines → ~30 lines)

**Interfaces:**
- Produces: `NeuronTestBase.ConfigureClient(IClientBuilder builder)` (virtual, no-op default), `NeuronTestBase.InitialSilosCount` (virtual, `short`, default `1`), `NeuronTestBase.Cluster` (protected, `TestCluster`). These three are consumed by Tasks 3, 4, and 5/6.
- Consumes: nothing new — `ISiloBuilder`/`ISiloConfigurator` resolve via the existing Orleans-SDK global usings (confirmed: today's `NeuronTestBase.cs` uses `ISiloBuilder` with zero Orleans-namespace `using` statements). `IClientBuilder`/`IClientBuilderConfigurator` resolve the same way (confirmed: `DigitalBrain.Tests/Kernel/TimelineStreamTests.cs` uses both today with no such `using` either). `TestCluster` does NOT resolve globally — every file using it today carries an explicit `using Orleans.TestingHost;`.

- [ ] **Step 1: Replace `TestDigitalBrain.cs` in full**

```csharp
using DigitalBrain.Core;
using Microsoft.Extensions.Configuration;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.TestKit;

public sealed class TestDigitalBrain(
    Action<ISiloBuilder>? extendSilo = null,
    Action<IClientBuilder>? extendClient = null,
    short initialSilosCount = 1) : IDigitalBrain, IAsyncLifetime
{
    private readonly Action<ISiloBuilder>? _extendSilo = extendSilo;
    private readonly Action<IClientBuilder>? _extendClient = extendClient;
    private readonly short _initialSilosCount = initialSilosCount;
    private TestCluster? _cluster;

    public TestCluster Cluster => _cluster!;

    public async Task InitializeAsync()
    {
        var builder = new TestClusterBuilder(initialSilosCount: _initialSilosCount);
        builder.AddSiloBuilderConfigurator<NeuronTestSiloConfigurator>();

        // AddSiloBuilderConfigurator<T>() / AddClientBuilderConfigurator<T>() require parameterless T:
        // Orleans stores T's AssemblyQualifiedName and reflectively Activator.CreateInstance()s it inside
        // the test host process, so a closure-capturing configurator instance can't be passed directly.
        // Bridge the captured delegates through AsyncLocals that the Extend* configurators read when
        // Orleans reflectively constructs them during builder.Build()/DeployAsync() below, on this same
        // async flow.
        if (_extendSilo is not null)
        {
            builder.AddSiloBuilderConfigurator<ExtendSiloConfigurator>();
            ExtendSiloConfigurator.Current.Value = _extendSilo;
        }

        if (_extendClient is not null)
        {
            builder.AddClientBuilderConfigurator<ExtendClientBuilderConfigurator>();
            ExtendClientBuilderConfigurator.Current.Value = _extendClient;
        }

        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async Task DisposeAsync()
    {
        if (_cluster is not null)
            await _cluster.StopAllSilosAsync();
    }

    public TGrain Grain<TGrain>(string key) where TGrain : IGrainWithStringKey =>
        _cluster!.GrainFactory.GetGrain<TGrain>(key);

    public Task FireAsync<T>(T synapse) where T : Synapse =>
        Grain<INeuron>(synapse.SynapseId.ToString()).DeliverAsync(synapse);

    public Task DeliverAsync<T>(T synapse) where T : Synapse =>
        synapse.Receiver is { } r
            ? Grain<INeuron>(r.Value).DeliverAsync(synapse)
            : throw new InvalidOperationException("DeliverAsync requires synapse.Receiver to be set.");

    private sealed class ExtendSiloConfigurator : ISiloConfigurator
    {
        public static readonly AsyncLocal<Action<ISiloBuilder>?> Current = new();

        public void Configure(ISiloBuilder siloBuilder) => Current.Value?.Invoke(siloBuilder);
    }

    private sealed class ExtendClientBuilderConfigurator : IClientBuilderConfigurator
    {
        public static readonly AsyncLocal<Action<IClientBuilder>?> Current = new();

        public void Configure(IConfiguration configuration, IClientBuilder clientBuilder) =>
            Current.Value?.Invoke(clientBuilder);
    }
}
```

- [ ] **Step 2: Replace `NeuronTestBase.cs` in full**

```csharp
using DigitalBrain.Core;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.TestKit;

public abstract class NeuronTestBase : IAsyncLifetime
{
    private TestDigitalBrain _brain = null!;

    protected virtual void ConfigureSilo(ISiloBuilder builder) { }
    protected virtual void ConfigureClient(IClientBuilder builder) { }
    protected virtual short InitialSilosCount => 1;

    protected TestCluster Cluster => _brain.Cluster;

    protected TGrain Grain<TGrain>(string key) where TGrain : IGrainWithStringKey => _brain.Grain<TGrain>(key);
    protected Task FireAsync<T>(T synapse) where T : Synapse => _brain.FireAsync(synapse);
    protected Task DeliverAsync<T>(T synapse) where T : Synapse => _brain.DeliverAsync(synapse);

    public Task InitializeAsync()
    {
        _brain = new TestDigitalBrain(ConfigureSilo, ConfigureClient, InitialSilosCount);
        return _brain.InitializeAsync();
    }

    public Task DisposeAsync() => _brain.DisposeAsync();
}
```

- [ ] **Step 3: Build**

Run: `dotnet build Brain.slnx`
Expected: `Build succeeded. 0 Error(s)` — every currently-migrated `NeuronTestBase` subclass (Group 1/2: `ContextRecallTests`, `UserSessionNeuronTests`, `CompanyKnowledgeTests`, `DigitalBrainToolsTests`, `SoftwareEngineeringReviewerTests`, `ChatNeuronTests`, `DbSupportNeuronTests`, `UnitTest1.cs`'s `NeuronTests`) must still compile unchanged since all three new members are additive with safe defaults.

- [ ] **Step 4: Run the full DigitalBrain.Tests suite as a regression check**

Run: `dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --no-build --logger "console;verbosity=minimal"`
Expected: same pass count as on branch before this change (no new tests yet — this step only proves the two rewritten TestKit files didn't break any existing subclass).

- [ ] **Step 5: Commit**

```bash
git add DigitalBrain.TestKit/TestDigitalBrain.cs DigitalBrain.TestKit/NeuronTestBase.cs
git commit -m "feat(testkit): add ConfigureClient/InitialSilosCount/Cluster hooks to NeuronTestBase"
```

---

### Task 2: Convert the 3 fully mechanical files

**Files:**
- Modify: `DigitalBrain.Tests/Economics/LicenseAndEntitlementTests.cs`
- Modify: `DigitalBrain.Tests/Kernel/RollingUpdateRollbackTests.cs`
- Modify: `DigitalBrain.Tests/Telegram/TelegramDeepLinkRoutingTests.cs`

**Interfaces:**
- Consumes: `NeuronTestBase.Grain<T>(key)` (existing), `NeuronTestBase.Cluster` (Task 1, for `TelegramDeepLinkRoutingTests`'s `GatewayService` construction via `Cluster.GrainFactory`).

- [ ] **Step 1: Replace `DigitalBrain.Tests/Economics/LicenseAndEntitlementTests.cs` in full**

```csharp
using System.Text.Json;
using DigitalBrain.Core;
using DigitalBrain.Kernel;
using DigitalBrain.TestKit;

namespace DigitalBrain.Tests.Economics;

public class LicenseAndEntitlementTests : NeuronTestBase
{
    [Fact]
    public async Task License_Issues_Verifies_And_Gates_Entitlement()
    {
        var license = Grain<ILicenseNeuron>("license-main");
        var token = await license.IssueLicenseAsync("PackA", "buyer1");

        Assert.True(await license.HasLicenseAsync("PackA", "buyer1"));
        Assert.False(await license.HasLicenseAsync("PackA", "stranger"));

        Assert.True(await license.VerifyLicenseAsync(token, "PackA", "buyer1"));
        Assert.False(await license.VerifyLicenseAsync(token, "PackA", "stranger")); // payload mismatch
        Assert.False(await license.VerifyLicenseAsync("not-a-token", "PackA", "buyer1")); // malformed
    }

    [Fact]
    public async Task Premium_Pack_Install_Requires_A_License()
    {
        var (priv, pub) = PackSignatureVerifier.GenerateKeyPair();
        var premium = PackSignatureVerifier.SignPack(
            new NeuroPack("Premium", "1.0", OwnerId: "dev", Code: "ok", Price: 9.99m), priv, pub);

        var market = Grain<IMarketplaceNeuron>("market-premium");
        await Publish(market, premium);

        // No license -> the premium gate rejects the install.
        await market.FireAsync(new InstallFromMarketplace("Premium", "1.0", BuyerId: "buyer1"));
        Assert.DoesNotContain(await market.GetTimelineAsync(), s => s is NeuroPackInstalled);

        // Grant a license -> install succeeds.
        await Grain<ILicenseNeuron>("license-main").IssueLicenseAsync("Premium", "buyer1");
        await market.FireAsync(new InstallFromMarketplace("Premium", "1.0", BuyerId: "buyer1"));
        Assert.Contains(await market.GetTimelineAsync(), s => s is NeuroPackInstalled);
    }

    [Fact]
    public async Task Full_Purchase_Flow_Synthetic_Issues_License_And_Installs()
    {
        var (priv, pub) = PackSignatureVerifier.GenerateKeyPair();
        var premium = PackSignatureVerifier.SignPack(
            new NeuroPack("FlowPack", "1.0", OwnerId: "dev", Code: "ok", Price: 5m), priv, pub);

        var market = Grain<IMarketplaceNeuron>("market-flow");
        await Publish(market, premium);

        // Buyer pays via the synthetic gateway.
        var gateway = new SyntheticPaymentGateway();
        var session = await gateway.CreateCheckoutAsync(new CheckoutRequest("FlowPack", "buyerX", "FlowPack", 5m, "s", "c"));
        var confirmation = gateway.VerifyWebhook(
            JsonSerializer.Serialize(new { sessionId = session.SessionId, bundleId = "FlowPack", userId = "buyerX" }), null);
        Assert.True(confirmation.Completed);

        // Payment confirmed -> issue license -> install the premium pack.
        await Grain<ILicenseNeuron>("license-main")
            .IssueLicenseAsync(confirmation.BundleId!, confirmation.UserId!);
        await market.FireAsync(new InstallFromMarketplace("FlowPack", "1.0", BuyerId: "buyerX"));

        Assert.Contains(await market.GetTimelineAsync(), s => s is NeuroPackInstalled);
    }

    private static Task Publish(IMarketplaceNeuron market, NeuroPack pack) =>
        market.FireAsync(new PublishToMarketplace(
            pack.Name, pack.Version, pack.Code, pack.OwnerId, pack.IsPrivate, pack.CommissionRate,
            pack.Description, pack.AuthorPublicKeyBase64, pack.SignatureBase64, pack.Price)).AsTask();
}
```

- [ ] **Step 2: Replace `DigitalBrain.Tests/Kernel/RollingUpdateRollbackTests.cs` in full**

```csharp
using DigitalBrain.Core;
using DigitalBrain.TestKit;

namespace DigitalBrain.Tests.Kernel;

public class RollingUpdateRollbackTests : NeuronTestBase
{
    [Fact]
    public async Task Verify_Failure_Rolls_Back_And_Does_Not_Complete()
    {
        var aspire = Grain<IAspireNeuron>("aspire-rollback");
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
}
```

- [ ] **Step 3: Replace `DigitalBrain.Tests/Telegram/TelegramDeepLinkRoutingTests.cs` in full**

```csharp
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Gateway;
using DigitalBrain.Runtime.Grpc;
using DigitalBrain.Telegram.Channel;
using DigitalBrain.Tests.TestSupport;
using DigitalBrain.TestKit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace DigitalBrain.Tests.Telegram;

// Verifies that GatewayService.Send routes a TelegramMessageReceived envelope
// to the per-chat TelegramChatNeuron rather than broadcasting via IngressNeuron.
[Collection("tg-routing-host")]
public class TelegramDeepLinkRoutingTests : NeuronTestBase
{
    private readonly HomeFeedBus _homeFeedBus = new();

    private GatewayService NewService() =>
        new(Cluster.GrainFactory, new ConfigurationBuilder().Build(), _homeFeedBus,
            new SignalEgressBus(),
            new FakeHostEnvironment(),
            NullLogger<GatewayService>.Instance);

    private static byte[] Json(long chatId, string text) =>
        System.Text.Encoding.UTF8.GetBytes(
            $"{{\"chatId\":{chatId},\"fromUserId\":1,\"text\":\"{text}\",\"updateId\":1}}");

    [Fact]
    public async Task Send_TelegramMessageReceived_start_routes_to_chat_neuron_and_binds()
    {
        await NewService().Send(new SynapseEnvelope
        {
            TypeName = "TelegramMessageReceived",
            CorrelationId = "tg-routing-1",
            Payload = global::Google.Protobuf.ByteString.CopyFrom(Json(200, "/start hello-world"))
        }, TestServerCallContext.Create());

        var chat = Grain<ITelegramChatNeuron>("tg-chat-200");
        Assert.Equal("hello-world", await chat.GetBoundBundleAsync());
    }

    [Fact]
    public async Task Send_TelegramMessageReceived_returns_the_same_envelope()
    {
        var envelope = new SynapseEnvelope
        {
            TypeName = "TelegramMessageReceived",
            CorrelationId = "tg-routing-2",
            Payload = global::Google.Protobuf.ByteString.CopyFrom(Json(201, "/start hello-world"))
        };

        var result = await NewService().Send(envelope, TestServerCallContext.Create());

        Assert.Equal(envelope.TypeName, result.TypeName);
        Assert.Equal(envelope.CorrelationId, result.CorrelationId);
    }
}

[CollectionDefinition("tg-routing-host", DisableParallelization = true)]
public sealed class TgRoutingHostCollection;
```

- [ ] **Step 4: Build**

Run: `dotnet build Brain.slnx`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 5: Run targeted tests**

Run: `dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "FullyQualifiedName~LicenseAndEntitlement|FullyQualifiedName~RollingUpdateRollback|FullyQualifiedName~TelegramDeepLinkRouting" --no-build --logger "console;verbosity=minimal"`
Expected: all pass (6 facts total: 3 + 1 + 2), 0 failed.

- [ ] **Step 6: Commit**

```bash
git add DigitalBrain.Tests/Economics/LicenseAndEntitlementTests.cs DigitalBrain.Tests/Kernel/RollingUpdateRollbackTests.cs DigitalBrain.Tests/Telegram/TelegramDeepLinkRoutingTests.cs
git commit -m "test(cleanup): migrate LicenseAndEntitlement/RollingUpdateRollback/TelegramDeepLinkRouting to NeuronTestBase"
```

---

### Task 3: Convert HomeFeedCrossSiloTests.cs (validates InitialSilosCount + Cluster)

**Files:**
- Modify: `DigitalBrain.Tests/Ui/HomeFeedCrossSiloTests.cs`

**Interfaces:**
- Consumes: `NeuronTestBase.InitialSilosCount` override, `NeuronTestBase.Cluster` (both from Task 1).

- [ ] **Step 1: Replace `DigitalBrain.Tests/Ui/HomeFeedCrossSiloTests.cs` in full**

```csharp
using DigitalBrain.Core;
using DigitalBrain.Kernel;
using DigitalBrain.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;

namespace DigitalBrain.Tests.Ui;

public class HomeFeedCrossSiloTests : NeuronTestBase
{
    protected override short InitialSilosCount => 2;

    [Fact]
    public async Task Broadcast_On_Silo0_Received_On_Silo1()
    {
        var bus0 = ((InProcessSiloHandle)Cluster.Silos[0]).SiloHost.Services.GetRequiredService<HomeFeedBus>();
        var bus1 = ((InProcessSiloHandle)Cluster.Silos[1]).SiloHost.Services.GetRequiredService<HomeFeedBus>();

        using var subscription = bus1.Subscribe();
        var card = new RfwCard("digitalbrain", "CrossSiloCard", "{\"x\":1}");

        bus0.Broadcast(card);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var received = await subscription.Reader.ReadAsync(cts.Token);
        Assert.Equal("CrossSiloCard", received.RootWidget);
    }

    [Fact]
    public async Task Broadcast_On_Silo0_Also_Delivered_To_Silo0_Subscriber()
    {
        // In cluster mode Broadcast goes out via the stream and loops back through this silo's own subscriber,
        // so a client connected to the producing silo must still receive the card (no synchronous local fanout).
        var bus0 = ((InProcessSiloHandle)Cluster.Silos[0]).SiloHost.Services.GetRequiredService<HomeFeedBus>();

        using var subscription = bus0.Subscribe();
        var card = new RfwCard("digitalbrain", "SelfDeliveryCard", "{\"x\":2}");

        bus0.Broadcast(card);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var received = await subscription.Reader.ReadAsync(cts.Token);
        Assert.Equal("SelfDeliveryCard", received.RootWidget);
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build Brain.slnx`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Run targeted tests**

Run: `dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "FullyQualifiedName~HomeFeedCrossSilo" --no-build --logger "console;verbosity=minimal"`
Expected: 2 passed, 0 failed. This is the first real proof that `InitialSilosCount` actually produces a 2-silo cluster.

- [ ] **Step 4: Commit**

```bash
git add DigitalBrain.Tests/Ui/HomeFeedCrossSiloTests.cs
git commit -m "test(cleanup): migrate HomeFeedCrossSiloTests to NeuronTestBase (validates InitialSilosCount hook)"
```

---

### Task 4: Convert TimelineStreamTests.cs (validates ConfigureClient)

**Files:**
- Modify: `DigitalBrain.Tests/Kernel/TimelineStreamTests.cs`

**Interfaces:**
- Consumes: `NeuronTestBase.ConfigureClient` override, `NeuronTestBase.Cluster` (both from Task 1).

- [ ] **Step 1: Replace `DigitalBrain.Tests/Kernel/TimelineStreamTests.cs` in full**

```csharp
using DigitalBrain.Kernel;
using DigitalBrain.TestKit;

namespace DigitalBrain.Tests.Kernel;

// Verifies that the DigitalBrainTimeline stream provider is registered and the Timeline() extension resolves the correct stream.
public class TimelineStreamTests : NeuronTestBase
{
    protected override void ConfigureClient(IClientBuilder builder) =>
        builder.AddMemoryStreams(SynapseStream.ProviderName);

    [Fact]
    public void ProviderName_Is_DigitalBrainTimeline()
    {
        Assert.Equal("DigitalBrainTimeline", SynapseStream.ProviderName);
    }

    [Fact]
    public void Timeline_Extension_Returns_Stream_For_Global_Namespace()
    {
        var provider = Cluster.Client.GetStreamProvider(SynapseStream.ProviderName);
        var stream = provider.Timeline();

        Assert.NotNull(stream);
        Assert.Equal("global", stream.StreamId.GetKeyAsString());
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build Brain.slnx`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Run targeted tests**

Run: `dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "FullyQualifiedName~TimelineStreamTests" --no-build --logger "console;verbosity=minimal"`
Expected: 2 passed, 0 failed. This is the first real proof that `ConfigureClient` actually registers the client-side stream provider.

- [ ] **Step 4: Commit**

```bash
git add DigitalBrain.Tests/Kernel/TimelineStreamTests.cs
git commit -m "test(cleanup): migrate TimelineStreamTests to NeuronTestBase (validates ConfigureClient hook)"
```

---

### Task 5: Convert the HomeFeedBus-based files (GatewayServiceTests, GenericSendTests, ExperienceStepDispatchTests)

**Files:**
- Modify: `DigitalBrain.Tests/Gateway/GatewayServiceTests.cs`
- Modify: `DigitalBrain.Tests/Gateway/GenericSendTests.cs`
- Modify: `DigitalBrain.Tests/Kernel/ExperienceStepDispatchTests.cs`

**Interfaces:**
- Consumes: `NeuronTestBase.ConfigureSilo` override (existing), `NeuronTestBase.Cluster.GrainFactory` (Task 1).

- [ ] **Step 1: Replace `DigitalBrain.Tests/Gateway/GatewayServiceTests.cs` in full**

```csharp
using DigitalBrain.Core;
using DigitalBrain.Runtime.Grpc;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Foundry;
using DigitalBrain.Kernel.Gateway;
using DigitalBrain.Tests.TestSupport;
using DigitalBrain.TestKit;
using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans.Journaling;

namespace DigitalBrain.Tests.Gateway;

[Collection("silo-host")]
public class GatewayServiceTests : NeuronTestBase
{
    private readonly HomeFeedBus _homeFeedBus = new();

    protected override void ConfigureSilo(ISiloBuilder builder) => builder
        .AddMemoryGrainStorageAsDefault()
        .AddMemoryStreams("Default")
        .AddMemoryStreams("HomeFeed")
        .AddMemoryStreams("DigitalBrainTimeline")
        .AddMemoryGrainStorage("PubSubStore")
        .ConfigureServices(services =>
        {
            services.AddKeyedScoped<IDurableList<Synapse>>("in-journal", (_, _) => new InMemoryDurableList<Synapse>());
            services.AddKeyedScoped<IDurableList<Synapse>>("out-journal", (_, _) => new InMemoryDurableList<Synapse>());
            services.AddScoped<NeuronJournals>();
            services.AddSingleton<IJournaledStateManager, TestJournaledStateManager>();
            services.AddSingleton<IPackEmbodiment, PackAlcEmbodier>();
            services.AddSingleton(_homeFeedBus);
        });

    private GatewayService NewService() =>
        new(Cluster.GrainFactory, new ConfigurationBuilder().Build(), _homeFeedBus,
            new SignalEgressBus(),
            new FakeHostEnvironment(),
            NullLogger<GatewayService>.Instance);

    [Fact]
    public async Task Ask_Ino_ReturnsNonEmptyReply()
    {
        var reply = await NewService().Ask(new AskRequest { NeuronId = "ino-main", Prompt = "hello" }, TestContext());
        Assert.False(string.IsNullOrWhiteSpace(reply.Text));
    }

    [Fact]
    public async Task Ask_NonIno_ThrowsInvalidArgument()
    {
        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            NewService().Ask(new AskRequest { NeuronId = "demo-x", Prompt = "hi" }, TestContext()));
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    [Fact]
    public async Task Fire_ThenTimeline_ShowsDemoMessage()
    {
        var svc = NewService();
        await svc.Fire(new FireRequest { NeuronId = "demo-fire", Text = "ping-123" }, TestContext());

        var timeline = await svc.Timeline(new TimelineRequest { NeuronId = "demo-fire", MaxEntries = 10 }, TestContext());
        Assert.Contains(timeline.Entries, e => e.Type == nameof(DemoMessageSynapse) && e.Text.Contains("ping-123"));
    }

    [Fact]
    public async Task WatchHomeFeed_Writes_Login_Surface_To_New_Client()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var writer = new CapturingServerStreamWriter<RfwCardEnvelope>(() => cts.Cancel());
        var svc = NewService();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            svc.WatchHomeFeed(new WatchHomeFeedRequest(), writer, TestContext(cts.Token)));

        var card = Assert.Single(writer.Messages);
        Assert.Contains("\"kind\":\"login\"", card.DataJson);
        Assert.Contains("\"synapseType\":\"LoginRequest\"", card.DataJson);
    }

    [Fact]
    public async Task Send_SurfaceDemoRequested_InstallsPack_And_BroadcastsRenderableSurface()
    {
        using var subscription = _homeFeedBus.Subscribe();
        var svc = NewService();

        await svc.Send(new SynapseEnvelope
        {
            TypeName = KernelSurfaceDemo.RequestType,
            CorrelationId = "ui-demo-test"
        }, TestContext());

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var cards = new List<RfwCard>();
        while (cards.Count < 8 &&
               (!cards.Any(c => c.DataJson.Contains("journaled response and surface update observed", StringComparison.Ordinal)) ||
                !cards.Any(c => c.DataJson.Contains("Embodied pack live", StringComparison.Ordinal))))
        {
            cards.Add(await subscription.Reader.ReadAsync(timeout.Token));
        }

        var graph = Assert.Single(cards, c => c.DataJson.Contains("journaled response and surface update observed", StringComparison.Ordinal));
        Assert.Equal("digitalbrain", graph.LibraryName);
        Assert.Equal("root", graph.RootWidget);
        Assert.Contains("\"kind\":\"activity-graph\"", graph.DataJson);
        Assert.Contains("\"edges\"", graph.DataJson);
        Assert.Contains("\"correlationId\":\"ui-demo-test\"", graph.DataJson);

        var card = Assert.Single(cards, c => c.DataJson.Contains("Embodied pack live", StringComparison.Ordinal));
        Assert.Equal("digitalbrain", card.LibraryName);
        Assert.Equal("root", card.RootWidget);
        Assert.False(string.IsNullOrWhiteSpace(card.CorrelationId));
        Assert.Contains("\"source\"", card.DataJson);
        Assert.Contains("Embodied pack live", card.DataJson);

        var generated = Grain<IGeneratedNeuron>(KernelSurfaceDemo.GeneratedNeuronKey);
        var timeline = await generated.GetOutgoingTimelineAsync();
        var emittedSurface = Assert.Single(timeline.OfType<UiSurface>(), surface =>
            surface.Props.TryGetValue(UiSurfaceKeys.SurfaceId, out var id) &&
            Equals(id, "surface-demo-pack"));
        Assert.Equal("ui-demo-test", emittedSurface.CorrelationId);
        Assert.False(string.IsNullOrWhiteSpace(emittedSurface.CausationId));

        var observability = Grain<IObservabilityNeuron>(KernelSurfaceDemo.ObservabilityNeuronKey);
        var graphTimeline = await observability.GetOutgoingTimelineAsync();
        Assert.Contains(graphTimeline.OfType<UiSurface>(), surface =>
            surface.Kind == UiSurfaceKinds.ActivityGraph &&
            surface.CorrelationId == "ui-demo-test");
    }

    private static ServerCallContext TestContext(CancellationToken cancellationToken = default) =>
        TestServerCallContext.Create(cancellationToken);

    private sealed class CapturingServerStreamWriter<T>(Action? afterFirstWrite = null) : IServerStreamWriter<T>
    {
        public List<T> Messages { get; } = new();
        public WriteOptions? WriteOptions { get; set; }

        public Task WriteAsync(T message)
        {
            Messages.Add(message);
            if (Messages.Count == 1)
            {
                afterFirstWrite?.Invoke();
            }

            return Task.CompletedTask;
        }
    }
}
```

- [ ] **Step 2: Replace `DigitalBrain.Tests/Gateway/GenericSendTests.cs` in full**

```csharp
using DigitalBrain.Core;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Foundry;
using DigitalBrain.Kernel.Gateway;
using DigitalBrain.Runtime.Grpc;
using DigitalBrain.Tests.TestSupport;
using DigitalBrain.TestKit;
using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans.Journaling;

namespace DigitalBrain.Tests.Gateway;

[Collection("signal-sink-host")]
public class GenericSendTests : NeuronTestBase
{
    private readonly HomeFeedBus _homeFeedBus = new();

    protected override void ConfigureSilo(ISiloBuilder builder) => builder
        .AddMemoryGrainStorageAsDefault()
        .AddMemoryStreams("Default")
        .AddMemoryStreams("HomeFeed")
        .AddMemoryStreams("DigitalBrainTimeline")
        .AddMemoryGrainStorage("PubSubStore")
        .ConfigureServices(services =>
        {
            services.AddKeyedScoped<IDurableList<Synapse>>("in-journal", (_, _) => new InMemoryDurableList<Synapse>());
            services.AddKeyedScoped<IDurableList<Synapse>>("out-journal", (_, _) => new InMemoryDurableList<Synapse>());
            services.AddScoped<NeuronJournals>();
            services.AddSingleton<IJournaledStateManager, TestJournaledStateManager>();
            services.AddSingleton<IPackEmbodiment, PackAlcEmbodier>();
            services.AddSingleton(_homeFeedBus);
        });

    private GatewayService NewService() =>
        new(Cluster.GrainFactory, new ConfigurationBuilder().Build(), _homeFeedBus,
            new SignalEgressBus(),
            new FakeHostEnvironment(),
            NullLogger<GatewayService>.Instance);

    [Fact]
    public async Task Send_UnknownTypeName_BroadcastsSignalToSubscribedHandlers()
    {
        var sink = Grain<ISignalSink>("sink-generic-1");
        await sink.ActivateAsync(); // prime the sink so it subscribes to the timeline

        var payload = System.Text.Encoding.UTF8.GetBytes("{\"chatId\":7,\"text\":\"hi\"}");
        await NewService().Send(new SynapseEnvelope
        {
            TypeName = TelegramSignals.MessageReceived,
            CorrelationId = "test-generic-1",
            Payload = global::Google.Protobuf.ByteString.CopyFrom(payload)
        }, TestServerCallContext.Create());

        // Poll until the sink receives the signal (broadcast is async)
        IReadOnlyList<Signal>? received = null;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (!cts.IsCancellationRequested)
        {
            received = await sink.GetReceivedSignalsAsync();
            if (received.Count > 0) break;
            await Task.Delay(50, cts.Token).ContinueWith(_ => { });
        }

        Assert.NotNull(received);
        var signal = Assert.Single(received, s => s.Name == TelegramSignals.MessageReceived);
        Assert.Equal(TelegramSignals.MessageReceived, signal.Name);
        Assert.True(signal.Props.TryGetValue("chatId", out var chatId), "Props should contain 'chatId'");
        Assert.True(chatId is 7 or 7L, $"chatId should be 7 (numeric), was {chatId} ({chatId?.GetType().Name})");
        Assert.Equal("hi", signal.Props["text"]);
    }

    // SECURITY: the generic fallback broadcasts an arbitrary named Signal onto the cluster timeline. It sits on
    // the same external gRPC service a browser reaches, so it is internal-only. An untrusted caller (no internal
    // key, non-Development) must be rejected before any forged egress/reply signal can ride the timeline.
    [Fact]
    public async Task Send_UnknownType_FromUntrustedCaller_InProduction_IsRejected()
    {
        var prodService = new GatewayService(
            Cluster.GrainFactory,
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["DigitalBrain:InternalServiceKey"] = "the-key" })
                .Build(),
            _homeFeedBus, new SignalEgressBus(), new FakeHostEnvironment("Production"),
            NullLogger<GatewayService>.Instance);

        var payload = global::Google.Protobuf.ByteString.CopyFrom(System.Text.Encoding.UTF8.GetBytes("{\"chatId\":7,\"text\":\"hi\"}"));

        // No x-internal-key header → forged egress injection rejected.
        var ex = await Assert.ThrowsAsync<RpcException>(() => prodService.Send(new SynapseEnvelope
        {
            TypeName = TelegramSignals.ReplyRequested,
            CorrelationId = "attacker-1",
            Payload = payload
        }, TestServerCallContext.Create()));
        Assert.Equal(StatusCode.Unauthenticated, ex.StatusCode);

        // Correct internal key → trusted in-cluster transport is admitted.
        var accepted = await prodService.Send(new SynapseEnvelope
        {
            TypeName = TelegramSignals.MessageReceived,
            CorrelationId = "transport-1",
            Payload = payload
        }, TestServerCallContext.WithHeaders(("x-internal-key", "the-key")));
        Assert.NotNull(accepted);
    }
}

[CollectionDefinition("signal-sink-host", DisableParallelization = true)]
public sealed class SignalSinkHostCollection;

public interface ISignalSink : INeuron
{
    Task ActivateAsync();
    Task<IReadOnlyList<Signal>> GetReceivedSignalsAsync();
}

[GrainType("digitalbrain.test.signal-sink")]
public sealed class SignalSinkGrain(
    Microsoft.Extensions.Logging.ILogger<SignalSinkGrain> logger,
    NeuronJournals journals)
    : Neuron(logger, journals), ISignalSink, IHandle<Signal>
{
    private readonly List<Signal> _received = new();

    public Task ActivateAsync() => Task.CompletedTask; // activation handled by OnActivateAsync

    public Task<IReadOnlyList<Signal>> GetReceivedSignalsAsync() =>
        Task.FromResult<IReadOnlyList<Signal>>(_received.ToList());

    public Task HandleAsync(Signal signal)
    {
        _received.Add(signal);
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 3: Replace `DigitalBrain.Tests/Kernel/ExperienceStepDispatchTests.cs` in full**

```csharp
using DigitalBrain.Core;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Foundry;
using DigitalBrain.Tests.E2E;
using DigitalBrain.TestKit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;

namespace DigitalBrain.Tests.Kernel;

[Collection("silo-host")]
public class ExperienceStepDispatchTests : NeuronTestBase
{
    private readonly HomeFeedBus _homeFeedBus = new();

    protected override void ConfigureSilo(ISiloBuilder builder) => builder
        .AddMemoryGrainStorageAsDefault()
        .AddMemoryStreams("Default")
        .AddMemoryStreams("DigitalBrainTimeline")
        .AddMemoryGrainStorage("PubSubStore")
        .ConfigureServices(services =>
        {
            services.AddKeyedScoped<IDurableList<Synapse>>("in-journal", (_, _) => new InMemoryDurableList<Synapse>());
            services.AddKeyedScoped<IDurableList<Synapse>>("out-journal", (_, _) => new InMemoryDurableList<Synapse>());
            services.AddScoped<NeuronJournals>();
            services.AddSingleton<IJournaledStateManager, TestJournaledStateManager>();
            services.AddSingleton<IPackEmbodiment, PackAlcEmbodier>();
            services.AddSingleton<IConfiguration>(
                new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["DigitalBrain:Marketplace:RejectUnsignedPacks"] = "false"
                    })
                    .Build());
            services.AddSingleton(_homeFeedBus);
        });

    [Fact]
    public async Task ExperienceStep_start_emits_intro_surface_to_home_feed()
    {
        var pack = new NeuroPack(
            Name: "travel",
            Version: "1.0",
            Code: TravelPackSource.Read(),
            OwnerId: "test",
            IsPrivate: false,
            CommissionRate: 0,
            Description: "travel domain");

        var generated = Grain<IGeneratedNeuron>("generated-travel");
        await generated.DeliverAsync(new NeuroPackInstalled(pack));

        using var sub = _homeFeedBus.Subscribe();

        await generated.FireAsync(new ExperienceStep(
            Pack: "travel",
            ExperienceId: "plan-trip",
            EventName: "start",
            Args: new Dictionary<string, string> { ["prompt"] = "plan a trip to Bali next month" }));

        var card = await ReadUntilAsync(sub.Reader, TimeSpan.FromSeconds(10),
            c => c.CorrelationId == "travel-intro");
        Assert.Equal("travel-intro", card.CorrelationId);
        Assert.Contains("WEATHER", card.DataJson);
    }

    static async Task<RfwCard> ReadUntilAsync(
        System.Threading.Channels.ChannelReader<RfwCard> reader,
        TimeSpan timeout,
        Func<RfwCard, bool> predicate)
    {
        using var cts = new CancellationTokenSource(timeout);
        await foreach (var card in reader.ReadAllAsync(cts.Token))
        {
            if (predicate(card)) return card;
        }
        throw new TimeoutException("No matching RfwCard arrived within the timeout.");
    }
}
```

- [ ] **Step 4: Build**

Run: `dotnet build Brain.slnx`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 5: Run targeted tests**

Run: `dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "FullyQualifiedName~GatewayServiceTests|FullyQualifiedName~GenericSendTests|FullyQualifiedName~ExperienceStepDispatchTests" --no-build --logger "console;verbosity=minimal"`
Expected: 8 passed (5 + 2 + 1), 0 failed.

- [ ] **Step 6: Commit**

```bash
git add DigitalBrain.Tests/Gateway/GatewayServiceTests.cs DigitalBrain.Tests/Gateway/GenericSendTests.cs DigitalBrain.Tests/Kernel/ExperienceStepDispatchTests.cs
git commit -m "test(cleanup): migrate GatewayServiceTests/GenericSendTests/ExperienceStepDispatchTests to NeuronTestBase, drop HomeFeedBus static bridge"
```

---

### Task 6: Convert the SignalEgressBus-based files (WatchSynapsesTests, PackConfigPullTests)

**Files:**
- Modify: `DigitalBrain.Tests/Gateway/WatchSynapsesTests.cs`
- Modify: `DigitalBrain.Tests/Gateway/PackConfigPullTests.cs`

**Interfaces:**
- Consumes: `NeuronTestBase.ConfigureSilo` override (existing), `NeuronTestBase.Cluster.GrainFactory` (Task 1).

- [ ] **Step 1: Replace `DigitalBrain.Tests/Gateway/WatchSynapsesTests.cs` in full**

```csharp
using DigitalBrain.Core;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Foundry;
using DigitalBrain.Kernel.Gateway;
using DigitalBrain.Kernel.Ui;
using DigitalBrain.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;

namespace DigitalBrain.Tests.Gateway;

// Proves the OUTBOUND mirror of generic Send: a broadcast Signal travels the DigitalBrainTimeline stream,
// the per-silo SignalEgressStreamSubscriber forwards it into SignalEgressBus, and a filtered subscription
// (the mechanism WatchSynapses streams to external transports) yields only the matching signal.
[Collection("signal-egress-host")]
public class WatchSynapsesTests : NeuronTestBase
{
    private readonly SignalEgressBus _egressBus = new();

    protected override void ConfigureSilo(ISiloBuilder builder) => builder
        .AddMemoryGrainStorageAsDefault()
        .AddMemoryStreams("Default")
        .AddMemoryStreams("HomeFeed")
        .AddMemoryStreams("DigitalBrainTimeline")
        .AddMemoryGrainStorage("PubSubStore")
        .ConfigureServices(services =>
        {
            services.AddKeyedScoped<IDurableList<Synapse>>("in-journal", (_, _) => new InMemoryDurableList<Synapse>());
            services.AddKeyedScoped<IDurableList<Synapse>>("out-journal", (_, _) => new InMemoryDurableList<Synapse>());
            services.AddScoped<NeuronJournals>();
            services.AddSingleton<IJournaledStateManager, TestJournaledStateManager>();
            services.AddSingleton<IPackEmbodiment, PackAlcEmbodier>();
            services.AddSingleton(_egressBus);
            services.AddSignalEgressStreamSubscriber();
        });

    [Fact]
    public async Task BroadcastSignal_ReachesEgressBus_FilteredByTypeName()
    {
        using var subscription = _egressBus.Subscribe(new[] { TelegramSignals.ReplyRequested });

        var emitter = Grain<IIngressNeuron>("egress-emitter-1");
        await emitter.IngestAsync(TelegramSignals.ReplyRequested,
            new Dictionary<string, object?> { ["chatId"] = 7L, ["text"] = "yo" });
        await emitter.IngestAsync("Other", new Dictionary<string, object?>());

        Signal? received = null;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        try
        {
            received = await subscription.Reader.ReadAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // received stays null -> assertion below fails with a clear message
        }

        Assert.NotNull(received);
        Assert.Equal(TelegramSignals.ReplyRequested, received!.Name);
        Assert.True(received.Props.TryGetValue("chatId", out var chatId), "Props should contain 'chatId'");
        Assert.True(chatId is 7 or 7L, $"chatId should be 7 (numeric), was {chatId} ({chatId?.GetType().Name})");
        Assert.Equal("yo", received.Props["text"]);

        // Drain any immediately available extras. The filter on the bus + the "Other" ingest prove
        // that non-matching signals are not delivered. Tolerate possible duplicate delivery of the
        // matching signal itself (stream provider / test cluster behavior) but never "Other".
        while (subscription.Reader.TryRead(out var extra))
        {
            Assert.Equal(TelegramSignals.ReplyRequested, extra.Name);
            Assert.NotEqual("Other", extra.Name);
        }
    }
}

[CollectionDefinition("signal-egress-host", DisableParallelization = true)]
public sealed class SignalEgressHostCollection;
```

- [ ] **Step 2: Replace `DigitalBrain.Tests/Gateway/PackConfigPullTests.cs` in full**

```csharp
using DigitalBrain.Core;
using DigitalBrain.Core.Config;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Config;
using DigitalBrain.Kernel.Foundry;
using DigitalBrain.Kernel.Gateway;
using DigitalBrain.Kernel.Ui;
using DigitalBrain.Runtime.Grpc;
using DigitalBrain.Tests.TestSupport;
using DigitalBrain.TestKit;
using Grpc.Core;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans.Journaling;

namespace DigitalBrain.Tests.Gateway;

// Task 7b store-and-PULL: a submitted config persists encrypted, GetPackConfig returns the DECRYPTED
// values point-to-point, and the broadcast carries only a NON-SECRET PackConfigured notification — the
// token never reaches the egress timeline. Mirrors WatchSynapsesTests' egress wiring.
[Collection("pack-config-pull-host")]
public class PackConfigPullTests : NeuronTestBase
{
    private readonly SignalEgressBus _egressBus = new();
    private readonly IPackConfigStore _configStore = BuildConfigStore();

    protected override void ConfigureSilo(ISiloBuilder builder) => builder
        .AddMemoryGrainStorageAsDefault()
        .AddMemoryStreams("Default")
        .AddMemoryStreams("HomeFeed")
        .AddMemoryStreams("DigitalBrainTimeline")
        .AddMemoryGrainStorage("PubSubStore")
        .ConfigureServices(services =>
        {
            services.AddKeyedScoped<IDurableList<Synapse>>("in-journal", (_, _) => new InMemoryDurableList<Synapse>());
            services.AddKeyedScoped<IDurableList<Synapse>>("out-journal", (_, _) => new InMemoryDurableList<Synapse>());
            services.AddScoped<NeuronJournals>();
            services.AddSingleton<IJournaledStateManager, TestJournaledStateManager>();
            services.AddSingleton<IPackEmbodiment, PackAlcEmbodier>();
            services.AddSingleton(_egressBus);
            services.AddSignalEgressStreamSubscriber();
        });

    private static IPackConfigStore BuildConfigStore()
    {
        var services = new ServiceCollection();
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        services.AddSingleton<IPackConfigBackingStore, InMemoryPackConfigBackingStore>();
        services.AddSingleton<IPackConfigStore, PackConfigStore>();
        return services.BuildServiceProvider().GetRequiredService<IPackConfigStore>();
    }

    private GatewayService NewService() =>
        new(Cluster.GrainFactory, new ConfigurationBuilder().Build(), new HomeFeedBus(),
            _egressBus, new FakeHostEnvironment(), NullLogger<GatewayService>.Instance, _configStore);

    // A production-equivalent service whose GetPackConfig gate is armed with a configured InternalServiceKey.
    private GatewayService NewGatedService(string internalKey) =>
        new(Cluster.GrainFactory,
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["DigitalBrain:InternalServiceKey"] = internalKey })
                .Build(),
            new HomeFeedBus(), _egressBus, new FakeHostEnvironment("Production"),
            NullLogger<GatewayService>.Instance, _configStore);

    private async Task StoreConfigAsync(GatewayService svc)
    {
        var payload = System.Text.Encoding.UTF8.GetBytes(
            "{\"pack\":\"TelegramResponderNeuron\",\"scope\":\"default\",\"telegram_token\":\"123:ABC\"}");
        await svc.Send(new SynapseEnvelope
        {
            TypeName = nameof(ConfigurationProvided),
            CorrelationId = "cfg-auth",
            Payload = global::Google.Protobuf.ByteString.CopyFrom(payload)
        }, TestServerCallContext.Create());
    }

    [Fact]
    public async Task GetPackConfig_ReturnsStoredValues_AfterConfigurationProvided()
    {
        var svc = NewService();
        var payload = System.Text.Encoding.UTF8.GetBytes(
            "{\"pack\":\"TelegramResponderNeuron\",\"scope\":\"default\",\"telegram_token\":\"123:ABC\",\"llm_provider\":\"ollama\"}");
        await svc.Send(new SynapseEnvelope
        {
            TypeName = nameof(ConfigurationProvided),
            CorrelationId = "cfg-1",
            Payload = global::Google.Protobuf.ByteString.CopyFrom(payload)
        }, TestServerCallContext.Create());

        var reply = await svc.GetPackConfig(
            new GetPackConfigRequest { Scope = "default", Pack = "TelegramResponderNeuron" },
            TestServerCallContext.Create());

        Assert.Equal("123:ABC", reply.Values["telegram_token"]);
        Assert.Equal("ollama", reply.Values["llm_provider"]);
    }

    // Regression: a form submitted with a sessionId (but no explicit scope) must still land under "default" —
    // the scope every reader (responder pack, LlmResponderNeuron, transport) actually pulls from. Deriving the
    // scope from sessionId would strand the token where no reader looks.
    [Fact]
    public async Task ConfigurationProvided_WithSessionIdButNoScope_StoredUnderDefaultScope()
    {
        var svc = NewService();
        var payload = System.Text.Encoding.UTF8.GetBytes(
            "{\"pack\":\"TelegramResponderNeuron\",\"sessionId\":\"user-session-42\",\"telegram_token\":\"123:ABC\"}");
        await svc.Send(new SynapseEnvelope
        {
            TypeName = nameof(ConfigurationProvided),
            CorrelationId = "cfg-session",
            Payload = global::Google.Protobuf.ByteString.CopyFrom(payload)
        }, TestServerCallContext.Create());

        var reply = await svc.GetPackConfig(
            new GetPackConfigRequest { Scope = "default", Pack = "TelegramResponderNeuron" },
            TestServerCallContext.Create());

        Assert.Equal("123:ABC", reply.Values["telegram_token"]);
        Assert.False(reply.Values.ContainsKey("sessionId"), "sessionId is a control key, not a stored config value.");
    }

    [Fact]
    public async Task ConfigurationProvided_BroadcastsNonSecretPackConfigured_WithoutToken()
    {
        using var subscription = _egressBus.Subscribe(new[] { "PackConfigured" });
        var svc = NewService();

        var payload = System.Text.Encoding.UTF8.GetBytes(
            "{\"pack\":\"TelegramResponderNeuron\",\"scope\":\"default\",\"telegram_token\":\"123:ABC\"}");
        await svc.Send(new SynapseEnvelope
        {
            TypeName = nameof(ConfigurationProvided),
            CorrelationId = "cfg-2",
            Payload = global::Google.Protobuf.ByteString.CopyFrom(payload)
        }, TestServerCallContext.Create());

        Signal? received = null;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        try { received = await subscription.Reader.ReadAsync(cts.Token); }
        catch (OperationCanceledException) { }

        Assert.NotNull(received);
        Assert.Equal("PackConfigured", received!.Name);
        Assert.Equal("TelegramResponderNeuron", received.Props["pack"]);
        Assert.Equal("default", received.Props["scope"]);
        Assert.False(received.Props.ContainsKey("telegram_token"),
            "The secret token must NEVER reach the broadcast egress.");
    }

    // SECURITY (the hole this task closes): GetPackConfig sits on the SAME external gRPC service a browser reaches.
    // A caller that does NOT present the shared internal service key (i.e. any untrusted internet/browser client)
    // must be REJECTED before any decrypted secret is returned.
    [Fact]
    public async Task GetPackConfig_WithoutInternalKey_IsRejectedUnauthenticated()
    {
        const string internalKey = "super-secret-internal-key";
        var svc = NewGatedService(internalKey);
        await StoreConfigAsync(svc);

        // No x-internal-key header — a browser/untrusted caller.
        var noHeaderEx = await Assert.ThrowsAsync<RpcException>(() => svc.GetPackConfig(
            new GetPackConfigRequest { Scope = "default", Pack = "TelegramResponderNeuron" },
            TestServerCallContext.Create()));
        Assert.Equal(StatusCode.Unauthenticated, noHeaderEx.StatusCode);

        // Wrong key — still rejected.
        var wrongKeyEx = await Assert.ThrowsAsync<RpcException>(() => svc.GetPackConfig(
            new GetPackConfigRequest { Scope = "default", Pack = "TelegramResponderNeuron" },
            TestServerCallContext.WithHeaders(("x-internal-key", "not-the-key"))));
        Assert.Equal(StatusCode.Unauthenticated, wrongKeyEx.StatusCode);
    }

    [Fact]
    public async Task GetPackConfig_WithCorrectInternalKey_ReturnsValues()
    {
        const string internalKey = "super-secret-internal-key";
        var svc = NewGatedService(internalKey);
        await StoreConfigAsync(svc);

        var reply = await svc.GetPackConfig(
            new GetPackConfigRequest { Scope = "default", Pack = "TelegramResponderNeuron" },
            TestServerCallContext.WithHeaders(("x-internal-key", internalKey)));

        Assert.Equal("123:ABC", reply.Values["telegram_token"]);
    }
}

[CollectionDefinition("pack-config-pull-host", DisableParallelization = true)]
public sealed class PackConfigPullHostCollection;
```

- [ ] **Step 3: Build**

Run: `dotnet build Brain.slnx`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 4: Run targeted tests**

Run: `dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "FullyQualifiedName~WatchSynapsesTests|FullyQualifiedName~PackConfigPullTests" --no-build --logger "console;verbosity=minimal"`
Expected: 6 passed (1 + 5), 0 failed.

- [ ] **Step 5: Commit**

```bash
git add DigitalBrain.Tests/Gateway/WatchSynapsesTests.cs DigitalBrain.Tests/Gateway/PackConfigPullTests.cs
git commit -m "test(cleanup): migrate WatchSynapsesTests/PackConfigPullTests to NeuronTestBase, drop SignalEgressBus static bridge"
```

---

### Task 7: Final whole-suite verification and branch review prep

**Files:** none (verification only)

- [ ] **Step 1: Confirm no manual boilerplate remains outside the deferred Group 4 / out-of-scope files**

Run: `grep -rn "TestClusterBuilder\| : IAsyncLifetime" DigitalBrain.Tests --include=*.cs`
Expected: only the deferred Group 4 files (`MarketplaceFilterRoundtripTests.cs`, `TrustedSeedInstallTests.cs`, `PublishGateTests.cs`, `BroadcastReactivityTests.cs`, `LlmResponderScopedConfigTests.cs`, `CatalogMaterializationTests.cs`, `HandlerGrowthTests.cs`, `PackBroadcastReactivityTests.cs`, `LlmResponderTests.cs`) and `GatewayGrpcWireTests.cs` (different pattern, explicitly out of scope) remain. No hits in any of the 10 files this plan touched.

- [ ] **Step 2: Full build**

Run: `dotnet build Brain.slnx`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Full DigitalBrain.Tests suite**

Run: `dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --no-build --logger "console;verbosity=minimal"`
Expected: 0 failed. Compare total pass count against the pre-slice baseline (recorded in Task 1 Step 4) — it should be unchanged (same tests, same assertions, just a different harness).

- [ ] **Step 4: Prepare diff range for reviewer**

Run: `git diff master...spec/group3-test-harness-dedup --stat`
Expected: `DigitalBrain.TestKit/{NeuronTestBase,TestDigitalBrain}.cs` + the 10 converted test files + 2 new docs files. No AppHost/hosting files — `aspire doctor` not required.

- [ ] **Step 5: Whole-branch review**

Fresh reviewer subagent reviews the full diff against this plan and the spec's guardrails: delete bias (net LOC), self-explanatory names, no vacuous comments, every `[Collection]` preserved, no behavior change, Context7/proven-API use documented in the spec. Target 0 critical/important findings; address any that surface with a fresh implementer pass, then re-verify (build + full suite).

- [ ] **Step 6: Finishing-a-development-branch**

Invoke the `finishing-a-development-branch` skill to decide merge/PR/cleanup for `spec/group3-test-harness-dedup`.

- [ ] **Step 7: Update ledger**

Append a round entry to `CONTINUITY.md` and `.superpowers/sdd/progress.md` summarizing: 10 files migrated, 3 additive `NeuronTestBase` hooks added, static bus bridges removed, Group 4 (9 files) and `UnitTest1.cs` domain-split explicitly deferred with reasons.
