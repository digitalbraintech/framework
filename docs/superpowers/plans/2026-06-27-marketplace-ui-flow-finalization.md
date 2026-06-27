# Marketplace Install + Run Experience Flow via UI Kit (Finalization Plan)

**Date:** 2026-06-27
**Status:** Plan for finalization of the server-driven marketplace UI loop (install from list → embody → run experience → new live surfaces). All driven by UiSurface/RfwCard (kit) + synapses + neurons. No client hard-coded logic for the flow.

**Background (from exploration):**
- Surfaces exist: `MarketplaceList` + `InstalledBundles` (with `installAction`, `experiences[*].action` as `SynapseAction` descriptors carrying `synapseType` + props like packName/version or prompt).
- Builders in `UiSurfaceLiveData` (MarketplaceListFromPacks, InstalledBundlesFromPacks, ExperiencesForPack).
- Emission gap: NOT pushed into primary `HomeFeedBus`/`WatchHomeFeed` on start or post-install (only MCP on-demand, dashboard mentions "market").
- Dispatch gap: `GatewayService.Send` only supports demo type; `_handlePanelEvent` sends generic `SynapseEnvelope` (looks for 'type'); no reconstruction of `InstallFromMarketplace` / `InoRequest` / `ExperienceUsed` + route to "market-main" / generated- / ino-main via `NeuronResolver`.
- Backend install/embody works great: `MarketplaceNeuron` (trust, econ, license, `NeuroPackInstalled`, special kernel self-update) → `GeneratedNeuron.DeliverAsync` + `ExperienceUsed` → `PackAlcEmbodier` (Roslyn+ALC) → outputs (incl. `UiSurface` from pack) → `BroadcastPackSurface` → `RfwCard` via bus.
- Run experience: actions point to `InoRequest` or direct; embodied packs can emit surfaces.
- UI kit (RFW): client has generic event dispatch, `rfw_card_sources` examples, panel manager for live cards, `digital_brain_ui` (glass/glow). No special marketplace card source yet (unlike TaskManagerCard). Actions use `event "name" { type: "...", ... }`.
- Harvest from `Projects/` (read-only, do not extend): Strong prototypes in `Projects/digitalbrain/UI/flutter` (and mirrors in digitalbrain-app, ino.flutter, final):
  - Custom UI kit patterns (glass/liquid, adaptive surfaces, glow, responsive VStack/HStack/Panel, pill buttons, theme tokens).
  - RFW host + fixed `digitalbrain` lib + event table + card sources + `rfwBoundedFrame`.
  - Install flow examples: `SkillCard` (icon+desc+chip + conditional Install/FilledButton or spinner or "Installed"), BLoC `installingId` + reload.
  - Action descriptors: old `UiButton(onTap: { 'Type': 'InstallFromMarketplace', ... })` + `buildFromUiWidget`.
  - Live canvas + panels + orbit badges + command palette (same as current).
  - Marketplace icons in painters, demo RFW buttons for install.
  - No ForUI (forui.dev) anywhere — all custom + RFW + selective cupertino/material + getwidget/modular_ui in galleries. The "UI kit" is the `digital_brain_ui` + RFW palette (harvest this).
- Forui.dev note: Modern Flutter component lib (ForCard, ForButton, etc.). Not in any prototype. Could be future enhancement for native parts of palette, but current server-driven RFW + custom glass kit is the architecture (keep consistent; do not introduce without plan). No need now.

**Goal (finalized flow):**
User opens app → sees live "Marketplace" + "Installed Bundles" panels (via kit/RFW) in canvas.
Tap "Install" on a pack → synapse → neuron (trust/econ/embody) → live refresh of lists + confirmation surface.
Tap "Run" on experience → dispatches action descriptor → experience runs (ino or embodied) → new surfaces/RFW cards from the pack appear as floating panels.
All via neurons firing synapses → kit surfaces. Pure, testable, embodied.

**Design decisions (5 steps + harvest):**
1. Less dumb: The surfaces + descriptors already model the flow perfectly (actionId/synapseType/props). Just close the emit + dispatch legs. Use existing `NeuronResolver`, `HomeFeedBus`, `UiSurfaceRfwBridge`.
2. Delete: No new client hardcode for "install UI". Generic dispatch + surface-driven. Remove demo-only special cases where they block. No new ForUI (harvest existing kit patterns).
3. Simplify: One generic path in `GatewayService.Send` that looks at `typeName` (or action descriptor) → resolve target neuron (market-main / generated-xxx / ino-main) → construct typed synapse from payload/props → `FireAsync`. MarketplaceNeuron after success fires refreshed surfaces (like tasks do). Use journal for lists (already cached).
4. Accelerate: Emit `MarketplaceList`/`InstalledBundles` (or bridged Rfw with rich data) on `StartDistributedApp` (like task manager) + after `NeuroPackInstalled`. Client already renders panels + buttons from data.
5. Automate later: Progress during install (emit temp surface), auto-run on certain packs, ForUI polish if wanted.

**Architecture:**
- Backend owns: live lists from journals/packs + action descriptors.
- On bootstrap + post-install: `FireAsync(UiSurface(..., MarketplaceList/InstalledBundles))` + `bus.Broadcast( bridge or direct RfwCard("digitalbrain", "MarketplaceCard" or root from props, data with packs + actions) )`.
- Dispatch: extend `GatewayService.Send` (and note UiGateway) to:
  - If typeName == "InstallFromMarketplace" (or from descriptor): get "market-main", build `new InstallFromMarketplace(...)` from payload, `FireAsync`.
  - Similar for Ino/ClosedLoop/ExperienceUsed: route + fire.
  - Use `NeuronResolver` where possible; fall back to market/ino/generated.
- Client: minimal — ensure RFW for lists uses `for` + Button emitting `event "install-pack" { type: "InstallFromMarketplace", packName: ..., version: ..., buyerId: ... }` (or use action descriptor). Existing `_handle` + send mostly works if type present. Add support for actionId if needed (harvest from old Ui models).
- RFW kit: add (or use bridge) a source for marketplace/ bundles cards using existing primitives (Panel, VStack, Button, Badge for installed, Counter). Harvest card patterns (TaskManagerCard, skill cards).
- After embody: packs can emit their own `UiSurface`/`RfwCard` for "running experience" UI — flows automatically.
- Use `UiSurfaceLiveData` builders for consistency; call from kernel on relevant events (or on journal write via reactor if needed).

**Constraints (non-negotiable):**
- net11, central packages (Directory.Packages.props).
- Context7 for every API (Flutter RFW events, Orleans grains/journal/fire, Aspire resources/endpoints — done in this planning).
- Relative paths. No C:\Users.
- Aspire changes via MCP (`doctor`, `list_resources`, `execute_resource_command`, `list_console_logs`).
- Small changes, delete > add. Self-explanatory names. No vacuous docs.
- Tests: targeted high-sev (UiSurfaceContract, gateway, neuron steps) + aspire doctor after each. Full E2E when called for.
- Harvest only; do not edit Projects/.
- Verify: build, test, aspire run (targeted resources), doctor.

**Implementation phases (small verticals, verify each):**

### Phase 0: Commit + baseline (done in session)
- Commit the task-manager + related (already executed).
- Re-run `aspire doctor`, `dotnet build`, relevant tests (UiSurface + marketplace paths).
- Confirm current surfaces + descriptors via code.

**Verification:** doctor green; build 0e; tests for surfaces pass.

### Phase 1: Emit marketplace surfaces live (kernel + core)
**Files (brain/ only, relative):**
- `DigitalBrain.Kernel/SystemNeurons.cs` (in `AspireOrchestratorNeuron.HandleAsync(StartDistributedApp)` after task/dashboard, and in `MarketplaceNeuron` after `NeuroPackInstalled` success + on publish).
- Optionally `DigitalBrain.Core/UiSurfaces.cs` (if need richer MarketplaceCard sample or direct Rfw helper like TaskManager).

**Changes (delete-first, small):**
- After dashboard/task emit: `var marketSurface = UiSurfaceLiveData.MarketplaceListFromPacks(...); await FireAsync(marketSurface); bus?.Broadcast(bridge.From... or direct Rfw with data);` (same for Installed).
- In `HandleAsync(InstallFromMarketplace)` after `FireAsync(NeuroPackInstalled)` + deliver: refresh + broadcast updated lists (use recent journals or query market).
- Similar on `PublishToMarketplace` or `ListPublished`.
- Make data rich for kit: include full action descriptors.
- Use existing `WithCommon`, builders.

**Why this works:** Mirrors task manager (which we just proved). Surfaces hit bus → Watch → panels.

**Verification:** `dotnet test ... --filter "UiSurfaceContract|Marketplace"`; doctor; rebuild kernel resource via MCP if running.

### Phase 2: Close dispatch gap (gateway + resolver)
**Files:**
- `DigitalBrain.Kernel/Gateway/GatewayService.cs` (extend `Send`).
- `DigitalBrain.Kernel/Gateway/NeuronResolver.cs` (minor if needed).
- Note: `UiGatewayService` for bidi if actions come that way.

**Changes:**
- In `Send`:
  ```csharp
  if (request.TypeName == nameof(InstallFromMarketplace) || request.TypeName.Contains("install", StringComparison.OrdinalIgnoreCase))
  {
      var market = grains.GetGrain<IMarketplaceNeuron>("market-main");
      // parse payload json to props (use System.Text.Json or existing)
      var p = ... from request.Payload;
      await market.FireAsync(new InstallFromMarketplace(p["packName"]?, ... ));
      return request;
  }
  if (request.TypeName == nameof(InoRequest) || ... ) { ... resolve "ino-main" or from props, Fire; }
  // General: try resolver with typeName as hint, or default demo path for backward
  var neuron = NeuronResolver.ResolveForAction(grains, request.TypeName, payload); // extend resolver lightly
  await neuron.FireAsync( construct from type + payload ); // or keep Demo for unknown
  ```
- Extend resolver if helpful: map actionId or synapseType.
- Support payload as the props dict (already json in envelope).
- For experience run: if action has "experienceId" or direct synapse, route to generated- + `ExperienceUsed` or ino.

**Harvest:** Use patterns from old `buildFromUiWidget` + onFire maps.

**Why:** Makes UI taps (RFW events with descriptors) reach the exact neuron/synapse.

**Verification:** Update contract/gateway tests; targeted test; doctor.

### Phase 3: Client/kit polish for rich marketplace cards + actions (app/ + minimal)
**Files (app/ relative, separate repo commit):**
- `lib/features/canvas/living_canvas_screen.dart` (extend _handle if needed to recognize action descriptors from surface data, set 'type' from action or synapseType).
- `lib/rfw_host/rfw_card_sources.dart` or `digitalbrain_rfw_library.dart` (add `kMarketplaceListCardSource` or reuse Panel/Button for `data.packs` + install buttons emitting full event with type + props. Harvest from `kTaskManagerCardSource` + old skill_card).
- `lib/rfw_host/palette/palette_primitives.dart` (if need new primitive like install button with loading).
- Ensure RFW for lists supports `for pack in data.packs` + conditional button (installed? badge : installAction button).

**Changes (small):**
- In handle: if no 'type' but args has action or from surface, set typeName = action['synapseType'] or name.
- Add card source using existing widgets (VStack of pack rows with Button(onTap: event "install" { type: "InstallFromMarketplace", packName: ..., ... })).
- Use data from bridge (props copied in).
- For run: same, buttons emit the experience action.

**Harvest from Projects:** Use card + button + loading patterns exactly (skill_card, UiButton, RFW for loops, glass panels).

**Verification:** flutter analyze; if possible rebuild flutter resource via MCP `execute_resource_command restart`; check logs for no crash.

### Phase 4: Refresh + confirmation + run experience surfaces
**Files (mostly kernel):**
- `SystemNeurons.cs` (Marketplace after success: emit confirmation Rfw/UiSurface + refreshed lists; on ExperienceUsed success if needed).
- Optionally enhance `UiSurfaceRfwBridge` or add specific source for marketplace actions (rich buttons with synapseType).

**Changes:**
- After `NeuroPackInstalled` + deliver: `FireAsync` updated `InstalledBundles` (now shows new bundle + its run actions) + `MarketplaceList` (pack now installed=true).
- Broadcast so live panels update.
- For run: ensure the action descriptor leads to `ExperienceUsed` on the generated grain (or Ino) → pack can emit its UI (surfaces appear as new panels).
- Add simple "Install successful" surface with "Run now" button.

**Verification:** integration test or steps that fire install action → assert surfaces in journals/feed.

### Phase 5: End-to-end test + final polish + aspire run
- Add/update Reqnroll or unit: UI action → install → refresh → run → pack surface.
- Use aspire MCP: select apphost, list_resources, execute restart on kernel/flutter, list_console_logs with search "market|install|InstalledBundles|MarketplaceList|RfwCard".
- Full: `dotnet build`; targeted high-sev (include marketplace neurons, surfaces, gateway); `aspire doctor`; manual `aspire run` (watch for surfaces in logs + flutter no error).
- If ForUI wanted: note as future (add to pubspec, map some palette to For* components) — not required for this finalization (existing kit sufficient + harvested).

**Files touched summary (minimal):**
- brain/: SystemNeurons.cs (emit + refresh), GatewayService.cs (dispatch), tests.
- app/: living_canvas + rfw sources (action + card support) — keep tiny.
- Plan + docs only.

**Risks / open (keep small):**
- Dynamic synapse construction from json (use existing payload patterns or simple switch on typeName).
- Port/endpoint for flutter (already fixed in prior).
- HA: surfaces fan per replica (ok, like tasks; later shared view).
- No new packages (unless ForUI explicit later).

**Verification ritual (every phase + final):**
- `cd brain; dotnet build`
- `dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "UiSurface|Marketplace|Gateway|NeuronCore" --logger "console;verbosity=minimal"`
- `aspire doctor` (MCP)
- When ready: targeted `aspire run` (or resource restart via MCP `execute_resource_command`), observe logs for "Marketplace", "install", surfaces, flutter connect + render.
- High-sev green, aspire integration aspects (resources healthy).

This closes the loop with pure neurons (Marketplace/Generated/Ino), synapses (Install/ExperienceUsed + actions), kit (surfaces + RFW cards from descriptors). Matches "install something from marketplace via ui out of ui kit and neurons and synapses as well as run the experience".

Ready for approval + execution (use subagent-driven or executing-plans after exit). Follow 5 steps in order during impl.

(Exploration used Context7 for RFW events/dispatch, Orleans grains/journal/fire, Aspire resources — per rules. Projects harvested for patterns only.)