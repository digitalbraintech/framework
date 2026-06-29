# UI-Kit Catalog (Sub-project B) — fan the `ui:` kit out to a curated 35-component gallery

**Date:** 2026-06-29
**Repos:** `brain` (DigitalBrain.Core/Kernel) + `app` (Flutter host)
**Status:** Approved design — ready for implementation plan
**Basis:** Slice 0 (`2026-06-29-ui-kit-fast-author-slice0-design.md`, DONE + merged locally) established the typed `ui:` kit, the `KitExperience` fluent builder, the one-file-per-component ForUI cover, the `ui:*` renderer branch, and the marketplace render+author+hot-reload loop end-to-end. This sub-project grows the kit from **5 to 35 components** following that exact pattern, and adds a self-authored gallery that showcases them.

## Goal

Grow the curated kit from **5** to **35** components (5 existing + 30 new), each a thin cover over a ForUI primitive, each a typed `ui:` node on the wire. Authors compose them via the existing `KitExperience` fluent builder; the client stays a dumb renderer. Ship a **gallery experience** that displays every component grouped by category — a living showcase and a broad render smoke-test.

The whole point of Slice 0 was to make each new component a three-edit increment. This sub-project is that increment applied 30 times, plus navigation and overlays, plus the gallery.

## Non-goals (explicitly out)

- **Multi-screen / cross-experience routing.** Navigation components navigate *within* one experience by advancing hops (`ExperienceStep`), not between experiences or app routes. The server-driven navigate surface remains a later sub-project.
- **A new overlay wire protocol.** Overlays are declarative nodes driven through the existing `ExperienceStep` loop (see Overlay model). No new RPC, no new surface kind.
- **Per-user (vs per-session) flow state.** Unchanged from Slice 0.
- **Retiring the legacy RFW mega-library.** Kept for rich custom widgets.
- **Theming / design-token authoring surface.** Components use the active ForUI theme as-is.
- **Live `onChange`-fires-an-action model.** Inputs capture into hop form-state; `ui:Button` (and nav nodes) advance hops. No per-keystroke server round-trips.

## Locked decisions

- **Catalog = 35 components**, grouped Inputs / Layout / Display / Feedback / Navigation / Overlays (full table below). User signed off on the list and grouping.
- **Value model:** every captured input value is **string on the wire**, matching Slice 0's `Map<String,String>` form-scope and `ExperienceStep.Args`. Non-string inputs are coerced at the cover (`Checkbox`→`"true"`/`"false"`, `Slider`→`"0.5"`, `DateField`→ISO `"2026-06-29"`).
- **Navigation = hop-advance:** nav nodes carry `(label, goTo)` targets and fire `ExperienceStep{eventName: goTo}` over the same unary `Send` path `ui:Button` uses. **No new infra.**
- **Overlays = declarative `open` nodes:** an overlay node with `open:true` in a hop tree triggers a client-side imperative `showFDialog`/`showFSheet`/`showFToast`; dismissal/actions flow back as `ExperienceStep`. **No protocol change.**
- **Gallery is dogfood:** a seeded `KitExperience` pack (`ui-gallery`) authored *with the kit*, openable full-screen from the marketplace like `hello-world`.
- **Additive only:** the existing `neuron:` / `forui:` / RFW renderer branches and the `ui:*` dispatch + `Inject` walk are untouched. Every component is a Core const+builder + one `ui_kit/ui_<name>.dart` cover + a registry entry.

## The catalog (35 components)

`✓` = already shipped in Slice 0. All others are new. Each maps to a ForUI 0.21.3 primitive.

### Inputs — capture a value into hop form-state, or advance hops
| `ui:` node | ForUI primitive | Wire value |
|---|---|---|
| `ui:TextField` ✓ | `FTextField` | string |
| `ui:TextArea` | `FTextField` (multiline) | string |
| `ui:Checkbox` | `FCheckbox` | `"true"`/`"false"` |
| `ui:Switch` | `FSwitch` | `"true"`/`"false"` |
| `ui:RadioGroup` | `FSelectGroup` (radio) | selected string |
| `ui:Select` | `FSelect` | selected string |
| `ui:Slider` | `FSlider` | number-as-string |
| `ui:DateField` | `FDateField` | ISO date string |
| `ui:Button` ✓ | `FButton` | fires `ExperienceStep(goTo)` |

### Layout — structure inside one Screen hop
| `ui:` node | ForUI primitive |
|---|---|
| `ui:Screen` ✓ | `FScaffold` + `Column` |
| `ui:Panel` ✓ | `FCard` |
| `ui:Row` | `Row` |
| `ui:Column` | `Column` (nested vertical group) |
| `ui:Divider` | `FDivider` |
| `ui:Header` | `FHeader` |
| `ui:Gap` | `SizedBox` (fixed spacing) |

### Display — static or `ctx`-bound content
| `ui:` node | ForUI primitive |
|---|---|
| `ui:Text` ✓ | `Text` (body typography) |
| `ui:Heading` | `Text` (heading typography) |
| `ui:Icon` | `Icon(FLucideIcons.*)` |
| `ui:Avatar` | `FAvatar` |
| `ui:Badge` | `FBadge` |
| `ui:Tile` | `FTile` (prefix/title/subtitle/suffix, optional `goTo`) |
| `ui:List` | `FTileGroup` (group of `ui:Tile`) |

### Feedback — inline status
| `ui:` node | ForUI primitive |
|---|---|
| `ui:Alert` | `FAlert` |
| `ui:Progress` | `FProgress` (linear) |
| `ui:Spinner` | `FProgress` (circular) |
| `ui:Tooltip` | `FTooltip` (wraps a child) |

### Navigation — multi-target hop-advancers (reuse `ExperienceStep`, no new infra)
| `ui:` node | ForUI primitive |
|---|---|
| `ui:Tabs` | `FTabs` |
| `ui:Breadcrumb` | `FBreadcrumb` |
| `ui:Sidebar` | `FSidebar` |
| `ui:BottomNav` | `FBottomNavigationBar` |
| `ui:Pagination` | `FPagination` |

### Overlays — declarative `open` nodes (one new client-side mechanism, no protocol change)
| `ui:` node | ForUI primitive |
|---|---|
| `ui:Dialog` | `FDialog` (via `showFDialog`) |
| `ui:Sheet` | `FSheet` (via `showFSheet`) |
| `ui:Toast` | `FToast` (via `showFToast`, fire-once) |

**Counts:** Inputs 9, Layout 7, Display 7, Feedback 4, Navigation 5, Overlays 3 = **35** (5 existing + **30 new**).

## Architecture (unchanged from Slice 0, applied ×30)

```
AUTHOR (KitExperience.Define)        KERNEL (live, no restart)          APP (dumb renderer)
 fluent ui: builder            ──►   emit UiSurface.ForExperienceHopTree ──► ui:* tree branch
 emits typed UiWidgetTree             (UiWidgetTree of ui:* nodes)            → ui_kit/ui_<x>.dart cover
        ▲                                    ▲                                      │
        │ capture-into-state                 │  ExperienceStep (unary Send)         │ input capture / Button / nav / overlay
        └────────────────────────────────────┴──────────────────────────────────────┘
```

The keystone facts that make this a pure fan-out:
- The `ui:*` branch in `UiSurfaceTreeRenderer.build` (`rfw_runtime_host.dart`) already routes **any** `ui:*` node to `buildUiNode` in `ui_registry.dart`. Adding a component = a new `case` + a new cover file. **No renderer change.**
- `KitExperience.Inject` already walks the emitted tree to stamp `pack`/`experienceId` onto action-bearing nodes. It is extended once (Component model, below) to also stamp nav and overlay-action nodes — then every component inherits it.
- Hop emission, correlation, and the experience-host render branch are all in place.

## Component model details

### Value capture (inputs)
Each input cover writes its current value into the existing `UiKitFormScope` controller (`Map<String,String>`) under its `name`, coercing to string at the cover boundary:
- `ui:Checkbox` / `ui:Switch` → `"true"` / `"false"`.
- `ui:Slider` → the numeric value via `toString()` (locale-invariant).
- `ui:DateField` → ISO-8601 date (`yyyy-MM-dd`).
- `ui:Select` / `ui:RadioGroup` → the selected option's string value.
- `ui:TextArea` → string (multiline).

`ui:Button` already snapshots the whole form-scope into `ExperienceStep.props` at press. Because all values are strings, no other change is needed on the wire. The fluent builder gains one method per input (`Checkbox(name, label)`, `Switch(name, label)`, `Select(name, options, …)`, `RadioGroup(name, options, …)`, `Slider(name, …)`, `DateField(name, …)`, `TextArea(name, …)`).

### Navigation nodes
A nav node carries an ordered list of `(label, goTo[, icon])` items plus an optional `selected` index/key. On selection the cover calls the same `onEvent` envelope `ui:Button` builds — `{synapseType:'ExperienceStep', props:{pack, experienceId, eventName: goTo, …capturedValues}}`. `KitExperience.Inject` stamps `pack`/`experienceId` onto nav nodes exactly as it does for buttons. Authoring: `Tabs(items)`, `Sidebar(items)`, `BottomNav(items)`, `Breadcrumb(items)`, `Pagination(pageCount, goTo)`.

### Overlay nodes
An overlay node (`ui:Dialog`/`ui:Sheet`/`ui:Toast`) appears in the hop tree with props `{open: bool, title, …, children}`. The cover is a zero-size widget that, in a post-frame callback when `open` flips true, imperatively presents the ForUI overlay and renders its `children` via the same recursive `buildChild`. Buttons inside the overlay fire `ExperienceStep` like any button; the author's hop logic re-emits the hop with `open:false` (or advances) to dismiss. `ui:Toast` is fire-once (no dismiss round-trip). The new mechanism is entirely client-side: an imperative-present adapter keyed on `open` + a guard so a re-render doesn't double-present. Authoring: `Dialog(open, title, body)`, `Sheet(open, …)`, `Toast(message, …)`.

This is the only genuinely new code beyond thin covers, and it touches **no** Core wire types and **no** kernel protocol — it is a client adapter plus three builder methods.

## The gallery (`ui-gallery`)

A seeded `KitExperience` pack authored **with the kit**, openable full-screen from the marketplace exactly like `hello-world` (a `kind:"experience"` Run action routing to `/experience/ui-gallery/ui-gallery`).

- **Structure:** one hop per category (`inputs`, `layout`, `display`, `feedback`, `navigation`, `overlays`), with a `ui:Sidebar` (or `ui:Tabs`) whose items `goTo` each category hop — dogfooding navigation.
- **Each category hop:** a `ui:Screen` of `ui:Panel`s, one per component, each panel showing the component's `ui:Heading` (its `ui:Name`) above a live instance with representative props. Overlay components show a trigger `ui:Button` that opens the overlay.
- **Purpose:** (1) a real components gallery for authors to see what's available; (2) a broad render smoke-test — the E2E opens the gallery, walks the category hops, and asserts a representative node from each group renders.

The gallery pack is the proof that all 35 components compose through the standalone Roslyn/ALC pack path (explicit usings, no `\"` escapes in raw strings — string concat where a literal quote is needed).

## Folded-in Slice-0 follow-ups (cheap, done early)

1. **Forward `title` through the widget-tree bridge.** `UiSurfaceRfwBridge` widget-tree branch currently drops `title`; carry it so hops/overlays can show a heading.
2. **String-coerce `buildActionEnvelope` props.** Future-proofs non-string inputs (Checkbox/Slider/DateField) so the action envelope never ships a non-string and the dumb client never has to coerce. Done as the enabling change before the input batch.

## Testing & verification

- **Core (TDD, fast suite):** one builder/emit test per new `ui:` node — assert the fluent method produces the expected `UiWidgetTree` type + props; assert `Inject` stamps `pack`/`experienceId` on nav nodes; assert overlay nodes carry `open`. A gallery test asserts the `ui-gallery` pack source has explicit usings and `: KitExperience`, and that the seed is present.
- **Kernel:** extend the bridge marker test for `title` round-trip.
- **App (TDD):** one widget test per `ui_kit/ui_<name>.dart` cover (render + capture/event where applicable), plus registry tests asserting each `ui:<name>` maps to its cover. ForUI gotchas carried forward: theme `FThemes.neutral.light.touch`; `FTextField` change via `FTextFieldControl.lifted(value:, onChange:)`; `FButton` 100ms timer → tap then `pumpAndSettle()`.
- **E2E (gated, runs real):** open `ui-gallery` from the marketplace, walk the category hops via the sidebar, assert a representative component from each group renders via `flt-semantics-identifier`; keep the Hello World E2E green (regression guard).
- **Ritual per task:** `dotnet build`; targeted `dotnet test`; `flutter analyze` + `flutter test`. End of branch: `aspire doctor` + one intentional `aspire run` to watch the gallery render live.

## Edit-points map

**brain (`DigitalBrain.Core`):**
- `UiSurfaces.cs` — 30 new `ui:*` consts on the `Ui` class.
- `UiKit/UiExperience.cs` (`UiHop`/`UiExperience`) — one fluent method per new component (inputs, layout, display, feedback, nav, overlays).
- `UiKit/KitExperience.cs` — extend `Inject` to stamp nav + overlay-action nodes.
- `MarketplaceSeeds.cs` — seed the `ui-gallery` pack; `UiSurfaceLiveData` Run-to-route action for it.

**brain (`DigitalBrain.Kernel`):**
- `Ui/UiSurfaceRfwBridge.cs` — carry `title` on the widget-tree branch.

**brain (`DigitalBrain.Tests`):** Core builder/emit tests, bridge `title` test, `ui-gallery` pack source + E2E.

**app (`lib/ui_kit/`):** 30 new `ui_<name>.dart` covers; 30 new `ui_registry.dart` cases; an overlay imperative-present adapter; `buildActionEnvelope` string-coercion.

**app (tests):** widget test per cover + registry tests + gallery hop-render test.

## Execution shape (for the plan that follows)

Subagent-driven, fresh implementer per task + per-task spec/quality review + final whole-branch review (exactly like Slice 0). One task ≈ a small same-category batch, TDD:

0. Enabling follow-ups (bridge `title`, `buildActionEnvelope` string-coerce).
1. Inputs A — Checkbox, Switch, TextArea.
2. Inputs B — Select, RadioGroup, Slider, DateField.
3. Layout — Row, Column, Divider, Header, Gap.
4. Display A — Heading, Icon, Avatar, Badge.
5. Display B — Tile, List.
6. Feedback — Alert, Progress, Spinner, Tooltip.
7. Navigation A — Tabs, Breadcrumb, Pagination.
8. Navigation B — Sidebar, BottomNav.
9. Overlays — Dialog, Sheet, Toast (+ the imperative-present adapter).
10. Gallery — the `ui-gallery` pack + marketplace Run action + E2E.

Each batch lands its Core consts+builders, app covers+registry, and tests together, green before moving on.

## Branch & merge

Work on a feature branch off `master` (brain) / `main` (app). Finish with a **local** merge — **no push** (app push deploys digitalbrain.tech) unless the user says otherwise. See `[[merge-state-master-main-2026-06]]`.
