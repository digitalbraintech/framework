# Bucket A — Runtime Hardening (Design)

**Date:** 2026-06-26
**Status:** Approved (design); pending implementation plan.
**Scope:** `brain/` only. Five hardening items that are fully build/test/`aspire doctor`-verifiable in this environment (pure C#, no external infra). Buckets B–F (WASM, live infra, Flutter render, Google auth, LLM behavioral tests) are out of scope for this cycle.

## Context

The pack→embody→dispatch core, marketplace, signing, economics, SDK, context, and server-driven UI are implemented and tested. What remains before a real pilot is production hardening. This spec covers the subset that needs no external systems. Audit findings that drive the design:

- **Seeds and the kernel pack are unsigned.** `DigitalBrain.Core/MarketplaceSeeds.cs` constructs `NeuroPack`s with no `AuthorPublicKeyBase64`/`SignatureBase64` and empty `Code`. `RejectUnsignedPacks` currently defaults **off** (warn-only). Flipping it on naively would reject seed installs and kernel self-update.
- **MCP tools are one partial class.** `DigitalBrainTools` (partials `.Neurons.cs`, `.Ui.cs`) exposes read and mutation tools together via `[McpServerTool]`. HTTP transport is co-hosted in the kernel but internal-only; there is no read/mutation separation.
- **Rolling self-update has only a happy path.** `AspireOrchestratorNeuron.PerformKernelSelfUpdate` does per-replica drain/verify/complete with checkpoints, but no rollback/abort on a failed replica.
- **N+1 handler growth is implicit.** No test explicitly asserts that installing a pack adds exactly one responder to a broadcast.
- **Checkpoint key source is hard-wired.** AES key comes from `DigitalBrain:Checkpoint:Key`; there is no provider abstraction for a cloud key source.

## Decisions (locked)

- Scope = Bucket A only this cycle.
- Available infra for validation: Docker + `aspire run`, Ollama, Flutter SDK. (No Stripe keys — not needed here.)
- MCP hardening = **read/mutation split**, no token auth yet. Mutation tools are removed from the HTTP transport; read-only tools remain remotely reachable. stdio keeps all tools.

---

## A1. Secure-by-default trust (reject unsigned ON)

**Goal:** Installs reject unsigned/untampered packs by default, while the system's own trusted packs (UI seeds, Telegram seed, kernel pack) still install.

**Approach:**
- Add a built-in **trusted-publisher** identity (`digitalbraintech`) with an ECDSA-nistP256 keypair. The public key is embedded/known to the kernel; the private key signs trusted artifacts at publish time. Reuse `PackSignatureVerifier` (ECDSA-nistP256) — no new crypto.
- Sign the `MarketplaceSeeds.LocalUiPacks` and the kernel pack with the trusted key at seed/publish time, so each carries valid `AuthorPublicKeyBase64` + `SignatureBase64`.
- Flip `RejectUnsignedPacks` **default → true** in `MarketplaceNeuron`'s config read (`DigitalBrain:Marketplace:RejectUnsignedPacks`). Setting it to `false` remains the documented dev escape hatch.

**Components touched:** `DigitalBrain.Core/Trust/*` (trusted key holder), `DigitalBrain.Core/MarketplaceSeeds.cs` (signed seeds), `DigitalBrain.Kernel/SystemNeurons.cs` (MarketplaceNeuron default + kernel-pack signing on publish).

**Tests:**
- Unsigned pack rejected with **default** config (no opt-in).
- Trusted seed pack and kernel pack install successfully (signature verifies) under default config.
- Tampered trusted pack rejected.
- Existing "strict config enabled" test updated to reflect strict-by-default; add an explicit "escape hatch off" test that an unsigned pack installs only when `RejectUnsignedPacks=false`.

**Risk:** The trusted private key must not be a real secret committed to the repo. It is a **local development trust anchor** for seeds only; cloud/production trust keys are a separate concern (and pair with the deferred Key Vault work). Documented as such inline and in the spec.

---

## A2. MCP read/mutation split

**Goal:** The HTTP MCP transport exposes only read-only tools; mutation tools are stdio-only (local/trusted).

**Approach:**
- Split `DigitalBrainTools` into two registerable tool types sharing a common base:
  - `DigitalBrainReadTools` — `list_marketplace`, `get_workbench_surfaces`.
  - `DigitalBrainMutationTools` — `publish_to_marketplace`, `install_from_marketplace`, `fire_ui_action`, `visualize_data`, `run_closed_loop`, `run_code_foundry`, `ask_llm_neuron`, `ask_ino`.
  - Shared helpers (`grains`, `ResolveNeuron`, `GetPublishedPacksWithLocalSeedsAsync`, JSON options, id parsing) move to a shared base class so both types reuse them.
- Registration: **stdio** (`DigitalBrain.Mcp/Program.cs`) registers both types; **HTTP** (`DigitalBrain.Kernel/Program.cs`) registers `DigitalBrainReadTools` only.
- Classification rationale: a tool is "mutation" if it fires a side-effecting synapse, spends LLM tokens, or changes marketplace/cluster state. `visualize_data` and `fire_ui_action` fire synapses → mutation.

**Components touched:** `DigitalBrain.Mcp.Tools/*` (refactor into base + two types), `DigitalBrain.Mcp/Program.cs`, `DigitalBrain.Kernel/Program.cs`.

**Tests:** assert the HTTP-registered tool set contains no mutation tool names; stdio set contains all. (Verify via the registered tool metadata, not by standing up a transport.)

**API to verify via Context7 before coding:** `ModelContextProtocol` / `ModelContextProtocol.AspNetCore` — `WithTools<T>()` registration semantics and whether multiple `WithTools` calls compose.

---

## A3. Rolling self-update rollback/abort

**Goal:** A failed replica verify aborts the rollout, restores the pre-update checkpoint, and reports it — instead of silently completing.

**Approach:**
- Add a deterministic **verify seam** to `PerformKernelSelfUpdate` (test-only failure injection via a flag/synapse field) so a chosen replica's verify fails.
- On verify failure: stop restarting further replicas; call `RestoreCheckpointAsync(preUpdateCheckpoint)`; emit a new `KernelUiSurfaceKinds.RollingRollback` surface with the failing replica/version; do **not** emit `RollingComplete`.
- Happy path unchanged.

**Components touched:** `DigitalBrain.Kernel/SystemNeurons.cs` (`AspireOrchestratorNeuron`), `DigitalBrain.Kernel/Ui` kinds (`KernelUiSurfaceKinds.RollingRollback`), possibly `DigitalBrain.Core/RestartResource.cs` if the verify-failure signal needs a typed field.

**Tests:** Reqnroll scenario in `NeuronCore.feature` — trigger update with an injected failing replica; assert timeline contains `kernel-rolling-rollback`, does **not** contain `kernel-rolling-complete`, and the checkpoint was restored.

---

## A4. Explicit N+1 handler-growth proof

**Goal:** Make the marketplace's core dynamic-dispatch guarantee an explicit assertion.

**Approach:**
- Add a test that: fires a broadcast synapse and counts responders/emissions; installs a pack whose embodied behavior handles that synapse; fires again; asserts exactly **one additional** responder. Reuse the existing embodiment path (`GeneratedNeuron` + `IPackBehavior` manifest) and the `CompanyKnowledgeTests`/`PackAlcEmbodier` patterns.
- The "count" metric: number of distinct emissions of the pack's output synapse on the timeline for a correlation, before vs after install (before = 0, after = 1), proving the new handler reacts without restart.

**Components touched:** test-only — likely `DigitalBrain.Tests/Foundry` or a new `Distribution` test, plus a Reqnroll step if expressed in `NeuronCore.feature`.

**Tests:** the assertion itself is the deliverable.

---

## A5. Pluggable checkpoint keying

**Goal:** Decouple the AES key source so a cloud key provider can drop in.

**Approach:**
- Introduce `ICheckpointKeyProvider { byte[]? GetKey(); }` (or async equivalent).
- `ConfigCheckpointKeyProvider` — reads `DigitalBrain:Checkpoint:Key` from config/env (current behavior). Testable.
- `KeyVaultCheckpointKeyProvider` — **structural**, wired behind config (e.g. `DigitalBrain:Checkpoint:KeyVaultUri`) like Stripe/Qdrant; not validated here (no vault). Marked env-gated.
- `AddKernelSecurity` resolves the key via the registered provider and builds `AesNeuronStateProtector`; production + no key → fail-fast; dev → `PassThroughNeuronStateProtector`.

**Components touched:** `DigitalBrain.Core/INeuronStateProtector.cs` neighborhood (interface), `DigitalBrain.Kernel/Kernel/*` (providers + `AddKernelSecurity` wiring).

**Tests:** `ConfigCheckpointKeyProvider` yields a working AES protector round-trip; fail-fast assertion when production environment + no key configured; dev falls back to pass-through.

---

## Cross-cutting conventions

- **Context7 first:** look up every framework/library API touched (ModelContextProtocol tool registration, Orleans grain/journal APIs, BCL ECDSA/AES-GCM) before writing code.
- **Latest central packages** via `Directory.Packages.props`; no `Version="*"`.
- Self-explanatory names; no vacuous `/// <summary>`; small inline comments only where genuinely non-obvious.
- Relative paths only.
- **Per-item verification ritual:** `dotnet build` → targeted high-severity `dotnet test --filter` → `aspire doctor` (MCP). Full `aspire run` validation after A1–A5 land together.

## Out of scope (deferred, named for traceability)

WASM/`IWasm` sandbox; live Qdrant + real embeddings; live Stripe; Flutter render E2E (un-skip); Google auth; LLM behavioral tests; MCP token auth + external ingress; real cloud Key Vault provider validation.

## Build / sequence order

A2 and A4 are independent. A1 is the largest (signing + seeds + default flip). A3 depends only on existing self-update code. A5 is self-contained. Suggested order: A4 (cheapest, proves the core) → A2 → A5 → A3 → A1 (largest, touches seeds + boot path). Final: combined `aspire run` smoke + full high-severity suite.
