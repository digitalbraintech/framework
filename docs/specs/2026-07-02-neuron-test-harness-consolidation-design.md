# Neuron Test Harness Consolidation — Design

**Date:** 2026-07-02
**Status:** Design, pending implementation plan
**Scope:** `brain/` only — `DigitalBrain.TestKit`, the 8 per-integration ino `.Tests` projects, `DigitalBrain.Tests`

## Context

This is the first sub-project of a larger cleanup/refactoring/simplification initiative for `brain/` and
`app/` (see `docs/plans/2026-07-01-cleanup-refactoring-simplification-30-steps-neuron-synapse-proof-plan.md`,
`docs/SYSTEM_DESIGN.md`, and `core-requirements/Musk approach.txt`). A survey of the archived `Projects/IAW`
tree (a prior iteration, not to be extended directly) showed a pattern worth harvesting: every marketplace
integration ("agent") lived in its own folder with a zero-boilerplate `AgentTest<TAgent>` test base, so each
integration was independently, cheaply testable in isolation.

A follow-up survey of brain's actual current state found the gap is narrower than first assumed — and a
second, direct read of every ino `.Tests` directory (correcting an incomplete first pass that missed several
files) found it narrower still. Brain already has a working per-ino test harness — `DigitalBrain.TestKit`'s
`TestDigitalBrain` facade wraps a real Orleans `TestCluster` via the shared `NeuronTestSiloConfigurator` —
and coverage across the 8 ino projects is already comprehensive: `Windows.Tests` covers FileSystem, Shell,
*and* Winget; `Developer.Tests` covers Git, Roslyn, DotNet, *and* NuGet; `Google.Tests` covers Gmail, Drive,
Calendar, *and* Auth. There is no coverage gap to fill. The actual gaps versus IAW are narrower:

1. **Boilerplate** — every one of the 14 grain-test classes across the 8 ino projects hand-rolls
   `IAsyncLifetime` + `new TestDigitalBrain(...)` instead of inheriting one reusable base.
2. **A grab-bag central file** — `DigitalBrain.Tests/UnitTest1.cs` is a 601-line, ~25-scenario class mixing
   seven unrelated concerns, despite the rest of `DigitalBrain.Tests` already being organized by concern
   folder (`Kernel/`, `Trust/`, `Distribution/`, `Context/`, `Ui/`, `Sdk/`, ...).
3. **Unknown test-quality debt** — no audit has been run for assertion-free/tautological/duplicate tests
   across the ~130 test files in scope.

## Goals

- Close gaps 1–4 above so every marketplace integration is tested the way IAW's agents were: in isolation,
  cheaply, with a template that makes adding the *next* integration's tests (a future Notion/Slack/etc. ino)
  fast to start.
- Keep tests fast: in-memory `TestCluster` only, no new real infrastructure, no shared-cluster/collection-
  fixture complexity that isn't needed for correctness.
- Delete more than we add: the central suite should end up easier to navigate, not just reorganized.

## Non-goals

- Static-virtual `IAgent`-style self-describing metadata (`AgentDisplayName`/`Capabilities`/`Instructions`)
  — that's about LLM/MCP tool discovery, a different concern the user did not ask for here.
- `app/`'s Flutter test suite (20 files) — separate future sub-project.
- A shared/warm `TestCluster` across test classes — in-memory cluster boot is already fast; sharing state
  across test classes trades a small speed win for cross-test pollution risk that isn't justified here.

## Design

### 1. `NeuronTestBase` (new, in `DigitalBrain.TestKit`)

An abstract class implementing `IAsyncLifetime`, wrapping the existing `TestDigitalBrain` facade so every
grain-test class stops re-implementing the same lifecycle:

```csharp
public abstract class NeuronTestBase : IAsyncLifetime
{
    TestDigitalBrain _brain = null!;

    protected virtual void ConfigureSilo(ISiloBuilder builder) { }

    protected TGrain Grain<TGrain>(string key) where TGrain : IGrainWithStringKey => _brain.Grain<TGrain>(key);
    protected Task FireAsync<T>(T synapse) where T : Synapse => _brain.FireAsync(synapse);
    protected Task DeliverAsync<T>(T synapse) where T : Synapse => _brain.DeliverAsync(synapse);

    public Task InitializeAsync() { _brain = new TestDigitalBrain(ConfigureSilo); return _brain.InitializeAsync(); }
    public Task DisposeAsync() => _brain.DisposeAsync();
}
```

`TestDigitalBrain`/`IDigitalBrain`/`NeuronTestSiloConfigurator` are unchanged — this only removes duplicated
call sites. `TestKit.Tests` gets a new test proving `NeuronTestBase` itself works, mirroring the existing
`TestDigitalBrainTests` (using `IDemoNeuron`, the one-implementor leaf interface already used to avoid grain
resolution ambiguity).

### 2. Per-ino migration table (14 files, all mechanical lift-and-shift, no new scenarios)

| Project | File | Migrate to `NeuronTestBase`? |
|---|---|---|
| `Context.Tests` | `ContextNeuronTests.cs` | Yes |
| `Telegram.Channel.Tests` | `TelegramChatNeuronTests.cs` | Yes |
| `UiKit.Tests` | `FlutterUiNeuronTests.cs` | Yes |
| `Developer.Tests` | `GitNeuronTests.cs`, `RoslynNeuronTests.cs`, `NuGetNeuronTests.cs`, `DotNetNeuronTests.cs` | Yes |
| `Google.Tests` | `GmailNeuronTests.cs`, `GoogleCalendarNeuronTests.cs`, `GoogleDriveNeuronTests.cs`, `GoogleAuthNeuronTests.cs` | Yes |
| `Windows.Tests` | `FileSystemNeuronTests.cs`, `ShellNeuronTests.cs`, `WingetNeuronTests.cs` | Yes |
| `TestKit.Tests` | `TestDigitalBrainTests.cs` | Special case — this file tests the harness itself; add a companion test proving `NeuronTestBase` works, don't just migrate |
| `Telegram.Tests` | `TelegramResponderNeuronTests.cs` | No — pure `IPackBehavior` pack, no grain, no Orleans dependency |
| `Experience.PersonalAssistant.Tests` | `PersonalAssistantNeuronTests.cs` | No — pure `IPackBehavior` pack, no grain, no Orleans dependency |

Two plain-C#-service tests with zero Orleans dependency (`Developer.Tests/RoslynAnalysisServiceTests.cs`,
`Windows.Tests/FileSystemOperationsTests.cs`) are also left untouched — they don't use `TestDigitalBrain` at
all today and shouldn't start.

Every migration is a mechanical, behavior-preserving lift-and-shift: replace the `IAsyncLifetime` + private
`TestDigitalBrain _brain` field with `: NeuronTestBase` inheritance, replace `_brain.Grain<T>(...)` calls with
the inherited `Grain<T>(...)`, and (for `Google.Tests`, which passes a silo-builder extension callback to
`TestDigitalBrain`'s constructor) override `ConfigureSilo` instead. No assertions or test logic change.

### 3. `DigitalBrain.Tests` reorganization

`UnitTest1.cs` (601 lines, ~25 scenarios across 7 unrelated concerns) is deleted and its scenarios
redistributed into the suite's existing concern-folder taxonomy, with self-explanatory file names:

| New file | Scenarios moved in |
|---|---|
| `Kernel/NeuronCoreTests.cs` | Activation/journaling, `FireAsync` replay, correlation/causation propagation |
| `Kernel/CheckpointTests.cs` | Branch/fork with replayed history, restore-from-checkpoint |
| `Kernel/SystemStatusTests.cs` | SystemStatus launch + checkpoint-fix simulation, `KernelTask` run/recover |
| `Trust/PackSignatureTests.cs` | Invalid/unsigned pack rejection |
| `Distribution/MarketplaceTests.cs` | Publish/install/commission, private-pack ownership gating |
| `Foundry/PackEmbodimentTests.cs` | Compile→embody→dispatch, typed-synapse handling + causation/`UiSurface` emission |
| `Context/InoTaskContextTests.cs` | Dual-journal ino/task context |
| `Ui/ChartAndInsightsTests.cs` | DataVisualization/Chart surfaces, Gmail-insights summary + chart |
| `Sdk/GitNeuronTests.cs`* | Git commit metrics derived from journal |

No new folders are created beyond what already exists in `DigitalBrain.Tests/`.

\* `Developer.Tests/GitNeuronTests.cs` already tests `IGitNeuron` against a real git repo. Before relocating
this scenario, check it for overlap with that existing test — if it's duplicate coverage, delete it instead
of moving it; only relocate if it exercises something the ino-level test doesn't.

### 4. Quality audit

Run the `assertion-quality` and `test-anti-patterns` skills (already installed under `brain/.claude/skills`)
across all of `DigitalBrain.Tests` (112 files) and the 8 ino `.Tests` projects. Findings are triaged per-item
into fix / delete / consolidate — this is where "clean trash" happens across the suite, informed by tooling
rather than a manual skim.

## Verification ritual

Per slice: `dotnet build` → `dotnet test --filter "<affected namespace>"` (targeted, matching the repo's own
documented convention of not running blanket full-suite tests on every edit) → after all slices, one broader
high-severity filter run covering everything touched. No Aspire/hosting files change in this sub-project, so
`aspire doctor` is not part of this ritual (it applies to AppHost/wiring changes, which this isn't).

## Suggested sequencing (input to the implementation plan)

1. Extract `NeuronTestBase`; migrate all 14 grain-test files across the 8 ino projects to it (mechanical,
   proves the base class against real, already-passing usage before anything else changes). **This is the
   scope of the first implementation plan** — see
   `docs/plans/2026-07-02-neuron-test-base-extraction-and-migration.md`.
2. Split `UnitTest1.cs` into the 9 domain files (own plan, written once step 1 has landed).
3. Run `assertion-quality` + `test-anti-patterns` across the full scope; act on findings (own plan — findings
   aren't knowable until the tools actually run).
4. Final verification pass.

## Risks

- Migrating all 14 files to `NeuronTestBase` must not change existing assertions or behavior — this is a
  mechanical lift-and-shift for already-passing tests; no new test logic is introduced in step 1.
- The original survey that fed this design missed several already-existing test files (Winget/Shell,
  DotNet/NuGet, Drive/Calendar/Auth) on its first pass — a direct read of every ino `.Tests` directory (done
  before finalizing this doc) corrected that. There is no remaining coverage-gap-filling work.
