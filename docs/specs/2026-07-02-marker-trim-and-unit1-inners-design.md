# Redundant Marker Interfaces Trim + UnitTest1 Inner Cleanup — Design

**Date:** 2026-07-02
**Status:** Design, ready for plan
**Scope:** `brain/DigitalBrain.Core/Synapse.cs` (trim redundant base interfaces) + `brain/DigitalBrain.Tests/UnitTest1.cs` (clean remaining inner manual TestClusterBuilder blocks). Small, delete-biased.

## Context

Continues the cleanup initiative after test-boilerplate merge to master. Previous work converted the main grab-bag class to NeuronTestBase but left inner manual builders and the full set of marker interfaces in Core.

**What's already done:**
- Most simple IAsyncLifetime cases in main DigitalBrain.Tests/ migrated or noted as strict (Gateway collections, custom configs, Steps).
- UnitTest1 class now inherits NeuronTestBase; many GetGrain calls use Grain<T>.
- SystemNeurons bloat, Core demo literals, etc. cleaned in prior rounds.

**Musk step 1 (requirements less dumb) from brainstorm verification:**
- "Keep all marker interfaces forever": partially dumb. IAspire / IAspireNeuron and IMarketplace / IMarketplaceNeuron are pure aliases (no direct impls or GetGrain<IAspire> outside definitions). Nothing implements the bare IAspire directly anymore. IChannelNeuron is documented thin marker for cross-channel (used as base by ITelegramChatNeuron).
- "Inner manual builders in UnitTest1 are necessary forever": dumb for the isolated sim replay (can be covered by existing checkpoint/restore tests or base usage); the strict config one may stay or be extracted.
- Trace to real: Core should be minimal protocol (INeuron + Synapse + IHandle). Test code should use the harness. Redundant aliases add noise without value. Inner builders duplicate the boiler we just deleted at class level.
- No impact on Mcp.Tools (uses *Neuron forms which stay) or resolver (uses *Neuron for GetGrain).
- Purity: keep I*Neuron as the practical contracts where needed; delete pure inheritance duplicates.

## Goals
- Delete the two redundant base interfaces (IAspire, IMarketplace) and their *Neuron aliases if they add no value (update the *Neuron interfaces to carry the IHandle declarations directly if needed, or keep *Neuron and remove bases).
- Clean the two inner manual TestClusterBuilder usages in UnitTest1.cs (the sim replay and strict trust one) — prefer using existing harness or delete if redundant with other tests.
- Net delete. Self-explanatory names. No new abstractions.
- Keep all observable behavior for the tests.

## Non-goals
- Full removal of useful markers (IDemoNeuron, IDbSupportNeuron, IGeneratedNeuron, etc.).
- Touching strict Gateway/Steps tests.
- Big refactor of resolver or grains.

## Design (evidence from reads/greps)
- `DigitalBrain.Core/Synapse.cs:62-70`: IAspire + IAspireNeuron : IAspire; IMarketplace + IMarketplaceNeuron : IMarketplace. Grep showed zero external uses of bare IAspire/IMarketplace (only the *Neuron forms and definitions).
- `DigitalBrain.Kernel/SystemNeurons.cs:17`: AspireOrchestratorNeuron : ... , IAspireNeuron , ...
- CLI, GatewayService, UiGatewayService use IMarketplaceNeuron.
- `DigitalBrain.Tests/UnitTest1.cs:55` and ~350 area: manual simBuilder and StrictMarketplaceTrustSiloConfigurator using TestClusterBuilder + NeuronTestSiloConfigurator (after class migration).
- Clean: Delete IAspire and IMarketplace entirely. Make IAspireNeuron and IMarketplaceNeuron directly declare the IHandle contracts (or leave as-is if *Neuron already sufficient; simplest delete bases and adjust the one impl if needed to IAspireNeuron directly inheriting the handles? Wait, move the handles to *Neuron).
  Better: since *Neuron are the used ones, delete the base IAspire/IMarketplace lines, and have IAspireNeuron declare the handles inline (copy the list), same for IMarketplaceNeuron. This removes the inheritance alias.
- For UnitTest1: replace the inner sim cluster code with usage of the base class's Grain or a second activation with different key + checkpoint, or remove the isolated part if the main checkpoint test already covers (but keep semantics). For the strict one, move the config to a helper or use ConfigureSilo override on a subclass of NeuronTestBase.
- Delete more: any comments about the aliases.

## Verification ritual (after every edit)
- `dotnet build`
- `dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "UnitTest1|NeuronCore|Marketplace|SystemStatus" --no-build`
- If Core interface change touches grains: also relevant filters.
- No AppHost change expected → no aspire doctor.
- Full relevant at end of slice.

## Risks
Low. Alias removal is mechanical (grep confirmed no bare IAspire usage). Inner test cleanup keeps the assertions.

This is classic delete step + purity for Core + test harness consistency. Small focused slice.