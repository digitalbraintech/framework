# Test Boilerplate Deduplication and UnitTest1 Cleanup — Design

**Date:** 2026-07-02
**Status:** Design, pending implementation plan
**Scope:** Primarily `brain/DigitalBrain.Tests/` (the monolith test project) + related Steps. Leverage existing `DigitalBrain.TestKit.NeuronTestBase`.

## Context

This continues the cleanup (root 30-step plan, prior specs/plans, Musk 5-step, subagent research).

**What's already done (no duplication):**
- Ino isolation complete.
- NeuronTestBase + migration of 14+ files in dedicated *.Tests projects (Context, Developer, Google, Telegram.Channel, UiKit, Windows) complete (base in TestKit).
- Fix pre-existing (duplicate PerformKernelSelfUpdate, Software10 demo deletes) complete.
- SystemNeurons bloat delete complete (implementer subagent: ~251 LOC demo emission stripped from SystemNeurons.cs; ownership moved to neurons via synapses/buses; rituals green).
- Core demo literal removal advanced (HelloWorld/SimpleColorPicker/UiGallery/ExcelViz/Dummy pack codes + Hops + entries removed from MarketplaceSeeds; hello special cases from UiSurfaces; dead E2E pack sources + AwesomeSoftware10.feature/cs + demo test files + ref cleanups; builds/tests green per merge c079798).
- Prior: task renaming (no more KernelTask* in Core), signals work, etc.

**Musk step 1 (requirements less dumb) incorporated from brainstorm:**
- Assumption "all remaining IAsyncLifetime+TestClusterBuilder in DigitalBrain.Tests/ is dumb dupe to blindly nuke" is valid here — mechanical, proven by NeuronTestBase on other projects; no new Orleans APIs invented (use existing base wrapper).
- IDemoNeuron usage in tests is practical fallback (many impls of INeuron; Mcp.Tools refs only Core requires typed markers for resolver). Not "purity violation" to remove in this slice.
- UnitTest1.cs grab-bag name + mixed concerns is dumb (trace: legacy catch-all before domain folders).
- No change to Core/Kernel production; delete boiler only. Trace to real: faster iteration, consistency, "independently testable" per SDD.

**Latest research (Tests subagent + Core + current tree on branch):**
- Main `DigitalBrain.Tests/` still has heavy manual boilerplate (IAsyncLifetime + TestClusterBuilder + NeuronTestSiloConfigurator) in UnitTest1.cs (grab-bag) and files: Auth/UserSessionNeuronTests, Awesome/SoftwareEngineeringReviewerTests, Company/CompanyKnowledgeTests, Context/ContextRecallTests, Gateway/*Tests, Kernel/*Tests (TimelineStream, ExperienceStepDispatch, Rolling...), Mcp/DigitalBrainToolsTests, Telegram/TelegramDeepLinkRoutingTests, Ui/ChatNeuronTests, Economics/License..., Steps/*, plus E2E fixture (may stay).
- Dedicated projects are clean (inherit base).
- UnitTest1 is the legacy catch-all.
- Good assertions but dupe setup hurts maintainability and "independently testable" vision.
- Thin coverage noted (Ino, DbSupport orphan) — not addressed in this mechanical slice.

This is the explicit next phase from the NeuronTestBase design ("grab-bag... split into 9 domain files").

## Goals

- Migrate remaining manual cluster setup in DigitalBrain.Tests/ to use NeuronTestBase (or TestDigitalBrain directly for non-grain).
- Extract/split UnitTest1.cs contents into focused files under existing folders (Kernel/, Distribution/, Ui/, etc.) or delete pure duplication.
- Eliminate dupe boilerplate (hundreds of lines).
- Keep behavior/assertions identical.
- Delete bias where possible.

## Non-goals

- Full rewrite of E2E/Reqnroll (focus on xUnit grain tests).
- Adding new coverage (that's separate).
- Touching Core/Kernel production code beyond minimal.

## Design

- Inventory remaining manual files (grep for `TestClusterBuilder| : IAsyncLifetime` in DigitalBrain.Tests/**/*.cs + Steps).
- For each non-grab-bag: read exact, inherit `NeuronTestBase` (from DigitalBrain.TestKit), replace manual `_cluster = new TestClusterBuilder()...; await Deploy...; _cluster.GrainFactory.GetGrain<IFoo>(k)` with `Grain<IFoo>(k)` + `await FireAsync(...)` or `DeliverAsync(...)` (see NeuronTestBase + migrated examples in Context.Tests etc.). Remove InitializeAsync/DisposeAsync boiler. Self-explanatory class names.
- For UnitTest1.cs (grab-bag ~600 LOC mixing core activation/journal/fire/branch/restore/embody + IDemoNeuron): extract to focused e.g. `Kernel/NeuronCoreTests.cs`, `Distribution/MarketplaceCoreTests.cs`, `Ui/UiSurfaceCoreTests.cs` etc under existing folders (self-explanatory). Delete or empty the UnitTest1.cs file post-extract (misnomer name).
- No new abstractions. Use existing TestDigitalBrain/NeuronTestSiloConfigurator (AsyncLocal bridge for configurator extensions is established).
- Context7 used (resolve + query for /dotnet/orleans testing patterns) before migration edits; patterns match repo's proven base (no direct TestClusterBuilder edits in prod paths).
- Delete bias: remove 100-200+ LOC boiler across files; no net feature add.

Verification ritual after EVERY edit (and per task/chunk): `dotnet build` (from brain/) → `dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "<target e.g. NeuronCore|Marketplace|Ui|Context|Auth>" --no-build -l minimal` (high-severity relevant). aspire doctor ONLY if AppHost/hosting touched (not this slice). Update plan checkboxes + CONTINUITY at end. Final whole-branch review + finishing-a-development-branch.

Risks: Low (mechanical; base proven; all prior migrations green). Some files (E2E fixtures, Reqnroll Steps) may stay manual if they need custom config or collection fixtures — audit per inventory.

## Risks

Low (mechanical lift-and-shift; tests already green; base proven on other projects).

High value for maintainability and the "independently testable" goal.

This is the logical next after the base extraction and the bloat deletes.