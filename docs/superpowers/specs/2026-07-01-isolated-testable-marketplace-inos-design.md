# Isolated, Testable Marketplace Inos — Design

**Date:** 2026-07-01
**Status:** Approved for planning

## Core Law (restated, the governing constraint)

Everything is a Neuron or a Synapse. There is no tier of "platform substrate" vs "marketplace content" —
Winget, FileSystem, Git, Telegram, Google, Context, and UI-Kit are all **inos**: self-contained bundles of
neurons + synapses, each its own project, each depending on `DigitalBrain.Core` and nothing else for
contracts. The only real axis that matters is **embodiment mechanism**, because it's a genuine technical
constraint, not a value judgement:

- **Real-grain inos** — statically compiled `INeuron`/`Neuron` subclasses, real DI, real async I/O
  (`HttpClient`, `Process`, Qdrant, Google APIs, …). Hosted by `DigitalBrain.Kernel` (the Orleans silo,
  now a thin composition root) via a normal `ProjectReference` so Orleans can discover the grain types.
  Free to reference each other and third-party packages.
- **Pure-pack inos** — `IPackBehavior` implementations, dynamically Roslyn-compiled + loaded into a
  collectible `AssemblyLoadContext` at marketplace-install time (`PackAlcEmbodier`). Constrained to
  depend **only** on `DigitalBrain.Core` because they must be embeddable as self-contained source. They
  can never hold a real `HttpClient`/DI service, so they reach real-grain capability only by firing a
  generically-named `Signal` and reacting to the reply — exactly the existing `AskLlm`/`LlmResponderNeuron`
  pattern, extended to every other real-grain ino.

`DigitalBrain.Core` stays 100% generic: `Synapse`, `Signal`, `AskLlm`, `IPackBehavior`, `INeuron`,
`IHandle<T>`, `UiSurface`, `IChannelNeuron` (marker only), `INeuronAgent` (marker + metadata pattern),
`CommandResult` (generic process-result shape), `BundleManifest`/`NeuroPack`/marketplace catalog types.
Nothing vendor- or integration-specific lives there after this migration.

## Project inventory

### Real-grain ino projects (new or migrated out of Kernel/Core)

| Project | Contents | Depends on |
|---|---|---|
| `DigitalBrain.Windows` (new) | `IFileSystemNeuron`+`FileSystemNeuron`, `IWingetNeuron`+`WingetNeuron`, `IShellNeuron`+`ShellNeuron`, `ProcessRunner` | Core |
| `DigitalBrain.Developer` (new) | `IGitNeuron`+`GitNeuron`, `IDotNetNeuron`+`DotNetNeuron`, `INuGetNeuron`+`NuGetNeuron`, `IRoslynNeuron`+`RoslynNeuron` | Core, `DigitalBrain.Windows` (for `ProcessRunner`) |
| `DigitalBrain.Google` (new) | `IGmailNeuron`/`IGoogleDriveNeuron`/`IGoogleCalendarNeuron` + real grains, `I*ApiClient` wrapper interfaces + `Google*ApiClient` real impls, `GoogleCredentialFactory` (PackConfigStore → `UserCredential`), sign-in `UiSurface` builder | Core, `Google.Apis.Gmail.v1`/`Drive.v3`/`Calendar.v3`/`Auth` |
| `DigitalBrain.Context` (moved) | `IContextNeuron`+`ContextNeuron`, `ContextServices`, `DocumentIngestor`, `HybridScorer`, `QdrantVectorStore`, `VectorStore` | Core, Qdrant/AI packages |
| `DigitalBrain.UiKit` (moved) | `IFlutterUiNeuron`+`FlutterUiNeuron` only — **not** `HomeFeedBus`/`ChatNeuron`/`SignalEgressBus`/stream subscribers/`UiSurfaceRfwBridge`, which stay in Kernel as cross-cutting broadcast infra used by many neurons beyond this one channel | Core |
| `DigitalBrain.Telegram.Channel` (moved) | `ITelegramChatNeuron`+`TelegramChatNeuron` (the binding/routing grain) | Core |

`IChannelNeuron` (pure marker, zero members) stays in `DigitalBrain.Core/Synapse.cs` — it's genuinely
generic. `ITelegramChatNeuron`, `IFlutterUiNeuron`, `IContextNeuron` move out of `Synapse.cs` into their
respective projects above.

### Pure-pack ino projects

| Project | Contents | Depends on |
|---|---|---|
| `DigitalBrain.Telegram` (existing, unchanged) | `TelegramResponderNeuron : IPackBehavior` | Core only |
| `DigitalBrain.Experience.PersonalAssistant` (new) | `PersonalAssistantNeuron : IPackBehavior` — multi-hop: recall context → augmented `AskLlm` → text reply or visualize | Core only |

`PersonalAssistantNeuron` composes Telegram + Context + Google + UiKit **only by generic `Signal` name**
(`ContextSignals.RecallRequested`, `AskLlm`, `GoogleSignals.GmailFetchRequested`, `TelegramSignals.*`) —
never a typed reference to those projects, since it must stay Core-only for ALC embodiment.

**Clarification on signal-name constants:** these are plain `string` constants (`GoogleSignals.AuthRequested
= "GoogleAuthRequested"`, etc.), not typed synapse contracts, and they live in `DigitalBrain.Core/Signals.cs`
alongside the existing `TelegramSignals`/`UiSignals` — exactly the precedent item 10 of the prior plan
already established. A string constant naming a well-known event is not the same as Core knowing what
Gmail is; Core still never references `Google.Apis.*` or any vendor type. Add `GoogleSignals` and
`ContextSignals` to `Signals.cs` following that pattern.

### Shared test infrastructure (new)

`DigitalBrain.TestKit` — depends on Core + `DigitalBrain.Kernel` + every real-grain ino project (Windows,
Developer, Google, Context, UiKit, Telegram.Channel) + Orleans `TestingHost`. Wraps `TestClusterBuilder` +
`NeuronTestSiloConfigurator` behind a minimal façade:

```csharp
public interface IDigitalBrain
{
    Task FireAsync<T>(T synapse) where T : Synapse;
    TGrain Grain<TGrain>(string key) where TGrain : IGrainWithStringKey;
}

public sealed class TestDigitalBrain : IDigitalBrain, IAsyncLifetime
{
    public TestDigitalBrain(Action<ISiloBuilder>? extend = null); // lets an ino's test project add its own config
    // internally: TestClusterBuilder + NeuronTestSiloConfigurator; ino tests never touch TestCluster directly
}
```

Every real-grain ino gets a sibling `DigitalBrain.<Name>.Tests` project referencing `DigitalBrain.TestKit`
+ its own ino project — never the central `DigitalBrain.Tests`. Example:

```csharp
public class FileSystemNeuronTests(IDigitalBrain brain)
{
    [Fact] public async Task Write_Read_RoundTrip()
    {
        var fs = brain.Grain<IFileSystemNeuron>("fs-test");
        await fs.WriteFileAsync(...);
        Assert.True(await fs.ExistsAsync(...));
    }
}
```

Pure-pack inos get a sibling `.Tests` project too, but with **zero** infrastructure — Reqnroll or xUnit
step defs call `new PersonalAssistantNeuron().Handle(synapse)` directly, no TestKit, no Orleans.

Reqnroll `.feature` files co-locate per-ino inside that same sibling `.Tests` project
(`DigitalBrain.Google.Tests/Features/GmailAuth.feature`), not centralized. The central `DigitalBrain.Tests`
project shrinks to only genuinely cross-ino concerns: Playwright E2E, and composition proofs that don't
belong to one ino (e.g. "Telegram signal → Chart → FlutterUiNeuron").

## Google integration specifics

- `IGmailNeuron`: `ListMessagesAsync(query, maxResults)`, `ReadMessageAsync(messageId)`,
  `SendMessageAsync(to, subject, body)`.
- `IGoogleDriveNeuron`: `ListFilesAsync(query)`, `UploadFileAsync(name, content, mimeType)`,
  `DownloadFileAsync(fileId)`, `DeleteFileAsync(fileId)`.
- `IGoogleCalendarNeuron`: `ListEventsAsync(timeMinIso, timeMaxIso)`,
  `CreateEventAsync(summary, startIso, endIso, description)`, `DeleteEventAsync(eventId)`.
- Each real grain depends on a thin per-agent wrapper interface (`IGmailApiClient`, …) rather than the raw
  `GmailService`/`DriveService`/`CalendarService` — real impl wraps `BaseClientService.Initializer` +
  `HttpClientInitializer` credential (verified via Context7), tests inject a fake — no real network/OAuth
  in the automated suite.
- Auth reuses `PackConfigStore`'s encrypted-secret pattern exactly like Telegram's bot token
  (`client_id`/`client_secret`/`refresh_token` as `Secret`-kind `PackConfigField`s). `GoogleCredentialFactory`
  builds a non-interactive `UserCredential` from the stored refresh token — no browser consent flow.
- The "Sign in with Google" experience is a `UiSurface` (a `ui:Button`) built inside `DigitalBrain.Google`,
  whose `onClick` fires `Signal("GoogleAuthRequested", {...})`; the real grain (or a small responder inside
  the same project) reacts and eventually fires `Signal("GoogleAuthCompleted", {...})`. Any other ino —
  Telegram responder, PersonalAssistant, a future pack — reuses Google capability purely by firing/handling
  these well-known `Signal` names, with zero compile-time coupling.

## Marketplace seeds

Add a `DigitalBrain.Experience.PersonalAssistant` `NeuroPack` entry in `MarketplaceSeeds.cs`
(`BundleTier.Content`, `Channels: [Telegram]`, `Dependencies: [("DigitalBrain.Telegram.Responder","1.0.0"),
("DigitalBrain.UIKit.ForUI","0.1.0")]`). `BundleDependency` remains descriptive-only (not enforced —
confirmed no code anywhere reads `.Dependencies` today; Phase 2 dependency resolution is explicitly
deferred in the existing distribution spec) — this declares intent honestly without inventing new
enforcement machinery.

## Build/wiring changes

- `Brain.slnx`: + `DigitalBrain.Windows`, `DigitalBrain.Developer`, `DigitalBrain.Google`,
  `DigitalBrain.Context`, `DigitalBrain.UiKit`, `DigitalBrain.Telegram.Channel`,
  `DigitalBrain.Experience.PersonalAssistant`, `DigitalBrain.TestKit`, and one `.Tests` project per
  real-grain ino + the two pure-pack `.Tests` projects (14 new project entries total).
- `DigitalBrain.Kernel.csproj`: + `ProjectReference` to the 6 real-grain ino projects (for Orleans grain
  assembly discovery). Kernel's own `Sdk/`, and the moved files from `Context/`/`Ui/` (only
  `FlutterUiNeuron.cs`) and `TelegramChatNeuron.cs`, are deleted from Kernel once moved.
- `DigitalBrain.Core.csproj`: no new dependencies; four interfaces deleted from `Synapse.cs`.
- `Directory.Packages.props`: + `Google.Apis.Gmail.v1`, `Google.Apis.Drive.v3`, `Google.Apis.Calendar.v3`,
  `Google.Apis.Auth`.
- `DigitalBrain.Kernel/Program.cs` (or wherever grains are registered): no explicit per-grain registration
  needed beyond assembly reference — Orleans discovers `[GrainType]`-attributed classes automatically, same
  as today.

## Explicitly out of scope (deferred, not silently dropped)

- Eliminating the pack-source duplication between a real compiled class (e.g. `TelegramResponderNeuron` in
  `DigitalBrain.Telegram`) and its embedded string-const copy in `MarketplaceSeeds.cs` — accepted tradeoff
  already noted in prior work ("preserve self-contained pack compilation"); a single-source mechanism
  (e.g. build-time embedded resource) is separate scope.
  `PersonalAssistantNeuron` follows the same accepted duplication for consistency.
  Windows, Developer, Google, Context, and UiKit real grains are **not** dynamically-embodied packs and
  have no such duplication — this only applies to pure-pack inos.
- Interactive browser-based Google OAuth consent flow — config form accepts a pre-obtained refresh token,
  same as Telegram's token paste.
- Making the pre-existing empty-stub `DigitalBrain.Experience.GmailInsights` pack real — still just a
  catalog stub after this pass; the new `IGmailNeuron` makes it *possible* but wiring the pack itself is
  future work.
- Google Drive/Calendar UI beyond the shared sign-in button — no dedicated file-browser or calendar-view
  `UiSurface` yet.

## Verification plan

Standard ritual after the migration: `dotnet build` (0 errors across all 20+ projects), targeted
`dotnet test` per new/moved project plus the shrunk `DigitalBrain.Tests`, `aspire doctor`. Given the scale
(mechanical move of ~20 files across 6 new projects, plus ~2 new pure-pack/real-grain inos, plus TestKit),
the implementation plan should sequence: TestKit first (nothing else can be verified without it) → migrate
one real-grain ino at a time (build+test green before moving to the next) → build Google → build
PersonalAssistant → final full-solution build + full test pass → update `Brain.slnx`/docs.
