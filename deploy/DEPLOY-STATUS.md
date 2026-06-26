# Deploy status — sub-project A (Azure Deploy Foundation)

## LIVE (2026-06-23) — Elon Algorithm Pass 1 applied

The vendored `DeploymentKit` (308 files / ~33k LOC) was deleted and replaced by a single ~237-line
`Pulumi.AzureNative` program (`Program.cs`) that provisions **only** what the runtime uses. A real
`pulumi up` on stack `dev` reduced the live footprint from 17 to 10 Pulumi resources (8 real Azure
resources). Both kept resources and the silo are healthy.

### Resources (RG `digitalbrain-rg`, westeurope)
- `azure-native:resources:ResourceGroup` digitalbrain-rg
- `azure-native:storage:StorageAccount` digitalbrainstprod (StorageV2, Standard_LRS, public network access)
- `azure-native:cognitiveservices:Account` digitalbrainopenaiprod (S0) + `Deployment` chat (gpt-4o-mini, GlobalStandard, cap 10)
- `azure-native:operationalinsights:Workspace` digitalbrain-log-prod + `applicationinsights:Component` digitalbrain-ai-prod
- `azure-native:app:ManagedEnvironment` digitalbrain-cae-prod
- `azure-native:app:ContainerApp` digitalbrain-jobs (silo, internal Http2 ingress on port 8080 / gRPC gateway)

### Deleted in Pass 1
Key Vault (`digitalbrainkvprod`), ACR (`digitalbrainacrprod`), the gateway app (`digitalbrain-api`,
external HTTP — `api.digitalbrain.tech` no longer served), and the empty DeploymentKit component wrappers
(Network / Database / app-registry / app-runtime). The undeployed MCP "Bot" slot and the two-phase
placeholder-image deploy are gone.

### Image registry
The silo image lives in **public Docker Hub**: `docker.io/vhorbachov/digitalbrain-silo:v4`
(built with `dotnet publish -t:PublishContainer --os linux --arch x64`, pushed via the docker CLI). ACA currently runs revision `digitalbrain-jobs--0000005` on the previous image (v3). Image **v4** (which adds the co-hosted gRPC gateway + needs the internal Http2 ingress) is built and pushed to Docker Hub but **not yet deployed** — apply with `pulumi up` to roll it out.

### Silo (Orleans) runtime
The silo now exposes an internal-only Http2 ingress on port 8080 serving the gRPC `DigitalBrainGateway` (Ask/Fire/Timeline/Health).

The silo (`digitalbrain-jobs`) runs with Azure **Table clustering**
(`OrleansSiloInstances`), **Blob grain storage** (`grainstate`) and **Blob journaling**, all wired from
the injected `ConnectionStrings__clustering/grainstate/journal` (one StorageV2 connection string, a
Container App secret) plus `DigitalBrain__Llm__AzureOpenAIEndpoint/AzureOpenAIKey` and
`Provider=azureopenai`/`Model=chat`/`DIGITALBRAIN_ENV=cloud`.

## How it's wired
- `Program.cs` (`Deployment.RunAsync(Provision)`) declares each resource directly with `Pulumi.AzureNative`.
  Kept resources reuse their original logical names so they match the existing state; the env + silo carry
  an `Alias { ParentUrn = ...DeploymentKitApp::digitalbrain-app-runtime-prod }` so Pass 1 re-parented them
  to the stack root **in place** (no destructive replace).
- Image tag comes from `pulumi config imageTag` (default in `Pulumi.dev.yaml`) or `DIGITALBRAIN_IMAGE_TAG`.
- No Key Vault, ACR, gateway, monitoring agents, or placeholder phase.

## Environment for local `pulumi` (off the user profile)
```sh
export PULUMI_HOME=E:/tools/pulumi-home
export PULUMI_CONFIG_PASSPHRASE=digitalbrain-dryrun
export ARM_SUBSCRIPTION_ID=08e2e8fa-a9bf-4a1a-be54-56664d2c6cc9
export ARM_TENANT_ID=10b9647a-65af-44e0-9e55-d8f9fc93a381   # also AZURE_TENANT_ID
E:/tools/pulumi/pulumi/bin/pulumi.exe login file://E:/tools/pulumi-state
# stack dev. Kernel image: dotnet publish DigitalBrain.Kernel -t:PublishContainer --os linux --arch x64
#   -p:ContainerRepository=vhorbachov/digitalbrain-silo -p:ContainerImageTag=<tag>  then docker push.
```

## Commands
```sh
pulumi preview --stack dev --cwd framework/deploy     # no-spend plan
pulumi up      --stack dev --cwd framework/deploy     # provision / update (INCURS COST)
pulumi destroy --stack dev --cwd framework/deploy     # tear down
```

## Follow-ups

### (2026-06-26) SP1 CI standardization
- **Ingress:** The kernel container app (`digitalbrain-jobs`) now serves the public interaction path: an **external** ingress with `Transport="Auto"` (HTTP/1.1 + HTTP/2 on port 8080) carrying browser gRPC-Web and native gRPC through the kernel's co-hosted `DigitalBrainGateway`/`UiGateway`. This supersedes the Pre-SP1 "no interaction path is live" state below.
- **CI state backend:** CI (`deploy.yml`) now explicitly declares `cloud-url: azblob://pulumi-state`. Stack state must be migrated from the local file backend to Azure Blob Storage; this is a runbook step (Task 5).
- **Secret requirement:** `DigitalBrain:Checkpoint:Key` (base64 AES key for checkpoint encryption — `AddKernelSecurity` fail-fasts without it in Production) is now a Container App secret. The Pulumi program reads it from env `DIGITALBRAIN_CHECKPOINT_KEY` (CI injects it from the repo secret `CHECKPOINT_KEY`, never committed) or falls back to local `pulumi config set --secret checkpointKey`.

### (2026-06-26) CI auto-deploy bootstrap — REMAINING (one-time, needs credentials this session's az login lacks)
The `deploy.yml` code is complete (publishes `DigitalBrain.Kernel`, OIDC login, `pulumi up` on `azblob://pulumi-state` with `AZURE_STORAGE_ACCOUNT=digitalbrainstprod`, checkpoint key from `CHECKPOINT_KEY` secret). To make `push:[master] → live deploy` actually work, complete these once:

1. **Azure OIDC identity** (BLOCKED for the current az account `…@tripradar.io` — "Insufficient privileges" to create app registrations; needs an account with AD app-reg rights + Owner/User-Access-Admin on sub `08e2e8fa-…`):
   - Create an app registration (e.g. `digitalbrain-github-deploy`); add a **federated credential** for `repo:digitalbraintech/brain:ref:refs/heads/master` (and `:environment:` if used).
   - Assign the SP **Contributor** on RG `digitalbrain-rg` (provisions ACA/etc.) and **Storage Blob Data Contributor** on `digitalbrainstprod` (so the azblob state backend can read/write).
   - Capture `appId` (→ `AZURE_CLIENT_ID`), tenant (→ `AZURE_TENANT_ID`), sub (→ `AZURE_SUBSCRIPTION_ID`).
2. **GitHub repo secrets/vars** (digitalbraintech/brain → Settings → Secrets and variables → Actions):
   - Variables: `DOCKERHUB_USERNAME`, `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`.
   - Secrets: `DOCKERHUB_TOKEN`, `PULUMI_PASSPHRASE` (= `digitalbrain-dryrun` unless rotated), `CHECKPOINT_KEY` (a fresh base64 32-byte AES key).
3. **Pulumi state → azblob** (storage data-plane; the current az account CAN do this): create container `pulumi-state` in `digitalbrainstprod`; `pulumi stack export` from the local file backend, then `pulumi login azblob://pulumi-state` (with `AZURE_STORAGE_ACCOUNT=digitalbrainstprod`), `pulumi stack init dev`, `pulumi stack import`. NOTE: state lives in the same account the program manages — acceptable for first-cut (we never `destroy`); SP3 may move it to a dedicated account.
4. **First run:** merge `hardening/bucket-d` + `prod/sp1-public-backend` to `master` (SP1 depends on bucket-d's gRPC-Web foundation, absent on `master`). The push triggers the deploy. First `pulumi up` flips `digitalbrain-jobs` ingress to **external/public** and deploys the new image — verify boot + a gRPC-Web preflight against the `*.azurecontainerapps.io` FQDN.

### Pre-SP1 (2026-06-23) — superseded where noted by the SP1 entry above
- **DNS:** remove the dangling `api` / `asuid.api` records at the registrar — they pointed to the old deleted gateway. SP2 attaches a fresh `api.digitalbrain.tech` custom domain to the now-public kernel ingress.
- **CI (`deploy.yml`):** auto-deploy on `push:[master]` → `pulumi up`. Requires the azblob state migration above plus repo `DOCKERHUB_USERNAME` (var) + `DOCKERHUB_TOKEN` (secret) before enabling.
