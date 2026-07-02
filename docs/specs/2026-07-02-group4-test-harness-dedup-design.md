# Group 4 Test Harness Dedup — Design

**Date:** 2026-07-02
**Status:** Design, pending approval
**Branch:** `spec/group4-test-harness-dedup`
**Scope:** `DigitalBrain.Tests/` (9 files) — zero changes to `NeuronTestBase`/`TestDigitalBrain`.

## Context

Direct continuation of Group 3 (`docs/specs/2026-07-02-group3-test-harness-dedup-design.md`), which explicitly deferred these 9 files as "Group 4" because they build ad-hoc `TestClusterBuilder` per-test-method rather than at class level, and judged — without reading them — that this was "a different, larger pattern... not yet scoped."

Fresh research (this round) read all 9 files in full and found that judgment was too pessimistic: none of them actually need new `NeuronTestBase`/`TestDigitalBrain` capability. Every file's facts either:
- share one cluster config → migrate directly to `NeuronTestBase`, mechanical (same as Group 3's "mechanical" tier), or
- split across two genuinely different configs → resolved by moving the minority-config fact(s) into a **nested class** in the same file, a pattern already established in `UnitTest1.cs` (`IsolatedReplayTest`, `StrictConfigNeuronTest` — both nested `NeuronTestBase` subclasses with their own `ConfigureSilo` override), not by adding new hooks.

Two facts (`TrustedSeedInstallTests.Trusted_Publisher_Signs_Seeds_So_They_Verify`, `HandlerGrowthTests.Dev_Can_Package_And_Publish_Dummy_Distributions_Using_Seeds_Helpers`) build no `TestCluster` at all today — pure unit tests co-located with cluster tests in the same file. Forcing these onto `NeuronTestBase` would make them pay for an Orleans silo spin-up they never needed: a real regression, not a style choice. They move to their own nested plain class (no `NeuronTestBase`).

## Goals

- Migrate all 9 files off manual `TestClusterBuilder`/try-finally-`StopAllSilosAsync()` boilerplate.
- Reuse `NeuronTestBase` exactly as it exists today (`ConfigureSilo`, `InitialSilosCount`, `Cluster` — all already shipped in Group 3). Zero changes to `DigitalBrain.TestKit`.
- Where a file's facts use genuinely different cluster configs, split via nested class (matching the `UnitTest1.cs` precedent), not new base-class API.
- Where a fact builds no cluster today, extract it to a plain nested class with no `NeuronTestBase` — preserve its current (fast, cluster-free) execution cost.
- Preserve every `[Fact]`'s behavior and assertions exactly — pure harness refactor.
- Net delete boilerplate LOC.

## Non-goals

- Any change to `NeuronTestBase`/`TestDigitalBrain` (unlike Group 3, no new hooks needed here).
- `Gateway/GatewayGrpcWireTests.cs` — confirmed out of scope; uses `IClassFixture<WebApplicationFactory<Program>>`, not Orleans `TestCluster`.
- Renaming existing `[Fact]` methods or changing assertions.
- `UnitTest1.cs` domain split (separate, deferred item — not this slice).
- Closing out `core-bloat-delete-design.md` (separate, tiny, unrelated files — not this slice).

## Design

### Per-file conversion

**Tier 1 — direct migration, zero overrides** (single fact, single uniform config: `NeuronTestSiloConfigurator` only):
- `Ui/MarketplaceFilterRoundtripTests.cs`
- `Kernel/BroadcastReactivityTests.cs`
- `Distribution/CatalogMaterializationTests.cs`
- `Distribution/PackBroadcastReactivityTests.cs`

Each becomes `class X : NeuronTestBase` with no override; delete the `TestClusterBuilder`/`AddSiloBuilderConfigurator<NeuronTestSiloConfigurator>`/`DeployAsync`/try-finally-`StopAllSilosAsync` ceremony (~12 lines each); replace `cluster.GrainFactory.GetGrain<T>(key)` with the inherited `Grain<T>(key)`.

**Tier 2 — direct migration, one `ConfigureSilo` override** (single fact, one extra configurator beyond the base):
- `Kernel/LlmResponderTests.cs` → `ConfigureSilo` override applies `LlmResponderSiloConfigurator` (registers the fake `AnswerPrefixChatClient`).

**Tier 3 — nested-class split** (facts diverge into two genuinely different configs):

- `Trust/PublishGateTests.cs`: outer class keeps `Gate_on_admits_a_trusted_publisher` + `Gate_on_rejects_a_stranger` (both use the gated config) with a `ConfigureSilo` override inlining today's `GatedSiloConfigurator` (`RejectUnsignedPacks=false`, `GatePublishing=true`); a nested class (no override — base `NeuronTestSiloConfigurator` only) holds `Gate_off_by_default_admits_unsigned`. The `ListedAsync(IMarketplaceNeuron)` helper stays as a shared `private static` method in the outer class, reused by both. `GatedCluster()` is deleted — `ConfigureSilo` does its job.

- `Trust/TrustedSeedInstallTests.cs`: outer class keeps `Under_Strict_Default_Signed_Seed_Installs_But_Unsigned_Is_Rejected` with a `ConfigureSilo` override inlining today's `StrictConfigurator` (`RejectUnsignedPacks=true`); a nested **plain** class (no `NeuronTestBase`) holds `Trusted_Publisher_Signs_Seeds_So_They_Verify`, which only calls `MarketplaceSeeds`/`PackSignatureVerifier` statics and never touches a cluster today.

- `Distribution/HandlerGrowthTests.cs`: outer class keeps `Installing_A_Pack_Adds_Exactly_One_Responder_To_A_Previously_Unhandled_Synapse` (uses the cluster) with no override; a nested **plain** class (no `NeuronTestBase`) holds `Dev_Can_Package_And_Publish_Dummy_Distributions_Using_Seeds_Helpers`, which only calls `MarketplaceSeeds.KernelPublishCommand` and never touches a cluster today.

- `Kernel/LlmResponderScopedConfigTests.cs`: outer class keeps `AskLlm_with_ConfigPack_uses_scoped_client_from_stored_provider_and_key` + `AskLlm_without_ConfigPack_uses_global_client` (both use `ScopedLlmResponderSiloConfigurator`/`RecordingScopedChatClientFactory`) with a `ConfigureSilo` override; a nested class holds `AskLlm_scoped_factory_returns_null_falls_back_to_global_client`, with its own `ConfigureSilo` override applying `NullScopedLlmResponderSiloConfigurator`/`NullScopedChatClientFactory`. Both classes inherit the default `InitialSilosCount => 1` (already matches today's explicit `initialSilosCount: 1`, no override needed). The existing static `Factory` fields on the configurators and the per-fact clear/set calls are unaffected — they're test-local state, not cluster-config concerns.

Exact nested-class names are picked during implementation (self-explanatory naming, checked in review) — not pinned here.

### Verification ritual (after every file, and again at the end)

`dotnet build` → `dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "<touched-area>" --no-build --logger "console;verbosity=minimal"`. `aspire doctor` not required (no AppHost/hosting files touched).

## Risks

Low. Every conversion in this slice is either identical in shape to Group 3's already-proven "mechanical" and "`ConfigureSilo` override" tiers, or a straight application of the `UnitTest1.cs` nested-class precedent — no new `NeuronTestBase` surface, no new Orleans registration-ordering question (Group 3 already spiked and proved that layering a per-file `ConfigureSilo` on top of the always-applied `NeuronTestSiloConfigurator` is safe). The only mechanical risk is per-file: a missed `using`, or moving a fact into the wrong class. Caught by running the full `DigitalBrain.Tests` suite at the end, not just touched-area filters.

## Suggested sequencing

1. Convert the 4 Tier 1 files (validates nothing new — matches Group 3's mechanical tier exactly).
2. Convert `LlmResponderTests.cs` (Tier 2, single override — matches Group 3's override tier).
3. Convert `PublishGateTests.cs` (first nested-class split — establishes the pattern).
4. Convert `TrustedSeedInstallTests.cs` and `HandlerGrowthTests.cs` (nested plain-class-for-unit-test pattern).
5. Convert `LlmResponderScopedConfigTests.cs` (two `ConfigureSilo`-bearing classes in one file — most complex, done last with the pattern already proven).
6. Full-branch review + final verification ritual (whole `DigitalBrain.Tests` suite).
