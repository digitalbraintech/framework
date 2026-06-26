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

### (2026-06-26) CI auto-deploy bootstrap — PROGRESS + REMAINING (OIDC app reg requires privileged Entra account)
All changes committed and pushed to **master** (default branch renamed from main; workflows, docs, and READMEs updated). Deploys now happen **ONLY via GitHub Actions**.

**Completed in this session (via gh cli + az cli + pulumi full-exe):**
- `.github/workflows/deploy.yml` and `ci.yml` updated to `master` (and ci ignores master).
- `deploy/DEPLOY-STATUS.md`, `deploy/README.md`, `scripts/bootstrap-azure.md`, root `README.md` refreshed (DeploymentKit history noted, commands accurate, master refs).
- GitHub repo default branch set to `master`; old `main` deleted.
- GH Actions vars set: DOCKERHUB_USERNAME=vhorbachov, AZURE_TENANT_ID, AZURE_SUBSCRIPTION_ID (CLIENT_ID is placeholder until app reg created).
- GH Actions secrets set: PULUMI_PASSPHRASE, CHECKPOINT_KEY (fresh 32B), DOCKERHUB_TOKEN (placeholder — rotate with real write-capable Docker Hub token immediately).
- pulumi-state container created in digitalbrainstprod.
- RBAC: current user granted Storage Blob Data Contributor on storage (for migration).
- Pulumi state fully migrated: `pulumi stack export` (file://) → `azblob://pulumi-state` `dev` stack (now 10 resources preserved).
- Commit + push on master performed.

**Remaining (one-time, run with privileged az login that can create app regs + assign Owner/UserAccessAdmin):**
1. **Azure OIDC identity** (run the commands below with a capable account):
   ```pwsh
   $APP_ID = (az ad app create --display-name 'digitalbrain-github-deploy' --query appId -o tsv)
   az ad sp create --id $APP_ID
   $APP_OBJ = (az ad app show --id $APP_ID --query id -o tsv)
   az ad app federated-credential create --id $APP_OBJ --parameters '{
     "name": "github-master",
     "issuer": "https://token.actions.githubusercontent.com",
     "subject": "repo:digitalbraintech/brain:ref:refs/heads/master",
     "audiences": ["api://AzureADTokenExchange"]
   }'
   az role assignment create --assignee $APP_ID --role Contributor --scope /subscriptions/08e2e8fa-a9bf-4a1a-be54-56664d2c6cc9/resourceGroups/digitalbrain-rg
   az role assignment create --assignee $APP_ID --role "Storage Blob Data Contributor" --scope /subscriptions/08e2e8fa-a9bf-4a1a-be54-56664d2c6cc9/resourceGroups/digitalbrain-rg/providers/Microsoft.Storage/storageAccounts/digitalbrainstprod
   # Then set the var:
   # gh variable set AZURE_CLIENT_ID --body "$APP_ID"
   ```
   Capture and set AZURE_CLIENT_ID (and rotate if needed).

2. **Rotate secrets** (if not already real values):
   - `gh secret set DOCKERHUB_TOKEN` (must allow push to vhorbachov/digitalbrain-silo)
   - Optionally rotate CHECKPOINT_KEY / PULUMI_PASSPHRASE and re-set.

3. **Trigger/verify first run on master**:
   - The push that created master + this commit already queued a deploy run (tag ~ commit sha).
   - Use `gh run list --workflow deploy.yml --limit 3` and `gh run watch` to observe.
   - After green: verify container boots (no checkpoint crash), gRPC-Web CORS preflight returns ACA *, Health over the FQDN.

NOTE: Because state is in azblob and OIDC + image push will use the GH secrets, **all future deploys (incl. imageTag changes) MUST go through `git push origin master` or workflow_dispatch. No local `pulumi up` for prod.**

### Pre-SP1 (2026-06-23) — superseded where noted by the SP1 entry above
- **DNS:** remove the dangling `api` / `asuid.api` records at the registrar — they pointed to the old deleted gateway. SP2 attaches a fresh `api.digitalbrain.tech` custom domain to the now-public kernel ingress.
- **CI (`deploy.yml`):** auto-deploy on `push:[master]` → `pulumi up`. Requires the azblob state migration above plus repo `DOCKERHUB_USERNAME` (var) + `DOCKERHUB_TOKEN` (secret) before enabling.
