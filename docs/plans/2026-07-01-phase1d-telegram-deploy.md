# Phase 1d — Telegram Channel as a Deployed ACA App (Pulumi IaC)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Make the Telegram transport a first-class, separately-deployed Azure Container App in the Pulumi IaC (`brain/deploy`), so the bot can run in prod independently of the kernel — resolving the hosting question (not co-hosted in the kernel, not run from `brain.cs` in prod; `brain.cs --telegram` stays the dev mirror).

**Scope & verifiability (READ THIS):** This slice AUTHORS infrastructure-as-code only. It is verified by **`dotnet build` of the deploy project compiling** and a resource-graph review. It does **NOT** build/push the transport container image and does **NOT** run `pulumi up`/`pulumi preview` (those need Azure creds + a Docker registry and are a manual, human-authorized step). Do not attempt to deploy.

**Architecture:** Mirror the existing kernel Container App. Add: (1) a Dockerfile that builds `DigitalBrain.Telegram.Transport`; (2) a second `ContainerApp` in `deploy/Program.cs` for the transport — external ingress (Telegram POSTs to `/webhook`), env pointing at the kernel's internal gRPC gateway, with the bot token + internal-service-key as Pulumi secret config. The kernel app already exists; the AppHost already wires `telegram-bot` for dev behind `DIGITALBRAIN_ENABLE_TELEGRAM`.

**Tech Stack:** Pulumi (.NET, `DigitalBrain.Deploy.csproj`), Azure Container Apps, Docker. No Orleans/test-cluster involvement.

## Global Constraints

- Target framework **net11.0** (the transport project's TFM); never pin `Version="*"`; use central `Directory.Packages.props` for any new package (avoid adding packages if possible).
- **No vacuous `/// <summary>`**; self-explanatory names; small inline comments only where genuinely non-obvious.
- **Do NOT run `pulumi up`, `pulumi preview`, `docker build`, or `docker push`.** Verification is `dotnet build` of the deploy project only. State clearly in the report that deploy is unverified-until-manual.
- Secrets (bot token, internal-service-key) MUST be Pulumi **secret** config (`config.RequireSecret(...)` / `config.GetSecret(...)`), never hard-coded. Follow how the existing app already sources secrets (storage keys, OpenAI key) in `deploy/Program.cs`.
- Mirror the EXISTING kernel `ContainerApp` resource shape (same environment, same registry/image tag pattern, same secret-wiring idiom) — do not invent a new pattern.
- Work in the `brain` repo on branch `spec/phase1d-telegram-deploy` (already checked out). Do NOT `git add` under `.superpowers/`.
- Commit messages end with: `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`.

---

## Orientation (read these first)

- `brain/deploy/Program.cs` — the Pulumi program. Study the existing kernel Container App (`digitalbrain-jobs`): how its `ContainerApp`, `ManagedEnvironment`, ingress, image (`docker.io/vhorbachov/digitalbrain-silo:${imageTag}`), `imageTag` config, env vars, and secrets are declared. You will mirror this.
- `brain/deploy/Pulumi.dev.yaml` / `Pulumi.yaml` — config layout (where the image tag + secrets live).
- The existing kernel **Dockerfile** (find it: search the repo for a Dockerfile referencing `DigitalBrain.Kernel`/the silo). You will add a parallel one for the transport.
- `brain/DigitalBrain.Telegram.Transport/Program.cs` — the transport's config keys. Confirm the exact env keys it reads: `DigitalBrain__GatewayAddress` (kernel gRPC endpoint), `Telegram__BotToken`, `DigitalBrain__InternalServiceKey`, and its listen port (ASPNETCORE_HTTP_PORTS / Kestrel). These become the container app's env/secrets.
- `brain/DigitalBrain.Aspire/DigitalBrainBuilderExtensions.cs` `WireTelegramTransport(...)` — the dev wiring; it shows the exact env keys/secrets the transport expects (reuse the SAME keys in the Pulumi env so dev and prod match).

---

## Task 1: Add the Telegram transport Container App + Dockerfile (Pulumi)

**Files:**
- Create: a Dockerfile for the transport (e.g. `brain/DigitalBrain.Telegram.Transport/Dockerfile`, mirroring the existing kernel Dockerfile's multi-stage net11 build but publishing `DigitalBrain.Telegram.Transport`).
- Modify: `brain/deploy/Program.cs` (add the transport `ContainerApp` + its secret config).
- Modify: `brain/deploy/Pulumi.dev.yaml` (add placeholder/secret entries for `telegramBotToken` + `internalServiceKey` if the existing pattern keeps such config here — match how existing secrets are declared; do not commit real secret values).

**Interfaces / requirements:**
- Consumes: the existing `ManagedEnvironment`, the `imageTag` config, the kernel `ContainerApp` (for its internal gateway FQDN), and the existing secret-sourcing idiom — all from `deploy/Program.cs`.
- Produces: a `digitalbrain-telegram` Container App resource.

- [ ] **Step 1: Establish the build baseline**

Run: `cd /e/digitalbraintech/brain && dotnet build deploy/DigitalBrain.Deploy.csproj`
Expected: succeeds today (record the baseline). If it does not build clean before your change, STOP and report BLOCKED with the error.

- [ ] **Step 2: Add the transport Dockerfile**

Locate the existing kernel Dockerfile (search the repo). Create a parallel Dockerfile for the transport that: uses the same net11 SDK/aspnet base images, restores/publishes `DigitalBrain.Telegram.Transport/DigitalBrain.Telegram.Transport.csproj`, and sets the entrypoint to the transport DLL. Expose the transport's HTTP port. Keep it structurally identical to the kernel Dockerfile (only the project path + exposed port differ).

- [ ] **Step 3: Add the Container App to `deploy/Program.cs`**

Mirroring the kernel `ContainerApp`, add a `digitalbrain-telegram` Container App in the same `ManagedEnvironment`:
- **Image:** `docker.io/vhorbachov/digitalbrain-telegram:${imageTag}` (reuse the existing `imageTag` config variable).
- **Ingress:** external, targetPort = the transport's HTTP port (so Telegram can POST `/webhook`).
- **Secrets (Pulumi secret config):** `telegramBotToken` and `internalServiceKey` via `config.RequireSecret(...)` (or `GetSecret` with a safe default of empty so a token-less deploy boots idle, matching the transport's no-op-without-token behavior — check `WireTelegramTransport`/transport for the idle-without-token contract and mirror it).
- **Env vars (use the SAME keys the transport reads — confirm from the transport `Program.cs` / `WireTelegramTransport`):**
  - `DigitalBrain__GatewayAddress` = the kernel Container App's internal gateway address (derive from the kernel app's ingress FQDN output, e.g. `https://<kernel-fqdn>`; use the kernel `ContainerApp` resource's `Configuration.Ingress` FQDN output).
  - `Telegram__BotToken` = (secret ref)
  - `DigitalBrain__InternalServiceKey` = (secret ref)
  - `ASPNETCORE_ENVIRONMENT=Production`, and the ASPNETCORE port env the transport expects (match the kernel app's port env pattern).
- **Scale:** modest (e.g. 1–3 replicas) — the transport is a stateless pipe.

Follow the exact Pulumi C# idioms already in the file (resource args types, `EnvironmentVar`, `Secret`, `Ingress`, `Template`/`Configuration` shapes). Export the transport FQDN as a stack output alongside the existing exports.

- [ ] **Step 4: Verify it compiles**

Run: `cd /e/digitalbraintech/brain && dotnet build deploy/DigitalBrain.Deploy.csproj`
Expected: Build succeeded, 0 errors. (This is the ONLY automated verification for this slice. Do NOT run `pulumi` commands or `docker`.)

- [ ] **Step 5: Commit**

```bash
cd /e/digitalbraintech/brain
git add deploy/Program.cs deploy/Pulumi.dev.yaml DigitalBrain.Telegram.Transport/Dockerfile
git commit -m "$(cat <<'MSG'
infra(deploy): add Telegram transport as a separate ACA container app

Pulumi ContainerApp for DigitalBrain.Telegram.Transport (external /webhook
ingress, kernel internal gateway address, bot token + internal key as secret
config) + a transport Dockerfile. Resolves prod Telegram hosting: separate
stateless app, not co-hosted in the kernel. IaC only — image build + pulumi
up are manual/unverified here.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
MSG
)"
```

---

## Final verification

- [ ] `cd /e/digitalbraintech/brain && dotnet build deploy/DigitalBrain.Deploy.csproj` → 0 errors.
- [ ] Confirm no real secret values are committed (only secret *references*/placeholders).

> No `aspire doctor`, no `pulumi up`, no `docker build` — all manual/human-authorized. The report MUST state the deploy path is authored-but-unverified-until-manual-deploy.

## Out of scope
- Building/pushing the `digitalbrain-telegram` image; running `pulumi up`/`preview`.
- CI wiring to build the transport image (follow-up).
- Setting the Telegram webhook URL on the bot (a runtime/ops step after deploy).
- Per-bot / BYO-token multiplexing (Phase 2).
