# In-Brain Pack Authoring ("DigitalBrain IDE" core loop) — Design

Status: DESIGN, not yet implemented. Follows the repo's `docs/specs/` + `docs/plans/` convention
(see `brain/docs/SYSTEM_DESIGN.md` §2.3) rather than a generic spec-doc location.

## 1. Motivation

The long-term vision (user's words): `ino` is a self-contained bundle of neurons and synapses that
encapsulates software-engineering complexity — logic, configuration, and self-tests — as one unit.
DigitalBrain should let a user program *itself* further, from inside the running system, without a
separate dev environment. The concrete example driving this design: marketplace has an X(Twitter)
integration installed as neurons (`XAccountNeuron("elonmusk")` firing `XAccount.Synapses.NewPost`); a
user should be able to say "when Elon posts, analyze how it affected Bitcoin" and get a real,
installed, reacting `IHandle<XAccount.Synapses.NewPost>` — authored, tested, and running — without
leaving DigitalBrain.

Two other use cases (Salesforce integration, Telegram admin-connect) motivated the same mental model
but are explicitly **out of scope for this spec** — see §7. This spec covers only the core authoring
loop, validated against the X→Bitcoin scenario.

## 2. Scope decisions (Musk's 5 steps, applied)

1. **Question requirements** — do we need new orchestration machinery? No: `ICodeGenNeuron` →
   `ICodeRunNeuron`/`ICodeDeployNeuron` (Tier-1/Tier-2) → `CodeFoundryClosedLoopNeuron` already exist,
   compile, and have per-component test coverage (`CodeFoundry.feature`, 7 scenarios) — confirmed by
   direct code research, not assumed from docs. The actual gap is that nothing chains
   describe→generate→verify→install into one story; no test exercises that end-to-end
   (`ask_ino`/`run_closed_loop`/`publish_to_marketplace` MCP tools exist and fire real synapses to real
   grains, but nothing connects them into a single flow). This spec is about wiring, not new
   substrate.
2. **Delete** — no new marketplace catalog path (personal packs never enter the public catalog); no
   new UI framework (the authoring surface reuses the existing server-driven `ui:*` kit surface
   mechanism that config-forms already use, and repurposes the app's existing — currently
   `.ino`-pointed — code-preview/syntax-highlight component instead of building a new editor); no new
   trust mechanism (reuses `PublisherTrust`/`TrustedPublisherKeys`, just adds one more trusted signer).
3. **Simplify** — the LLM authors *only* a narrow, pure, synchronous function —
   `string BuildAnalysisPrompt(TSynapse trigger)` — never the `IHandle<T>` dispatch/addressing code,
   and never the LLM call itself. This is narrower than it first looks: packs can't hold an
   `IChatClient` or await anything (`AskLlm` is fire-and-forget — the reply arrives *later* as a
   separate `Signal` broadcast, not a return value, per `brain/docs/SYSTEM_DESIGN.md` §1.8), so the
   fixed template — not generated code — fires the `AskLlm` intent (tagged with a correlation id +
   `OwnerId`) and, separately, implements `IHandle<Signal>` to catch the matching reply and deliver it
   point-to-point to the owner. The LLM's entire contribution is deterministic string-building; it
   never touches async flow, dispatch, or addressing.
4. **Accelerate** — reuses `BundleHarness` (in-process embodiment, no Aspire host, no browser) for
   both the Tier-1 sandbox test run and the pipeline's own test suite. Compile/test failures get one
   bounded LLM self-correction pass before bothering the human.
5. **Automate last** — auto-sign and auto-install only happen after generated tests genuinely pass in
   the sandbox. Nothing installs on the strength of the LLM's own claim that it worked.

**Explicitly deferred, not part of this spec:**
- `AskMarketData` (or similar live-external-data broker) — v1's analyzer is LLM-judgment-only ("does
  this post read as bullish/bearish for BTC, and why"), no live price number. Adding a live-data
  broker is a natural fast-follow once this loop is proven, mirroring `LlmResponderNeuron`'s shape.
- Broadcast-loop guard (depth/visited stamp) — already a known, previously-deferred gap (see
  `brain/docs/SYSTEM_DESIGN.md` §1.10). This spec's implementation plan should pull it forward as a
  **dependency**, not leave it deferred, because many freely-generated personal automations reacting
  to broadcasts raises the odds of an accidental self-triggering loop well above the one or two
  hand-built packs that exist today.
- Salesforce integration and Telegram admin-connect/miniapp — separate follow-on specs once this core
  loop is real (see §7).

## 3. Architecture

```
User (chat surface)
  │  "when Elon posts, analyze BTC impact"
  ▼
AuthorPackRequest synapse  →  CodeFoundryClosedLoopNeuron (keyed per authoring session:
  │                            "closedloop-" + SessionId, NOT the current shared "closedloop-main")
  ▼
ICodeGenNeuron.Generate()  →  { TriggerSynapseType, BuildPromptSource, TestCases[] }
  │   one LLM call, grounded against the installed-synapse catalog (reuses the existing catalog
  │   contract already surfaced to the app for .ino-editor autocomplete) so it resolves
  │   XAccount.Synapses.NewPost rather than hallucinating a type name
  ▼
PackDraftGenerated  →  code-preview UI (reusing the app's existing code-view component,
  │                     retargeted at C# instead of the dead .ino language)
  ▼  user reviews, optionally edits BuildPromptSource, fires PackDraftApproved
  ▼
Splice into PersonalAutomationTemplate  →  full IPackBehavior source
  (LLM writes ONLY the pure BuildAnalysisPrompt(trigger) body; the template supplies IHandle<T>,
   the AskLlm fire + IHandle<Signal> reply-catch, and — critically — always addresses the final
   output point-to-point to the owner, regardless of what BuildAnalysisPrompt returns)
  ▼
Tier-1 sandbox run: CapabilityGate.FindViolations, then PackAlcEmbodier.Embody in a throwaway ALC,
  │   then BundleHarness-style: fire each generated TestCase's sample trigger, assert
  │   BuildAnalysisPrompt's output contains the expected key details (deterministic string checks —
  │   this part of the pipeline has no LLM-judgment nondeterminism to hedge against)
  ├── compile error / test failure → bounded LLM self-correction retry (cap ~3), then surface
  │   diagnostics to the human for manual edit (see §5)
  ▼  all green
Sign with kernel's first-party authoring key (added to existing TrustedPublisherKeys allowlist)
  → OwnerId = CreatorId → private install (skips the public marketplace catalog entirely)
  ▼
GeneratedNeuron subscribes to the global broadcast timeline like any embodied pack
  │   real XAccount.Synapses.NewPost broadcasts → template invokes BuildAnalysisPrompt, fires AskLlm
  │   tagged with a correlation id + OwnerId
  ▼
Later: LlmResponderNeuron's reply arrives as Signal(ReplyType, props) — template's fixed
  IHandle<Signal> matches the correlation id and addresses the result point-to-point → owner's
  UserSessionNeuron and/or bound TelegramChatNeuron (per BundleManifest.Channels — existing delivery
  paths, nothing new)
```

## 4. Components

| Component | Status | Change |
|---|---|---|
| `AuthorPackRequest`, `PackDraftGenerated`, `PackDraftApproved` synapses | New | `DigitalBrain.Core/Distribution/Authoring.cs` |
| `CodeFoundryClosedLoopNeuron` | Existing | Re-key from shared `"closedloop-main"` to per-session `"closedloop-" + SessionId"`; extend handling for the new synapses |
| `ICodeGenNeuron` contract | Existing | Extend return shape to include `TestCases[]` alongside the generated `BuildAnalysisPrompt` source; ground trigger-type resolution against the installed-synapse catalog |
| `PersonalAutomationTemplate` | New | `DigitalBrain.Kernel/Foundry/PersonalAutomationTemplate.cs` — hand-written, reviewed once; wraps the LLM's pure prompt-building function into `IHandle<T>` (fires `AskLlm`) + `IHandle<Signal>` (catches the reply by correlation id); owns all output addressing, the LLM body never sees an async/dispatch surface |
| First-party authoring signing key | New | Kernel-held keypair, added to `TrustedPublisherKeys` config — reuses `PublisherTrust.IsTrusted` as-is |
| Private-install handler | New | Narrow path (near `MarketplaceNeuron`) that signs, sets `OwnerId`, embodies via existing `PackAlcEmbodier`, registers `GeneratedNeuron` — no marketplace catalog entry |
| Output delivery | Existing | `UserSessionNeuron` (in-app feed) / `TelegramChatNeuron` (if bound) — no new transport |
| Authoring UI surface | New (thin) | Server-rendered via existing `ui:*` kit mechanism; code-preview reuses app's existing (currently `.ino`-pointed) syntax-highlighted text component, retargeted at C# |
| Broadcast-loop guard | Existing gap, pulled forward | Depth/visited-stamp guard, previously deferred — now a dependency of this feature |

## 5. Error Handling

- **Compile errors**: one bounded LLM self-correction retry with the diagnostic fed back; if still
  broken, surface remaining diagnostics in the code-preview view for manual edit.
- **`CapabilityGate` violations**: rejected with a clear message. Since the LLM only ever authors a
  pure string-building function (no async, no I/O surface exposed to it at all), there's very little
  room for it to trip the gate in the first place — packs stay pure/sync/no-`System.Net`, matching
  the existing invariant. Live external data (e.g. a BTC price feed) is out of scope for v1 (§2).
- **Generated test failures**: same bounded self-correction loop (~3 attempts, feeding back the
  expected-vs-actual diff), then surface to the human.
- **Runaway/expensive logic**: hard timeout wraps every `BuildAnalysisPrompt` invocation, at both
  sandbox-test time and live-dispatch time — even a pure string-building function can contain a
  runaway loop, and this is now arbitrary LLM-authored code running in-process in the *shared* kernel.
- **Broadcast-loop risk**: covered by pulling the existing deferred guard forward as a dependency
  (§2, §4).
- **Known, accepted scaling characteristic (not a defect)**: dispatch is by synapse *type*, not
  payload content — two different users' personal analyzers both reacting to `NewPost` both receive
  *every* `NewPost` broadcast regardless of which X account posted, and must filter by
  account-handle inside their own logic. This is inherited from the existing broadcast design and is
  not something this feature needs to fix.

## 6. Testing

**Pipeline tests** (`DigitalBrain.Tests`, plain xUnit against `TestCluster`, following existing
convention — not new Reqnroll `.feature` files):
- Per-session `CodeFoundryClosedLoopNeuron` keying: two concurrent `AuthorPackRequest`s from different
  sessions don't collide or serialize on one grain.
- `PersonalAutomationTemplate` safety invariant (highest-value test in this spec): given any sample
  `BuildAnalysisPrompt` body and a fake `LlmResponderNeuron` reply, the compiled result always
  addresses its final output point-to-point to `OwnerId`, never `IsBroadcast=true` — regardless of
  what the body returns.
- Trust path: a pipeline-signed pack passes `PublisherTrust.IsTrusted` via the existing check; anything
  signed differently (or unsigned) is rejected by the same existing check — no bypass path to verify
  separately.
- Private-install path never creates a marketplace catalog entry (`ListMarketplace` doesn't surface it).
- Self-correction retries resolve within the cap (fake-LLM-backed test for both a broken-compile case
  and a failing-test case); exhausting the cap surfaces diagnostics rather than looping forever or
  installing something broken.
- End-to-end, in the fast inner loop via `BundleHarness` (not gated E2E): `AuthorPackRequest` → draft →
  approve → tests green → installed → real broadcast → private output delivered to the owner. This is
  the exact story confirmed missing from today's codebase during this session's research.

**Generated-pack tests** are the self-test mechanism itself (§3): since the generated code is just
deterministic prompt-building, its own tests can assert fairly directly ("the built prompt mentions
the post's text and the word Bitcoin") rather than needing fuzzy shape-matching — the nondeterminism
lives entirely in the downstream LLM judgment, which is the fixed template's territory, not
generated code's.

## 7. Explicitly out of scope (follow-on specs)

- **Salesforce integration** — once this loop exists, connecting Salesforce is "just" authoring a pack
  whose `RequiredConfig` captures OAuth credentials (existing `PackConfigStore`/`RequiredConfig`
  primitive, no new mechanism expected) — worth a short validation pass, not a new design, unless that
  validation surfaces a real gap.
- **Telegram admin-connect / miniapp** — the current `brain/` Telegram integration is bot-chat-only
  (webhook transport, `TelegramChatNeuron` deep-link binding). `Projects/ino` (archived) has a working,
  different precedent: a Telegram **Mini App** using the WebApp JS bridge + `initData` validation,
  serving the Flutter web bundle as the miniapp's content
  (`Projects/ino/clients/Telegram/Program.cs`). Whether to adopt that miniapp pattern in addition to
  (or instead of) the current chat-bot flow, and the bot-token-vs-Telegram-login auth question, is a
  real open decision deferred to its own spec.
- **`AskMarketData` / live external data broker** — deferred per §2.

## 8. Open follow-ups tracked, not blocking

- Broadcast-loop guard must land as part of (or before) this feature, not stay deferred (§2, §5).
- `ICodeGenNeuron`'s current exact contract/signature needs verification against live code during
  implementation planning — this spec describes the target shape, not a guaranteed current signature.
