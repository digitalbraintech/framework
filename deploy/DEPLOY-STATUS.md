# Deploy status — sub-project A (Azure Deploy Foundation)

## Dry-run validated (2026-06-23, no spend)

A no-spend `pulumi preview` was run successfully against the Azure subscription
(`westeurope`, RG `digitalbrain-rg`), proving the toolchain end-to-end:

- Pulumi CLI 3.247.0 + `Pulumi.yaml` (`runtime: dotnet`) run the `DigitalBrain.Deploy` program.
- DeploymentKit's `InfrastructureBuilder` builds + validates the full settings graph:
  Storage (Table + Blob), Azure OpenAI (custom), Key Vault, Container Apps.
- Settings validated: deployment `digitalbrain`, env `production`, location `westeurope`,
  subscription set, RG `digitalbrain-rg`, naming prefix `digitalbrain`.
- Stack outputs resolve: `resourceGroup`, `gatewayFqdn` (placeholder), `chatDeployment=chat`,
  `imageTag=latest`, `environment=prod`.
- Azure OpenAI confirmed available on the subscription (S0 in westeurope; quotas present).

Backend used for the dry-run: local file backend on `E:\tools\pulumi-state` with
`PULUMI_HOME=E:\tools\pulumi-home` (kept off the user profile). Pulumi CLI installed at
`E:\tools\pulumi\pulumi\bin\pulumi.exe`.

## Gap to a real `pulumi up` (confirmed by the dry-run)

`pulumi preview` reported **`+ 1 to create` (the Pulumi Stack only)** — the program currently
**builds and validates the `InfrastructureSettings` but does not invoke DeploymentKit's actual
resource provisioning** inside the Pulumi program, so no Azure resources are registered with the
engine yet. Before a real deploy:

1. **Wire actual provisioning:** invoke DeploymentKit's resource-creating path (its
   services / `DeployAsync` equivalent) inside `Deployment.RunAsync` so the ACA/Storage/OpenAI/
   Key Vault resources are registered (not just the settings object built).
2. **image-tag via Pulumi config/env, not `Main` args:** `pulumi up`/`preview` do not forward
   `-- --image-tag` to the program. Read it from `pulumi config` (`imageTag`) or an env var.
   (The `dotnet run -- --image-tag` form only works outside Pulumi.)
3. **GHCR pull credentials for ACA:** DeploymentKit is ACR-centric; ACA needs registry pull
   config to pull `ghcr.io/digitalbraintech/digitalbrain-{silo,gateway,mcp}` (or push to ACR).
   Also push the 3 images (currently local only) to the registry.
4. **Bootstrap:** for CI, the Entra app + GitHub OIDC federated credential + an azblob Pulumi
   state backend (see `scripts/bootstrap-azure.md`). For a local `pulumi up`, the user's `az`
   login + a chosen backend suffice.
5. **Align `OpenAiService` naming** with `IResourceNamingService` (registry/deconfliction).

## Commands

```sh
# no-spend plan (local backend example)
export PULUMI_HOME=E:/tools/pulumi-home
pulumi login file://E:/tools/pulumi-state
pulumi stack select dev
pulumi preview --stack dev

# real provision (after the gaps above are closed) — INCURS AZURE COST
pulumi up --stack dev
```
