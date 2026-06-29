# Startup Task Manager via UI Kit — Implementation Plan

**Date:** 2026-06-27
**Goal:** On system start the user immediately sees a live Task Manager (active executions, progress, quick actions) rendered entirely through the server-driven UI kit (RfwCard / UiSurface over WatchHomeFeed + bidi). The experience says "time to work." All dynamic task UI comes from kernel neurons / packs; client remains thin host + primitives.

**Derived from:** core-requirements (projects-survey UI kit best-of-breed + NeuroTask, transcript "executable skills", Musk 5 steps), CONTINUITY (SDUI backbone complete, task protocol universal, KernelTaskNeuron), brainstorm of current state (TaskWindow kind + kTaskManagerCardSource exist but not primary on start; landing is general canvas + bundles/market/timeline).

## Background (current state summary)
- Task execution: full `TaskCreated`/`...`/`TaskCompleted` + `RunTask`/`CancelTask` + `IKernelTask`/`KernelTaskNeuron` (LLM or fallback, pure journal derived `GetInfoAsync`).
- UI kit: `UiSurface` (TaskWindow kind + samples), `RfwCard`, `HomeFeedBus` singleton fanout, `WatchHomeFeed` gRPC, `UiGatewayService` bidi, `UiSurfaceRfwBridge`, client `LivingCanvasScreen` + `PanelManager` + `RfwRuntimeHost`.
- Client has `kTaskManagerCardSource` (Control Center, totals, TaskRow list with cancel events) + icon support, but raw broadcast feeds are filtered; no auto primary task list surface emitted on bootstrap.
- Startup: `AspireOrchestratorNeuron.HandleAsync(StartDistributedApp)` emits kernel-dashboard (mentions "tasks" panel) + other live surfaces via `FireAsync(UiSurface)`. Some paths also `Broadcast(RfwCard)`.
- Client default: `/` → `LivingCanvasScreen` (floating panels from renderable RfwCards). No prominent task manager.
- `UiSurfaceLiveData` builds workbench surfaces (used by MCP + some neurons); Task* not aggregated into manager view yet.
- NeuroTaskApp sample: empty stub.
- Emission patterns: grains `FireAsync` (journals + causation via Neuron base) or `ServiceProvider.GetService<HomeFeedBus>()?.Broadcast(card)`.
- Constraints honored: net11.0, central packages, relative paths, no vacuous docs, Context7 used for Orleans/Aspire/Flutter lookups (resolves + targeted queries on journaled grains, HomeFeed fanout, Aspire project wiring, RFW runtime + Semantics), aspire doctor green.

## Design decisions (5 steps applied)
1. **Less dumb:** Default surface = productive task list (not marketplace or generic canvas). Tasks are the "executable" layer over the company brain.
2. **Delete:** No new client task widgets or hardcoded lists. Remove duplication between TaskWindow demo and control-center card if mergeable. No extra persistence for tasks (journals + grain keying is truth).
3. **Simplify:** Single "task-manager" RfwCard shape matching existing client `kTaskManagerCardSource` (totals + tasks array). Aggregate from task journals + active grains. Reuse `HomeFeedBus` + existing Watch path.
4. **Accelerate:** Emit initial manager card + a demo task on bootstrap (in AspireOrchestrator or new light emitter). KernelTaskNeuron broadcasts updates on lifecycle events. Fast inner loop for changes.
5. **Automate:** Later — auto-suggest tasks from Context, pack-embodied richer TaskManager experience, self-improvement on completion rates.

**Architecture (keep server-driven):**
- Backend owns shape + data (live from journals/grains).
- Emit `RfwCard("digitalbrain", "TaskManagerCard", json)` so client card source renders it (high priority / auto prominent panel).
- Or dual: also support `UiSurface` kind "task-manager" turned by bridge.
- On start (after `StartDistributedApp` or grain activation): push manager card (empty or seeded with one live task via RunTask for visibility).
- Task updates: KernelTaskNeuron (and future task producers) broadcast refresh cards.
- Actions (cancel, focus): existing onEvent path in canvas → Send/Fire synapse → `CancelTask` etc.
- Client: minimal — ensure card with RootWidget "TaskManagerCard" renders via existing source; auto-open high-prio task panel on first receipt; use stable semantics for future E2E.
- No change to HA (3 replicas) — HomeFeedBus per-silo means task cards fan per replica (acceptable for v1; later shared view if needed).
- Verification gates: every task does `dotnet build && dotnet test --filter "..."` (relevant unit/contract) + `aspire doctor`. Full run only when called for end-to-end.

**Global constraints (non-negotiable)**
- net11.0, versions only from `Directory.Packages.props` (latest deliberate).
- Context7 before any framework call sites (already performed; re-query on edit if APIs shift).
- Aspire changes via MCP tools (`doctor`, `execute_resource_command`, logs) or CLI; prefer targeted over full restart.
- Use relative paths only. Small inline comments only for non-obvious.
- No default `/// <summary>`.
- Per-step: targeted test (high-sev stays green, E2E collection not selected by default filters).
- Two-repo: brain/ changes one commit; app/ changes separate if needed.
- Plan uses checkbox style for sub-agents / workers.

## Phases / Tasks

### Task 0: Setup + baseline (no behavior change)
- [ ] Run `aspire doctor` (MCP or `aspire doctor`) from brain/ dir; confirm green.
- [ ] `dotnet build` (brain) + relevant filter tests green.
- [ ] Read current `UiSurfaceKinds`, `KernelTaskNeuron`, bootstrap in `AspireOrchestratorNeuron`, `GatewayService.WatchHomeFeed`, `kTaskManagerCardSource`, `PanelManager` (done via tools).
- [ ] Document any new Context7 lookups needed for edits.

**Verification:** `aspire doctor`; `dotnet test --filter "FullyQualifiedName~UiSurfaceContractTests|HomeFeedBusTests|KernelTask"` (or equivalent).

### Task 1: Server — add live task manager data builder + card emission helper (Core + Kernel)
**Files (brain/ only):**
- `DigitalBrain.Core/UiSurfaces.cs` (extend `UiSurfaceKinds`, add `TaskManager()` sample + live builder method; keep delete > add).
- `DigitalBrain.Kernel/SystemNeurons.cs` (in `AspireOrchestratorNeuron.HandleAsync(StartDistributedApp)` and/or `KernelTaskNeuron` lifecycle: build + `FireAsync` or `Broadcast` a matching RfwCard or UiSurface).
- New or extend: helper to aggregate recent `TaskCreated` etc + active task grains into the shape expected by client card (totals + array of {correlationId, shortHash, originNeuron, status, ageMs, edgeCount, ...}).

**Key shape (match existing kTaskManagerCardSource):**
```json
{
  "totals": { "active": N, "completed": M, "failed": K },
  "tasks": [ { "correlationId": "...", "shortHash": "...", "originNeuron": "...", "originIcon": "...", "ageMs": 123, "edgeCount": 2, "status": "running", ... } ]
}
```

- On start: emit one (optionally fire a seed `RunTask` via grain to populate a visible item).
- On task progress/complete/cancel in `KernelTaskNeuron`: refresh/broadcast updated manager card (use `ServiceProvider.GetService<HomeFeedBus>()`).
- Also support UiSurface kind for future pure declarative (bridge will handle).

**Steps:**
- [ ] Add kind + builder (no behavior yet).
- [ ] Wire emission in bootstrap path + one lifecycle hook.
- [ ] Targeted test: extend `UiSurfaceContractTests` or new unit for builder.
- [ ] Run build + filter test.

**Verification per substep:** `dotnet build`; `dotnet test --filter "UiSurfaceContract|task"`; `aspire doctor`.

### Task 2: Make task manager appear on start (prominent + seeded)
**Files:**
- `DigitalBrain.Kernel/SystemNeurons.cs` (or introduce minimal `TaskOrchestratorNeuron` if aggregation grows; prefer edit existing).
- `DigitalBrain.Kernel/Gateway/GatewayService.cs` (if needed for seeding or special correlation).
- Optionally `DigitalBrain.Core/MarketplaceSeeds.cs` or seeds for a "DigitalBrain.UI.Tasks" reference.

- Seed: on first dashboard, also run a demo task ("Explore the live task manager") so list is non-empty.
- Priority/layout: emit with high priority or special surfaceId so `PanelManager` surfaces it first (or add light auto-open logic).

**Steps:**
- [ ] Update bootstrap to emit manager card + seed task.
- [ ] Ensure refresh cards use same correlation for panel stability.
- [ ] Test: unit for emission + contract.
- [ ] `dotnet test` relevant + doctor.

**Verification:** build + targeted tests + doctor.

### Task 3: Client — ensure render + productive landing (app/ repo)
**Files (app/):**
- `lib/rfw_host/rfw_card_sources.dart` (minor: ensure `TaskManagerCard` data shape tolerant; add comment only if non-obvious).
- `lib/features/canvas/living_canvas_screen.dart` (on first renderable task-manager card: auto upsert + bring-to-front or mark prominent via existing panel API).
- `lib/rfw_host/rfw_runtime_host.dart` or `digitalbrain_rfw_library.dart` (if new primitives needed for richer rows; prefer reuse Counter/Badge/Panel/etc.).
- `lib/features/canvas/panel/panel_manager.dart` (if tiny hook for "tasks" default open on connect; keep minimal).

- Keep all task visuals in the RFW source data-driven.
- Wire cancel: existing `_handlePanelEvent` + Send path already dispatches; ensure "cancelTask" produces `CancelTask` synapse.

**Steps:**
- [ ] Confirm/ tweak render path for RootWidget="TaskManagerCard" (use existing source).
- [ ] Add auto-promote for task manager panels on receipt (small, delete any dead branches if found).
- [ ] Optional: default layout seeds a tasks panel.
- [ ] Flutter side: `flutter test` (if widget tests) or analyze.
- Separate commit in app/.

**Verification:** client build/analyze; full brain high-sev still green.

### Task 4: Wire actions + live updates end-to-end (kernel + tests)
- KernelTaskNeuron already fires the lifecycle synapses.
- From card events → existing gateway send → neuron resolver fires `CancelTask` (MCP already does similar).
- Verify: manager refreshes after action.

**Steps:**
- [ ] Add or confirm in `KernelTaskNeuron` a broadcast of updated manager after each Handle.
- [ ] Add contract test that `RunTask` → card appears with correct status.
- [ ] E2E light (or gate): if flutter web built, but keep non-hanging.
- [ ] Use `aspire doctor`.

**Verification:** `dotnet test --filter "task|UiSurface|GatewayLive"` (targeted); doctor.

### Task 5: Sample + docs + polish (delete cruft)
- Update `samples/Awesome/SoftwareEngineering/Software20/NeuroTaskApp.cs` to at least emit a task or reference the manager (typed C# neuron example).
- Add a surface sample for task-manager in `UiSurfaceSamples` if not covered by live builder.
- Update CONTINUITY.md (brief entry) + any relevant readme.
- Delete any unused demo TaskWindow emission if purely superseded (5-step delete).
- Ensure no new package versions without Directory.Packages.props bump (use latest per rule).

**Verification:** build + doctor.

### Task 6: Full validation (only when ready)
- Targeted high-sev suite.
- `aspire doctor`.
- Optional: `aspire run` (or resource commands) + manual start client to see task manager card on connect (document the flow).
- If E2E flutter assets present: run gated browser render test for the card (follow prior bucket-d pattern).

## Risks / open (keep small)
- Replica fanout: manager cards are per-silo for now (fine for prototype; one replica for local dev).
- Data shape drift between builder and `kTaskManagerCardSource`: pin in test.
- Seeded demo task: make optional or always present for "time to work" feel.
- No new deps.

## Per-task command examples
- Build: `dotnet build` (from brain/)
- Test: `dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "FullyQualifiedName~UiSurfaceContractTests|HomeFeed|KernelTask" --logger \"console;verbosity=minimal\"`
- Aspire: `aspire doctor` (MCP preferred) or `aspire doctor --project NeuroOSPrototype.AppHost`
- Client: `flutter analyze` or `flutter test` from app/
- After edits: commit small, run ritual.

Follow this plan in order. Each task must pass its verification before next. Use sub-agents for parallel leafs only after parent complete. Keep changes small, delete-first where possible.

Ready for implementation or review.