# Real-Grain Ino / `Neuron` Placement — Design Amendment

**Date:** 2026-07-01
**Status:** Approved for planning
**Amends:** `docs/superpowers/specs/2026-07-01-isolated-testable-marketplace-inos-design.md` ("the base spec")

## Why this amendment exists

Task 2 (`DigitalBrain.Windows`) of the implementation plan hit a blocker during implementation, independently
verified: the base spec's "Real-grain ino projects" table (line 34-41) claims every real-grain ino
(`DigitalBrain.Windows`, `.Developer`, `.Google`, `.Context`, `.UiKit`, `.Telegram.Channel`) depends on
`DigitalBrain.Core` only. But the grain classes they're meant to host (`FileSystemNeuron`, `WingetNeuron`,
`GmailNeuron`, `ContextNeuron`, `FlutterUiNeuron`, `TelegramChatNeuron`, …) must derive from `Neuron`, and
`Neuron`/`NeuronJournals` (`DigitalBrain.Kernel/Neuron.cs`) are built on Orleans's alpha
`Journaling`/`DurableGrain` APIs (`Microsoft.Orleans.Journaling`), a package `DigitalBrain.Core` does not
reference and never has. The base spec never accounts for this — it describes the intended dependency
direction ("Free to reference each other... Hosted by Kernel... via a normal ProjectReference") without
verifying `Neuron`'s actual location. This makes the base spec's Tasks 2/3/4/5/6/8 uncompilable as written.

`DigitalBrain.Core.csproj`'s own description already states the boundary this amendment preserves:
*"Pure stable layer... Kernel-specific surfaces and runtime live in kernel package."* `Neuron` is runtime.
It stays in Kernel. This amendment does not move it.

## The fix: split each real-grain ino's responsibility

For every real-grain ino, split what currently lives in one file (or would have, per the base spec) across
two locations by what actually requires Orleans:

- **The ino project** (`DigitalBrain.Windows`, `.Developer`, `.Google`, `.Context`, `.UiKit`,
  `.Telegram.Channel` — depends on `DigitalBrain.Core` only, exactly as the base spec intended): the public
  grain interface (`IFileSystemNeuron`, `IGmailNeuron`, …) — unchanged, since `INeuronAgent`/`INeuron` are
  already pure `DigitalBrain.Core` types requiring nothing from `Neuron` — plus any real capability logic
  that does **not** require deriving from `Neuron`: file I/O, external HTTP calls, vector-store queries,
  process invocation. Where that logic is non-trivial, it becomes a plain class (optionally behind a small
  interface when a test needs to fake it — this is exactly the pattern the base spec's Task 8 already used
  for `IGmailApiClient`/`GoogleGmailApiClient`, now applied consistently everywhere). Already-plain helpers
  (`ProcessRunner`) are unaffected.
- **`DigitalBrain.Kernel`**: the concrete `[GrainType]`-attributed class deriving from `Neuron`, implementing
  the ino's interface. Where the ino holds a plain logic class, the Kernel-side grain is thin —
  constructor-injects it and delegates each interface member. Where a neuron's behavior is inherently
  Orleans/journal-shaped (subscribing to signals, stateful routing, referencing other Kernel-only broadcast
  infrastructure), that logic stays directly in the Kernel-side grain — there is no value in an artificial
  extraction with nothing real behind it.

## Why this resolves the blocker without new machinery

- `Neuron`/`NeuronJournals` never move. Core's dependency footprint is unchanged from the base spec
  (still just `Microsoft.Orleans.Core.Abstractions` + `Microsoft.Orleans.Serialization`). `Mcp.Tools`, which
  references only Core today, gains nothing new.
- Kernel's `ProjectReference` to each ino stays exactly as the base spec already planned in every task step —
  only the justification shifts, from "grain-assembly discovery" to "Kernel's grain classes need the ino's
  interface/logic types." No inversion of the reference direction, no Orleans `ApplicationPartManager` /
  runtime assembly-loading mechanism needs inventing.
- `DigitalBrain.TestKit` already depends on `DigitalBrain.Kernel` (landed in Task 1). A
  `DigitalBrain.Windows.Tests` project referencing `TestKit` transitively loads Kernel's assembly — and
  therefore the concrete `FileSystemNeuron` grain within it — into the `TestCluster`.
  `_brain.Grain<IFileSystemNeuron>("fs-test")` resolves to Kernel's concrete grain exactly as the base
  spec's plan text for Tasks 2/3/4/5/6/8 already wrote it. **None of the plan's existing test code changes.**
  Only each task's "what moves where" file-placement steps change.

## Honest limitation

Not every real-grain ino ends up meaningfully "isolated" by this split — it depends on how much of the
neuron's real behavior is separable from Orleans:

- **Windows / Developer / Context / Google**: genuine win. Real capability logic (file I/O, git/dotnet/nuget
  process wrapping, Qdrant vector-store calls, Gmail/Drive/Calendar HTTP calls) moves out of Kernel into an
  isolated, Core-only project with zero-Orleans unit tests possible against the plain logic class directly,
  in addition to the TestKit-based grain-level tests the base spec already planned.
- **UiKit / Telegram.Channel**: smaller win. `FlutterUiNeuron`'s body already calls
  `ServiceProvider.GetService<HomeFeedBus>()` and `UiSurfaceRfwBridge.FromUiSurface(...)` — both staying in
  Kernel per the base spec's own explicit scoping ("not `HomeFeedBus`/`ChatNeuron`/`SignalEgressBus`/stream
  subscribers/`UiSurfaceRfwBridge`, which stay in Kernel as cross-cutting broadcast infra"). Its grain body
  is inherently Kernel-coupled. Same for `TelegramChatNeuron`'s stateful, journal-derived binding logic.
  These two inos end up holding mostly just their public interface. This is still worth doing — it removes
  the interface from `Core`, giving other consumers a small, stable, generic-free dependency surface — but
  it does not achieve the "fully isolated, independently unit-testable capability logic" outcome that
  Windows/Developer/Context/Google get. Disclosed here rather than silently under-delivered.

## Per-task file placement (revises the base spec's Tasks 2/3/4/5/6/8)

Only the **Files** / move-instructions in each task change. Interfaces, csproj shapes (Core-only for the
ino), Kernel wiring direction (`Kernel → ino` `ProjectReference`), and all test code are otherwise
unchanged from the base spec.

| Task | → Ino project (Core only) | → Stays/moves to `DigitalBrain.Kernel` |
|---|---|---|
| 2 (Windows) | `IFileSystemNeuron`, `IWingetNeuron`, `IShellNeuron` (interfaces, unchanged); `ProcessRunner` (unchanged, already plain); **new** `FileSystemOperations` (extracted from `FileSystemNeuron`'s current body — real `System.IO` calls) | `FileSystemNeuron`, `WingetNeuron`, `ShellNeuron` (grain classes) stay in `DigitalBrain.Kernel/Sdk/`, gain `using DigitalBrain.Windows;`. `FileSystemNeuron` delegates to the ino's `FileSystemOperations`; Winget/Shell stay as literally-written one-line `ProcessRunner` wrappers — no artificial extraction, there is no logic to move |
| 3 (Developer) | `IGitNeuron`, `IDotNetNeuron`, `INuGetNeuron`, `IRoslynNeuron` (interfaces); non-trivial logic extracted to plain classes where real (e.g. Roslyn's `MSBuildWorkspace` analysis) | `GitNeuron`, `DotNetNeuron`, `NuGetNeuron`, `RoslynNeuron` (grain classes) stay in `DigitalBrain.Kernel/Sdk/`, reference `DigitalBrain.Windows` (for `ProcessRunner`) and `DigitalBrain.Developer` (for interfaces/extracted logic) |
| 4 (Context) | `IContextNeuron` interface; `ContextServices`, `DocumentIngestor`, `HybridScorer`, `QdrantVectorStore`, `VectorStore` (already largely Orleans-independent — verify each against its real body before finalizing, per the base spec's existing Task 4 caveat) | `ContextNeuron` (grain class, including Task 9's later `IHandle<Signal>` addition) moves to/stays in `DigitalBrain.Kernel`, referencing `DigitalBrain.Context` |
| 5 (UiKit) | `IFlutterUiNeuron` interface only (see Honest Limitation) | `FlutterUiNeuron` (grain class) stays in `DigitalBrain.Kernel/Ui/`, referencing `DigitalBrain.UiKit` for the interface. `HomeFeedBus`/`HomeFeedStreamSubscriber`/`SignalEgressBus`/`SignalEgressStreamSubscriber`/`UiSurfaceRfwBridge` stay in Kernel exactly as the base spec already scoped |
| 6 (Telegram.Channel) | `ITelegramChatNeuron` interface only (see Honest Limitation) | `TelegramChatNeuron` (grain class) stays in `DigitalBrain.Kernel/`, referencing `DigitalBrain.Telegram.Channel` for the interface |
| 8 (Google) | `IGmailNeuron`/`IGoogleDriveNeuron`/`IGoogleCalendarNeuron` interfaces; `IGmailApiClient`/`IGoogleDriveApiClient`/`IGoogleCalendarApiClient` + `Google*ApiClient` real implementations; `GoogleCredentialFactory` — **all unchanged from the base spec**, these never needed `Neuron` | `GmailNeuron`, `GoogleDriveNeuron`, `GoogleCalendarNeuron`, `GoogleAuthNeuron` (grain classes) move to `DigitalBrain.Kernel` **instead of** staying in `DigitalBrain.Google` as the base spec's Task 8 literally wrote — this is the one correction to Task 8's file placement; everything else in Task 8 (API clients, credential factory, fakes, tests) is unaffected |

`DigitalBrain.Telegram` (pure-pack, Task 7) and `DigitalBrain.Experience.PersonalAssistant` (pure-pack,
Task 9) are **unaffected** — `IPackBehavior` implementations never derive from `Neuron`, so they were never
blocked by this issue.

## What does not change

- The base spec's Google integration specifics, marketplace-seed section, `Brain.slnx`/`Directory.Packages.props`
  wiring direction, `DigitalBrain.TestKit` design, and pure-pack-ino sections all stand as written.
- `Global Constraint`: `DigitalBrain.Core` ends the plan with zero references to any specific
  integration/vendor — unaffected; this amendment doesn't touch Core at all.
