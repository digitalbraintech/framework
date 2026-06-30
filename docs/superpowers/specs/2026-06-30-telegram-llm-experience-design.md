# Design: Telegram LLM Bot as an Installable, Self-Configuring Experience

**Date:** 2026-06-30
**Repo:** `digitalbraintech/framework` (`brain/`), with a thin slice in `digitalbraintech/app` (`app/`)
**Status:** Design — pending user approval before planning

---

## 1. Goal

Ship a Telegram bot as a **marketplace experience**: you install it, it asks you for a
bot token and (optionally) an LLM key via an in-app form, and from that moment it answers
your Telegram messages using an LLM — **local Ollama by default**, no AppHost restart.

The experience is the forcing function for three reusable capabilities the runtime is
missing or only sketches today:

1. **Install-time config/secrets prompting** for packs (the keystone gap).
2. A **capability-transport** pattern that lets a sandboxed pack participate in real-world
   I/O without breaking the sandbox.
3. A worked example of **reactive-on-install** neurons reacting to broadcast synapses.

This document is scoped to the Telegram experience. The architecture/distribution
assessment and the reactive-neuron use cases are folded in where they bear on the build.

---

## 2. Constraints that shape the design (non-negotiable facts)

- **The sandbox.** A `NeuroPack` is compiled C# scanned by `CapabilityGate`, which forbids
  `System.Net`. A pack therefore *cannot* host a webhook, call `api.telegram.org`, or hold a
  network connection. Telegram network I/O **must** live outside the sandbox.
  (`DigitalBrain.Kernel/Foundry/PackAlcEmbodier.cs`, `CapabilityGate`.)
- **Aspire resources are static.** "Install and it is instantly live, no restart" exists
  **only at the neuron/pack layer** (ALC embodiment + timeline broadcast — proven by
  `DigitalBrain.Tests/Distribution/HandlerGrowthTests.cs`). Adding an executable/container to
  the AppHost resource graph requires a rebuild + restart.
- **The transport↔kernel boundary is already string-typed.** `Send` / `UiGatewayService`
  cross a `synapseType` *name* + JSON props, not a compile-time type
  (`DigitalBrain.Kernel/Gateway/UiGatewayService.cs`). Domain synapse types never need to
  exist in `Core` or `Kernel`.
- **`DigitalBrain.Core` is pure protocol.** `Neuron`, `Synapse`, `IHandle<>`, marketplace /
  trust contracts only. No domain types. Telegram contracts do **not** go here.

The consequence: the part that *must* be a resource (network I/O) is the part that *cannot*
hot-install. So the bot is split — a dumb always-on pipe + a smart hot-installed brain.

---

## 3. Architecture

**Governing rule: dependency inversion.** The kernel is a **generic synapse router/host with
zero domain knowledge**. Every Telegram-specific atom — synapse types, transport, responder,
config schema — lives in `DigitalBrain.Telegram` and *depends on* the kernel's type-agnostic
abstractions. The dependency arrow points **one way**: `DigitalBrain.Telegram → kernel`. The
kernel never references the package. Swap Telegram for Discord and **not one kernel line
changes**.

```
        DEPENDENCY DIRECTION:  DigitalBrain.Telegram ──▶ generic kernel/core abstractions
                               (kernel/core know NOTHING about Telegram)

┌─ kernel — a generic synapse router/host, zero domain knowledge ───────────────┐
│  • Send(envelope{ typeName, propsJson })   → fire ANY named synapse onto the   │
│                                              timeline. NO per-type switch.      │
│  • WatchSynapses(typeFilter[]) → stream<envelope>   ← generic outbound;         │
│                                              generalizes today's HomeFeedBus.   │
│  • manifest dispatch: deliver a named synapse to any grain that declared        │
│                       IHandle<thatName>     (already exists, string-keyed)       │
│  • PackConfigStore + generic ConfigForm-from-manifest   (generic, not Telegram) │
└────────────────────────────────────────────────────────────────────────────────┘
                          ▲                              ▲
              Send / WatchSynapses (gRPC)     embodied as a pack (ALC), manifest-routed
                          │                              │
┌─ DigitalBrain.Telegram — owns 100% of Telegram, depends only on the above ─────┐
│  telegram-transport  (Aspire resource; real AddTelegramBot; the ONLY System.Net)│
│     • boots NO-OP until a token exists (idle, ~0 cost)                           │
│     • inbound:  webhook JSON → Send("TelegramMessageReceived", props)            │
│     • outbound: WatchSynapses(["TelegramReplyRequested",                         │
│                                "ConfigurationProvided"@self]) → setWebhook +     │
│                                                                   sendMessage    │
│                                                                                  │
│  TelegramMessageReceived, TelegramReplyRequested   (synapse types)               │
│                                                                                  │
│  TelegramResponderNeuron  (ships as pack.Code; hot-installed; runs in brain)     │
│     IHandle<TelegramMessageReceived> → IChatClient → TelegramReplyRequested      │
│                                                                                  │
│  manifest.RequiredConfig = [ telegram_token, llm_provider, llm_key ]             │
└──────────────────────────────────────────────────────────────────────────────────┘
```

### 3.1 Why this keeps the kernel clean (the objection, answered)

- **The kernel never "handles" `TelegramMessageReceived`.** It fires a *string-named* synapse
  onto the timeline and delivers it to whatever grain registered `IHandle<that-name>`. The
  type is resolved inside the pack's ALC, not by the kernel.
- **The responder's code is 100% in the package** (`pack.Code`). The kernel only *hosts* it via
  generic ALC embodiment + manifest dispatch — like a runtime executing a plugin. No Telegram
  code, types, or routing in the kernel.
- **The transport reads its config the same generic way** — it subscribes to the generic
  `ConfigurationProvided` synapse filtered to its own pack, reads `telegram_token`, calls
  `setWebhook`. The kernel never reaches "into" the transport.
- **Only two genuinely new kernel abstractions, both type-agnostic:** switch-free `Send` (fire
  any named synapse) and `WatchSynapses(typeFilter)` (generalize `HomeFeedBus` from "RFW cards"
  to "any synapse by type"). Everything else already exists.

### 3.2 Governing principle (the distribution assessment)

A sandboxed pack **declares needs** — *required config* and *required capabilities* — and the
kernel / transports **satisfy** them generically. The pack only ever calls injected
interfaces (`IChatClient`); the actual networking lives in always-on transports. **The sandbox
stays intact, and the kernel stays domain-free.**

Telegram is the *first* capability-transport. Email / HTTP-fetch / Discord later follow the
identical shape, each as its own package depending on the same generic abstractions. We are
**not** building those now (Musk step 1: no speculative hedges); we are only making sure
Telegram's shape is the reusable one.

### 3.3 Adjacent simplification (Musk step 1: delete the dumb requirement)

Today's `Send` path has a `switch(synapseType)` (`InoRequest`, `LoginRequest`,
`PublishToMarketplace`, …) — the *same* anti-pattern: the kernel hardcoding knowledge of
specific message types. The clean end-state deletes that switch in favour of pure generic
manifest dispatch. Fully doing so is out of scope here, but **the Telegram path must use the
generic fire-onto-timeline route and must not add a case to that switch.**

---

## 4. New / changed contracts

### 4.1 Config-prompting primitive (reusable — every future experience inherits it)

In `DigitalBrain.Core/Distribution/IPackBehavior.cs`, `PackManifest` gains:

```csharp
IReadOnlyList<PackConfigField> RequiredConfig { get; }   // default: empty

record PackConfigField(
    string Key,
    string Label,
    PackConfigFieldKind Kind,            // Text | Secret | Choice
    IReadOnlyList<string>? Choices = null,
    string? DependsOnKey = null,         // simple conditional (e.g. llm_key when provider=openai)
    string? DependsOnValue = null);
```

`PackConfigField` and `PackConfigFieldKind` are **protocol-level** (generic, no domain
meaning) so they legitimately live in `Core` — unlike the Telegram synapses.

New protocol synapse (generic, `Core`): `ConfigurationProvided(PackName, Values)` — emitted
by the config-form submit. Secret values are never logged.

### 4.2 Telegram synapses (in `DigitalBrain.Telegram`, NOT Core)

```csharp
[GenerateSerializer] record TelegramMessageReceived(long ChatId, long FromUserId, string Text, long UpdateId) : Synapse;
[GenerateSerializer] record TelegramReplyRequested(long ChatId, string Text, long? ReplyToMessageId = null) : Synapse;
```

### 4.3 Telegram responder pack (the only genuinely new behavior — tiny)

```csharp
public sealed class TelegramResponderNeuron : IPackBehavior
{
    // GetManifest(): handles TelegramMessageReceived;
    //   RequiredConfig = [ telegram_token(Secret), llm_provider(Choice:[ollama,openai]), llm_key(Secret, when provider=openai) ]
    public async Task<Synapse?> HandleAsync(Synapse s, IPackServices svc) =>
        s is TelegramMessageReceived m
            ? new TelegramReplyRequested(m.ChatId, await svc.Chat.AskAsync(m.Text))
            : null;
}
```

`svc.Chat` is the kernel-resident `IChatClient`, already configured for this scope from the
stored config (Ollama by default). No `System.Net` in the pack → passes `CapabilityGate`.

> **Plan-time, not settled:** the signature above is illustrative. The real `IPackBehavior`
> exposes `Handle` / `Respond` / `GetManifest` (`DigitalBrain.Core/Distribution/IPackBehavior.cs`),
> and there is **no established mechanism today for injecting `IChatClient` into a pack**. How a
> hot-installed pack obtains a scope-configured `IChatClient` (constructor injection vs. an
> ambient `IPackServices` passed to `Handle`) must be designed against the current interface in
> the implementation plan — it is a real open question, not a detail.

---

## 5. Flows

### 5.1 Install → configure → live (no restart)

1. User installs the experience (`InstallFromMarketplace`). Signature verified
   (`PackSignatureVerifier`).
2. Kernel sees `manifest.RequiredConfig` is non-empty → maps fields to a `UiWidgetTree`
   using the existing **ui: kit** (`ui:TextField` for Text/Secret, `ui:Select` for Choice,
   `ui:Button` submit) → broadcasts via `HomeFeedBus` (the same path marketplace/shell use).
3. User fills the form in the Flutter app → submit fires `ConfigurationProvided` →
   `PackConfigStore` persists encrypted values.
4. Activation, all via **generic** mechanisms (no kernel code knows it is Telegram):
   - The transport is already subscribed to `ConfigurationProvided` (filtered to its own pack)
     via `WatchSynapses`. It receives `telegram_token` and calls Telegram `setWebhook` **at
     runtime** (an API call, not a resource change → no restart).
   - `TelegramResponderNeuron` is embodied (`GeneratedNeuron`) and registers
     `IHandle<TelegramMessageReceived>` in the manifest. Its `IChatClient` is built from the
     stored `llm_provider` / `llm_key` for this scope (mechanism = the §4.3 open question).
5. A Telegram message now flows entirely through generic routing: webhook →
   `Send("TelegramMessageReceived")` → timeline → manifest-dispatched to the responder →
   `IChatClient` → `TelegramReplyRequested` → `WatchSynapses` delivers it to the transport →
   `sendMessage`. **The kernel handled two anonymous named synapses; it never knew "Telegram".**

### 5.2 Transport (harvest, do not write from scratch)

Lift from `Projects/ino/clients/Telegram/` (~200 lines, battle-tested):
`WebhookSetupService` (optional-token boot + ngrok auto-discovery for local dev + webhook
registration), `TelegramBotService`, `TelegramBotOptions`. Rewire its "forward to silo" from
ino's `ChatRequest` to the generic `Send("TelegramMessageReceived", props)`, and replace its
reply path with a generic `WatchSynapses(["TelegramReplyRequested", "ConfigurationProvided"])`
consumer. Make `AddTelegramBot` in `DigitalBrain.Aspire` real (it points at this project). The
transport lives in `DigitalBrain.Telegram` and depends only on the generic kernel gRPC surface.

---

## 6. Verification contract — "tests only, no manual smoke"

Per explicit decision, the dev loop is **100% green tests**, nothing manual. Three layers:

1. **Reqnroll over `TestCluster` (written FIRST, as the executable spec):**
   install → assert ConfigForm UiSurface carries the 3 declared fields → provide config →
   assert stored → fire `TelegramMessageReceived` → assert `TelegramReplyRequested` carries
   the stubbed LLM text. Proves the entire reactive loop deterministically.
   (Pattern: `DigitalBrain.Tests/Features/*.feature` + `Steps/*.cs`.)
2. **Transport contract tests:** a **deterministic fake Telegram server** seeded with
   **recorded real webhook payloads**; assert payload→synapse mapping and the exact
   `sendMessage` request shape. This is what makes "no manual smoke" safe — the one thing
   tests usually miss (wrong API shape / payload mapping) is caught at the contract boundary.
3. **Flutter tree test:** feed the ConfigForm `UiWidgetTree` to `UiSurfaceTreeRenderer`,
   assert token/provider/key fields render, simulate submit, assert the action envelope.
   (Pattern: `app/test/features/experience/experience_hop_view_tree_test.dart`.)

**Residual risk (stated, not tested):** real token validity and public webhook reachability
only fail in production. Accepted per decision. LLM is stubbed in tests (deterministic);
no live-Ollama E2E in the loop.

---

## 7. Reactive-neuron use cases (all reuse this spine)

1. **Live N+1 demo — second handler on the same broadcast:** a `KeywordWatcherNeuron` that
   *also* `IHandle<TelegramMessageReceived>` and turns "remind me…" into a `ReminderScheduled`
   synapse. Two installed neurons reacting to one broadcast → proves N+1 without restart,
   visibly. **This is the proof scenario for slice 4.**
2. **Reacting to *system* synapses, not just Telegram:** an `InstallAnnouncerNeuron` that
   `IHandle<NeuroPackInstalled>` → `TelegramReplyRequested("your brain just learned X")`.
   Shows an installed neuron reacting to internal events and reusing the transport.
3. **(stretch) Daily digest:** `IHandle<TimerTick>` → summarize recent `ExperienceUsed` →
   push to Telegram.

---

## 8. Simplification (Musk steps 1–2 — delete before building)

- Delete the `AddTelegramBot` `echo` placeholder
  (`DigitalBrain.Aspire/DigitalBrainBuilderExtensions.cs` ~lines 164–177) → replace with the
  real transport integration.
- Replace the vague `MarketplaceSeeds.cs` Telegram paragraph with a real signed pack seed.
- **Net-new code is small:** ~50-line responder + the config primitive + recorded-payload
  tests. The transport is harvested; the ui: kit, embodiment pipeline, secret-capable
  storage substrate (Azurite), and broadcast bus already exist.

Out of scope (do not expand): the deprecated Flutter screens (`ino_chat_screen`, static
gallery/marketplace) — noted only as future cleanup, untouched here.

---

## 9. Decisions still to confirm at spec review

- **Secret storage:** kernel-side **DataProtection-encrypted store**, persisted to the
  Azurite blob already in the AppHost, keyed by `(scope, pack, key)` — chosen over Aspire
  user-secrets so it works for multi-brain / production, not just local dev.
- **Slicing order:** config primitive first (below), vs. Telegram loop end-to-end with
  hardcoded config first then retrofit the form.

---

## 10. Build order (each slice ends green)

0. **Generic kernel abstractions** (the only kernel changes, both domain-free) —
   switch-free generic `Send` (fire any named synapse onto the timeline) and
   `WatchSynapses(typeFilter[]) → stream<envelope>` (generalize `HomeFeedBus`). Proven with a
   throwaway test synapse, *no Telegram involved* — confirms the kernel stayed domain-free.
1. **Config primitive** — `RequiredConfig` on manifest, kernel form emission, `PackConfigStore`,
   `ConfigurationProvided`. (Reqnroll + Flutter tree test.)
2. **Synapses + responder** — `DigitalBrain.Telegram` contracts, `TelegramResponderNeuron`,
   embodiment, reactive-loop BDD scenario (LLM stubbed). Routed purely through slice 0.
3. **Transport** — harvest ino, make `AddTelegramBot` real, wire it to `Send` / `WatchSynapses`,
   contract tests vs the fake Telegram server with recorded payloads.
4. **Reactive use case #1** — the `KeywordWatcherNeuron` N+1 proof scenario (a *second*
   `IHandle<TelegramMessageReceived>` grain on the same broadcast).
