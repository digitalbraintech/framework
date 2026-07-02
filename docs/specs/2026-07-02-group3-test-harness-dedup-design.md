# Group 3 Test Harness Dedup — Design

**Date:** 2026-07-02
**Status:** Design, approved, implementation starting
**Branch:** `spec/group3-test-harness-dedup`
**Scope:** `DigitalBrain.Tests/` (10 files) + `DigitalBrain.TestKit/NeuronTestBase.cs` + `TestDigitalBrain.cs` (3 small additive hooks: `ConfigureClient`, `InitialSilosCount`, `Cluster`)

## Context

This is the direct continuation of `docs/plans/2026-07-02-test-boilerplate-deduplication.md`, whose Task 2 explicitly deferred "Group 3" (files using custom silo/client configs, xunit collections, or extra buses — judged "not mechanical" at the time) and whose Task 3 (splitting `UnitTest1.cs`) was left undone. Group 1 and Group 2 (8 files: `ContextRecallTests`, `UserSessionNeuronTests`, `CompanyKnowledgeTests`, `DigitalBrainToolsTests`, `SoftwareEngineeringReviewerTests`, `ChatNeuronTests`, `DbSupportNeuronTests`, plus `UnitTest1.cs`'s base-class conversion) are already migrated to `NeuronTestBase` and merged to master.

Fresh research (this round) re-examined the deferred files and found the "not mechanical" judgment was too pessimistic for most of them:

- `TestDigitalBrain.cs:18` already passes `ConfigureSilo` as an **instance-bound delegate** (`new TestDigitalBrain(ConfigureSilo)`), bridged into Orleans' reflection-based `ISiloConfigurator` activation via an `AsyncLocal` (`TestDigitalBrain.cs:20-26`). This means a `NeuronTestBase` subclass's `ConfigureSilo` override already closes over `this` and can capture instance fields directly (e.g., a bus). The `[Class]Config.SharedXBus` **static field bridge** pattern used by the 5 shared-bus files below exists only because Orleans' `AddSiloBuilderConfigurator<T>()` needs a separate, non-`NeuronTestBase`, parameterless `ISiloConfigurator` type — it is not needed once those files migrate to `NeuronTestBase`. Converting these files is a **net simplification**, not just a boilerplate move.
- Two files need real (small) additions to `NeuronTestBase`/`TestDigitalBrain`, because they use Orleans `TestClusterBuilder` capabilities that base class doesn't expose today:
  - `Kernel/TimelineStreamTests.cs:15-19` registers a client-side stream provider via `builder.AddClientBuilderConfigurator<TimelineClientConfigurator>()` (`IClientBuilderConfigurator.Configure(IConfiguration, IClientBuilder)` — confirmed working API, already compiling in this exact file today).
  - `Ui/HomeFeedCrossSiloTests.cs:15` needs `new TestClusterBuilder(initialSilosCount: 2)` — a 2-silo cluster — to test cross-silo broadcast; `NeuronTestBase` always builds exactly 1 silo.
- `Kernel/RollingUpdateRollbackTests.cs` was named in the original Group 3 audit list but wasn't in the newest 9-file inventory — it turns out to build a throwaway `TestClusterBuilder` inside its single `[Fact]` (no class-level `IAsyncLifetime` at all). It converts identically to the others (drop-in `NeuronTestBase` inheritance) and is folded into this slice as a 10th file.
- A **broader** set of ~9 files (`Ui/MarketplaceFilterRoundtripTests.cs`, `Trust/TrustedSeedInstallTests.cs`, `Trust/PublishGateTests.cs`, `Kernel/BroadcastReactivityTests.cs`, `Kernel/LlmResponderScopedConfigTests.cs`, `Distribution/CatalogMaterializationTests.cs`, `Distribution/HandlerGrowthTests.cs`, `Distribution/PackBroadcastReactivityTests.cs`, `Kernel/LlmResponderTests.cs`) also build ad-hoc `TestClusterBuilder` instances per-test-method. These are explicitly **out of scope** here (see Non-goals) — including them would roughly double this slice's size and risk. They're a good "Group 4" candidate for a future round.

## Goals

- Migrate the 10 remaining `IAsyncLifetime`/manual-`TestClusterBuilder` test files in `DigitalBrain.Tests/` to inherit `NeuronTestBase`, deleting the duplicated init/dispose ceremony and (where applicable) the static-field bus bridge.
- Add exactly three small, additive, backward-compatible members to `NeuronTestBase`/`TestDigitalBrain` to unblock the files that need real new capability: a `ConfigureClient(IClientBuilder)` override point, an `InitialSilosCount` override point (default `1`), and a `Cluster` accessor exposing the underlying `TestCluster` for the handful of tests that need raw `GrainFactory`/`Client`/`Silos` access. All three preserve existing behavior for every currently-migrated subclass.
- Preserve every xunit `[Collection(...)]` attribute and `DisableParallelization` semantics exactly as-is (these guard against local Orleans TestCluster port/resource contention, not against the state-sharing problem being deleted here — out of scope to relitigate).
- Net delete boilerplate LOC.

## Non-goals

- The ~9 per-method `TestClusterBuilder` files listed above (deferred "Group 4").
- `Gateway/GatewayGrpcWireTests.cs` — uses `IClassFixture<WebApplicationFactory<Program>>`, a full ASP.NET host integration test, not an Orleans `TestCluster`. Fundamentally different pattern; stays as-is.
- Any change to test assertions/behavior. This is a pure harness refactor — same coverage, same outcomes.
- Splitting `UnitTest1.cs` into domain files (separate, smaller follow-up; not blocking this slice).
- `.superpowers/sdd/` edits during implementation (ledger updated at round end per guardrails).

## Design

### NeuronTestBase / TestDigitalBrain additions

In `DigitalBrain.TestKit/TestDigitalBrain.cs`, extend the constructor to also accept an optional client-builder delegate and an initial silo count, bridged the same way `ConfigureSilo` already is (`AsyncLocal`, since Orleans reflectively activates `IClientBuilderConfigurator` too):

```csharp
public sealed class TestDigitalBrain(
    Action<ISiloBuilder>? extendSilo = null,
    Action<IClientBuilder>? extendClient = null,
    int initialSilosCount = 1) : IDigitalBrain, IAsyncLifetime
```

`InitializeAsync` passes `initialSilosCount` to `new TestClusterBuilder(initialSilosCount: initialSilosCount)`, and — only when `extendClient is not null` — adds a second `ExtendClientBuilderConfigurator` (mirroring the existing `ExtendSiloConfigurator` pattern) via its own `AsyncLocal<Action<IClientBuilder>?>`.

In `DigitalBrain.TestKit/NeuronTestBase.cs`, add two virtual members alongside the existing `ConfigureSilo`:

```csharp
protected virtual void ConfigureClient(IClientBuilder builder) { }
protected virtual int InitialSilosCount => 1;
```

`InitializeAsync` constructs `TestDigitalBrain` passing all three. Both new members default to no-ops/`1`, so every currently-migrated subclass (Group 1/2) is unaffected.

Reading the actual files surfaced two more gaps, both solved by the same fix: `GatewayServiceTests.cs`, `GenericSendTests.cs`, `PackConfigPullTests.cs`, and `TelegramDeepLinkRoutingTests.cs` all construct `GatewayService` directly, whose constructor (`DigitalBrain.Kernel/Gateway/GatewayService.cs:9-10`) takes a raw `IGrainFactory` as its first parameter; and `TimelineStreamTests.cs` calls `_cluster.Client.GetStreamProvider(...)`, needing the raw `IClusterClient`. Rather than add narrow `GrainFactory`/`Client`/`Silos` accessors piecemeal, expose the underlying cluster itself — one addition covers all three (plus `HomeFeedCrossSiloTests.cs`'s `Silos` need below):

```csharp
protected TestCluster Cluster => _brain.Cluster; // NeuronTestBase
public TestCluster Cluster => _cluster!;         // TestDigitalBrain
```

`NeuronTestBase.cs` needs a new `using Orleans.TestingHost;` for the `TestCluster` type (it currently has none, relying entirely on the Orleans-SDK-injected global usings for `ISiloBuilder`/`ISiloConfigurator`; `TestCluster`/`SiloHandle`/`InProcessSiloHandle` are not part of that global set — confirmed by every file that uses them today carrying an explicit `using Orleans.TestingHost;`).

### Per-file conversion

**Mechanical, no override needed** — inherit `NeuronTestBase`, delete `_cluster`/`InitializeAsync`/`DisposeAsync`, replace `_cluster.GrainFactory.GetGrain<T>(key)` with the inherited `Grain<T>(key)`:
- `Economics/LicenseAndEntitlementTests.cs` (already uses plain `NeuronTestSiloConfigurator`)
- `Telegram/TelegramDeepLinkRoutingTests.cs` (plain configurator; keep its `[Collection("tg-routing-host")]`)
- `Kernel/RollingUpdateRollbackTests.cs` (single-fact, per-method builder → drop-in base-class usage)

**`ConfigureSilo` override, static bridge removed** — capture the bus in an instance field set inside the override itself (no more `static SharedXBus` field, no more separate `[X]SiloConfig : ISiloConfigurator` class):
- `Gateway/GatewayServiceTests.cs` (`HomeFeedBus`, keep `[Collection("silo-host")]`)
- `Gateway/WatchSynapsesTests.cs` (`SignalEgressBus`, keep `[Collection("signal-egress-host")]`)
- `Gateway/GenericSendTests.cs` (`HomeFeedBus` + the `SignalSinkGrain` test helper, keep `[Collection("signal-sink-host")]`)
- `Kernel/ExperienceStepDispatchTests.cs` (`HomeFeedBus` + custom `IConfiguration` with `RejectUnsignedPacks: false`, keep `[Collection("silo-host")]`)
- `Gateway/PackConfigPullTests.cs` (`SignalEgressBus` via override; its independent `IPackConfigStore` built from a fresh `ServiceCollection` is already test-local and needs no change — keep `[Collection("pack-config-pull-host")]`)

**New hook usage:**
- `Kernel/TimelineStreamTests.cs` → `ConfigureClient` override registering `builder.AddMemoryStreams(SynapseStream.ProviderName)`; use `Cluster.Client.GetStreamProvider(...)` in place of `_cluster.Client...`.
- `Ui/HomeFeedCrossSiloTests.cs` → `InitialSilosCount => 2` override; keep its direct silo-indexing access pattern via `Cluster.Silos[0]` / `Cluster.Silos[1]` in place of `_cluster.Silos[...]`.
- `Gateway/GatewayServiceTests.cs`, `Gateway/GenericSendTests.cs`, `Gateway/PackConfigPullTests.cs`, `Telegram/TelegramDeepLinkRoutingTests.cs` → construct `GatewayService` with `Cluster.GrainFactory` in place of `_cluster.GrainFactory`.

### Verification ritual (after every file, and again at the end)

`dotnet build` → `dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "<touched-area>" --no-build -l minimal`. `aspire doctor` not required (no AppHost/hosting files touched).

## Risks

Low-medium. Mechanical per-file conversions carry standard refactor risk (missed `using`, forgotten `[Collection]` attribute). The two `NeuronTestBase` additions are additive and don't change any existing subclass's behavior — verified by running the full `DigitalBrain.Tests` suite, not just the touched files, at the end.

## Suggested sequencing

1. Extend `NeuronTestBase`/`TestDigitalBrain` with the three additions (`ConfigureClient`, `InitialSilosCount`, `Cluster`); verify no regression across the whole suite.
2. Convert the 3 fully mechanical files.
3. Convert `HomeFeedCrossSiloTests.cs` (validates the silo-count hook).
4. Convert `TimelineStreamTests.cs` (validates the client hook).
5. Convert the 5 shared-bus files (validates static-bridge removal).
6. Full-branch review + final verification ritual (whole `DigitalBrain.Tests` suite).
