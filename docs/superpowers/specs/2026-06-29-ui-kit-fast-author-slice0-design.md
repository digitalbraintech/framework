# Slice 0 — "Hello World on rails": fast UI-kit authoring loop

**Date:** 2026-06-29
**Repos:** `brain` (DigitalBrain.Core/Kernel/Cli) + `app` (Flutter host)
**Status:** Approved design — ready for implementation plan
**Basis:** `core-requirements/marketplace-user-flow-scenarios.md`, `Musk approach.txt` (5-step algorithm), the existing experiences slice (`2026-06-28-experiences-shell-host-design.md`, `2026-06-28-domains-experiences-travel-slice-design.md`), and user direction (curated ~20–30 component kit as ForUI covers represented as neuron/synapse vocabulary; extremely fast iteration).

## Goal

Thread one trivial app — **Hello World** (enter name → press button → "Hello {name}") — through **all four iteration levers** at minimum surface, establishing the pattern the full kit will follow:

1. **Fewer edit-points** — a new app is **one `.cs` file (~15 typed lines)**, not 2 files + ~200 lines of inline RFW.
2. **High-level UI widgets** — compose typed kit components, **zero hand-written RFW/JSON**.
3. **Open/run from marketplace** — click the pack → it launches full-screen.
4. **Live hot-reload** — save the pack file → it republishes + embodies in the running cluster, UI updates **with no kernel recompile or restart**.

Hello World is the **acceptance test** for the whole loop. Once the slice exists, adding another trivial app touches **no** kernel or app code.

## Non-goals (Musk "delete" — explicitly out of Slice 0)

- The full 20–30 component catalog (Slice 0 ships **5** seed components; the rest is a follow-up sub-project).
- Per-user (vs per-session) flow state.
- Multi-screen apps + navigation/routing model.
- A server-driven "navigate" surface (Slice 0 uses a small client routing rule — see Flag B).
- Retiring the legacy ~50-widget RFW library (kept for rich custom widgets; retired later).
- An Aspire-resource file watcher (Slice 0 uses a CLI `--watch`; promote later if desired).

## Locked decisions

- **Build order:** thin vertical slice first (this doc), then fan out the kit.
- **App unit:** an app **is an `Experience`** (guided hops) rendered by the existing experience-host. Hello World = 2 hops (`ask` → `greeting`).
- **Render path (Approach 1):** components are **typed kit nodes** carried as a `UiWidgetTree` on the wire, rendered by **one-Dart-file-per-component ForUI covers**. RFW is kept only for rich custom widgets outside the core kit.
- **Flag A — kit prefix:** new `kit:` node prefix (clean curated catalog) rather than overloading `neuron:`/`forui:`.
- **Flag B — launch trigger:** the client routes a `kind:"experience"` marketplace action to `/#/experience/<pack>/<experienceId>` (one small, documented piece of client routing knowledge for Slice 0; can move fully server-driven later).
- **Flag C — hot-reload surface:** a dev-only CLI watch command (`dbt author <file.cs> [--watch]`), not an Aspire resource.

## Architecture

```
AUTHOR (1 file, ~15 lines)          KERNEL (live, no restart)         APP (thin host)
HelloWorldExperience.cs       ──►   Roslyn → collectible ALC      ──►  experience-host route
 uses Kit fluent builder            embody IPackBehavior               renders typed tree
 emits typed UiWidgetTree           emit UiSurface(widget-tree)        via ForUI-cover widgets
        ▲                                   ▲                                  │
        │ save                              │ publish+install (self-signed)    │ Greet tap (ExperienceStep)
   dbt author --watch  ───────────────────►│◄─────────── unary Send ──────────┘
```

## Components

### A. Kit vocabulary — Core (`DigitalBrain.Core`)

Extend the kit vocabulary with **5 stable `kit:` node types** and a typed builder per node that produces `UiWidgetTree` (the existing record in `UiSurfaces.cs`). No new wire types beyond `UiWidgetTree`.

| Kit node | Role | App ForUI cover |
|---|---|---|
| `kit:Screen` | vertical hop root container | `kit/kit_screen.dart` → `FScaffold`/`Column` |
| `kit:Text` | text/label (static or `ctx`-bound) | `kit/kit_text.dart` → ForUI text style |
| `kit:TextField` | bound input (writes a named value into hop state) | `kit/kit_text_field.dart` → `FTextField` |
| `kit:Button` | action button (fires `ExperienceStep` to advance) | `kit/kit_button.dart` → `FButton` |
| `kit:Panel` | surface card | `kit/kit_panel.dart` → `FCard` |

The kit constants live alongside `NeuronUiKit` in `DigitalBrain.Core/UiSurfaces.cs` (e.g. a `Kit` static class). Each builder is a small pure function `UiWidgetTree` factory.

### B. Kit covers — app (`app/lib/kit/`)

New folder, **one Dart file per component**, each a thin cover over a ForUI widget, plus `kit_registry.dart` mapping `kit:<Name>` → builder. The existing tree-walk in `lib/rfw_host/rfw_runtime_host.dart` (which already dispatches `neuron:*` / `forui:*` nodes) gains an **additive** `kit:*` branch delegating to the registry. No rewrite of existing rendering.

Pattern for the future 20–30: add a Core node const + builder, add one `kit/kit_x.dart` cover, register it — nothing else.

### C. Authoring API — Core (`DigitalBrain.Core`)

A fluent builder + a `KitExperience : IPackBehavior` base class. Pack code references **Core + BCL only** (ALC constraint) and must use **explicit `using`s** (the documented Roslyn/ALC trap — implicit usings compile in the test asm but fail the standalone pack compile).

Author writes only `Define()`:

```csharp
using DigitalBrain.Core;

public sealed class HelloWorldExperience : KitExperience
{
    protected override Experience Define() => Experience("hello-world", "Hello World")
        .Hop("ask", s => s
            .Text("What's your name?")
            .TextField("name", placeholder: "Your name")
            .Button("Greet", goTo: "greeting"))
        .Hop("greeting", s => s
            .Panel(p => p.Text(ctx => $"Hello {ctx["name"]}!")));
}
```

`KitExperience` owns: the `ExperienceStep` state machine, hop routing by `EventName`, capture of `TextField` values into hop state, and emission of each hop via `UiSurface.ForExperienceHopTree(...)`.

### D. Typed hop emission — Core

New `UiSurface.ForExperienceHopTree(pack, experienceId, surfaceId, UiWidgetTree tree, title?, emitter?)`: the typed-tree sibling of today's RFW `ForExperienceHop`. Emits `WidgetTreeKind` with the `activeExperience` / `experienceId` / `surfaceId` markers **merged into the wire payload** the host keys on (mirrors the marker-trap fix already done for `ForExperienceHop`). `CorrelationId == surfaceId`.

### E. Experience-host render branch — app

`lib/features/experience/experience_hop_view.dart` gains a branch: if the envelope carries a typed widget-tree → render via the kit/tree renderer with `semanticsId == surfaceId` (preserving the E2E `flt-semantics-identifier` linchpin); else fall back to today's inline-RFW path (`inline_rfw_surface.dart`). Shell, canvas, and the travel RFW slice are untouched → no regression.

The `Greet` button fires `ExperienceStep` over the existing unary `Send` path (`lib/grpc/action_dispatch.dart` → `UiGatewayService`), which already routes `ExperienceStep` → the embodied pack. No protocol change.

### F. Marketplace open/run — Core + app

The Hello World marketplace entry emits a `kind:"experience"` run action (via `UiSurfaceLiveData`). Per Flag B, the app's action dispatch recognizes `kind:"experience"` and routes to `/#/experience/hello-world/hello-world`, then triggers the `start` hop. The experience-host already subscribes to `WatchHomeFeed`. Contained change in the list action wiring + app experience-kind routing.

### G. Hot-reload loop — CLI (`DigitalBrain.Cli`)

Dev-only `dbt author <file.cs> [--watch]`:

1. Read the pack `.cs`; wrap it in a `NeuroPack`; **self-sign** via `PackSignatureVerifier.SignPack` (as E2E `PublishPackAsync` does — self-signed passes the integrity gate; no publisher allowlist yet).
2. Fire `PublishToMarketplace` then `InstallFromMarketplace` over the gateway `Send` to the running cluster.
3. Kernel compiles → fresh collectible ALC → embodies → re-emits surfaces → host updates live.
4. `--watch` repeats on file save.

Faster than today because the current path edits `MarketplaceSeeds.cs` → **recompiles the kernel** → restart; the hot-loop pushes a standalone `.cs` into the live cluster with **no kernel recompile, no restart**. This is also the loop used to build the rest of the kit (dogfooding).

## Data flow (Hello World)

1. `dbt author HelloWorldExperience.cs` (or boot seed) → pack embodied in kernel.
2. Marketplace list surface shows "Hello World" with a `kind:"experience"` Run action.
3. User clicks Run → app routes to `/#/experience/hello-world/hello-world` → fires `ExperienceStep{eventName:"start"}`.
4. `KitExperience.Handle` emits the `ask` hop as a typed `UiWidgetTree` (Screen[Text, TextField(name), Button(Greet→greeting)]).
5. Host renders it full-screen via ForUI covers; user types "Alice".
6. `Greet` tap → `ExperienceStep{eventName:"greeting", args:{name:"Alice"}}` over unary `Send`.
7. `KitExperience.Handle` captures `name`, emits `greeting` hop (Panel[Text "Hello Alice!"]).
8. Host replaces the hop in place → "Hello Alice!" shown.

## Testing & verification

- **Core/Kernel (TDD, fast suite `--filter Category!=E2E`):** `KitExperience` state machine + value capture; `ForExperienceHopTree` marker round-trip; each `kit:` builder's node output.
- **App:** widget test per `kit/kit_*.dart` cover + a hop-render test mirroring `test/features/experience/experience_hop_view_test.dart`.
- **E2E (gated, runs real):** Hello World on `ExperienceFlowDriver` (~10 lines): open from marketplace → assert `ask` hop → type "Alice" → tap `Greet` → assert `greeting` shows "Hello Alice!" via `flt-semantics-identifier`.
- **Ritual after changes:** `dotnet build`; fast `dotnet test`; `flutter analyze` + `flutter test`; `aspire doctor`; one intentional `aspire run` to watch the hot-loop drive Hello World live before declaring done.

## Speed win (acceptance criteria)

| | Today | After Slice 0 |
|---|---|---|
| Files to add a trivial app | 2 + ~200 lines inline RFW | **1** `.cs`, ~15 typed lines |
| Hand-written RFW/JSON | yes | **none** |
| Kernel recompile + restart to see it | yes | **no** (hot-loop) |
| Launch from marketplace UI | deep-link / E2E only | **click → full-screen** |
| New kit component | edit the 50-widget mega-file | 1 Core const+builder + 1 `kit/` file + register |

## Edit-points map (where the work lands)

**brain:**
- `DigitalBrain.Core/UiSurfaces.cs` — `Kit` node consts + `ForExperienceHopTree`.
- `DigitalBrain.Core/` (new) — kit fluent builder + `KitExperience` base.
- `DigitalBrain.Core/MarketplaceSeeds.cs` — seed the Hello World pack; `UiSurfaceLiveData` experience-kind run action.
- `DigitalBrain.Cli/` — `dbt author [--watch]` command.
- `DigitalBrain.Tests/` — unit tests + the E2E driver test (under `E2E/`).

**app:**
- `lib/kit/` (new) — 5 `kit_*.dart` covers + `kit_registry.dart`.
- `lib/rfw_host/rfw_runtime_host.dart` — additive `kit:*` dispatch branch.
- `lib/features/experience/experience_hop_view.dart` — typed-tree render branch.
- `lib/grpc/action_dispatch.dart` (or router) — `kind:"experience"` → experience route (Flag B).
- `test/` — kit cover widget tests + hop-render test.

## Follow-up sub-projects (after Slice 0 proves the loop)

1. Fan the kit out to the full 20–30 components (same pattern).
2. More marketplace demo scenarios.
3. Per-user flow state; multi-screen + nav model.
4. Server-driven navigate surface (remove the Flag B client rule).
5. Retire the legacy RFW mega-library; Aspire-resource watcher for the hot-loop.
