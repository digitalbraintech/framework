# Redundant Marker Trim + UnitTest1 Inners Implementation Plan

> Use subagent-driven-development. Checkbox tasks. Fresh implementer + reviewer per logical chunk. Final whole-branch review.

**Goal:** Delete redundant base marker interfaces (IAspire/IMarketplace and their *Neuron aliases) from Core protocol. Clean the two remaining inner manual TestClusterBuilder blocks in UnitTest1.cs. Delete bias, self-explanatory, minimal.

## Global Constraints
- Delete > add.
- Relative paths only.
- Targeted `dotnet build` + `dotnet test --filter "..."` after every change.
- Context7 for any Orleans test cluster / grain / stream API before edits.
- Self-explanatory names; no vacuous comments.
- Branch: spec/marker-trim-unit1-clean (current).
- No edits to .superpowers/sdd during impl.
- Update CONTINUITY at end.

### Task 1: Audit and plan exact deletes (read + grep)

- [ ] Read `DigitalBrain.Core/Synapse.cs` sections for IAspire, IMarketplace, their *Neuron forms.
- [ ] Grep whole tree for bare `IAspire[^N]`, `IMarketplace[^N]`, `GetGrain<IAspire>`, `GetGrain<IMarketplace>`.
- [ ] Read the two inner manual blocks in `DigitalBrain.Tests/UnitTest1.cs` (SystemStatus_Simulates... and the strict config part).
- [ ] Confirm usages of IAspireNeuron and IMarketplaceNeuron (keep the *Neuron forms).
- [ ] Run baseline build + targeted test (UnitTest1 + SystemStatus + Marketplace).
- [ ] Document the exact strings to delete/replace.

### Task 2: Trim redundant bases in Core/Synapse.cs

- [ ] Context7 (if needed for serialization, but interfaces only — skip heavy if no new API).
- [ ] Delete the `public interface IAspire ...` and `IAspireNeuron : IAspire`.
- [ ] Delete `public interface IMarketplace ...` and `IMarketplaceNeuron : IMarketplace`.
- [ ] Update IAspireNeuron and IMarketplaceNeuron (if kept) to directly carry the IHandle declarations so they remain complete contracts.
- [ ] Or simplest: since *Neuron are used everywhere, just remove the two base interface definitions and the two alias lines. Update the one impl site if it referenced the base (SystemNeurons uses IAspireNeuron — will stay).
- [ ] Build + test filter "NeuronCore|Marketplace".
- [ ] Commit "cleanup(core): delete redundant IAspire/IMarketplace base aliases (only *Neuron forms used)".

### Task 3: Clean inner manual builders in UnitTest1.cs

- [ ] Read full relevant methods in UnitTest1.cs.
- [ ] For the sim replay block: replace manual TestClusterBuilder + Deploy with a helper or use the base class pattern (e.g. subclass NeuronTestBase for the test and use Grain with different key; or use checkpoint restore on same grain if semantics allow). Keep the assertions.
- [ ] For the StrictMarketplaceTrustSiloConfigurator block: move the override logic into a protected override void ConfigureSilo in a local test subclass of NeuronTestBase, or keep minimal but remove the raw builder code.
- [ ] Delete unused using for TestCluster if possible.
- [ ] After edit: build + test "UnitTest1|SystemStatus|Marketplace".
- [ ] Commit "test: remove remaining inner manual TestClusterBuilder in UnitTest1 (use harness)".

### Task 4: Final verification + docs

- [ ] Grep for any remaining references to deleted aliases.
- [ ] Broad relevant filters (Core + UnitTest1 areas) + build.
- [ ] Update CONTINUITY.md with one-line.
- [ ] Prepare diff.
- [ ] Fresh reviewer subagent on whole branch vs this plan.
- [ ] Address findings.
- [ ] Finishing-a-development-branch.

This slice is pure delete + small hygiene, high purity impact for Core, completes the UnitTest1 conversion started in prior slice. All rituals after every edit.