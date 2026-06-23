# DigitalBrain.Deploy

Pulumi (C# IaC) program that provisions the Azure infrastructure for DigitalBrain / NeuroOS,
built on [RoseXTechnology/DeploymentKit](https://github.com/RoseXTechnology/DeploymentKit)
(vendored under `DeploymentKit/`) and extended in this repo with an **Azure OpenAI** component.

## What it provisions

| Resource | DeploymentKit component |
| --- | --- |
| Azure Storage account (Table + Blob) | `AddStorage` / `AddTableStorage` / `AddBlobStorage` |
| Azure OpenAI account + chat deployment | `AddOpenAi` (**added in this repo**) |
| Key Vault (storage conn + OpenAI key) | `AddKeyVault` (managed identity, `ApplyToContainerApps`) |
| ACA environment + 3 container apps | `AddContainerApps` |

- **Target:** subscription `08e2e8fa-a9bf-4a1a-be54-56664d2c6cc9`, region `westeurope`,
  resource group `digitalbrain-rg`.
- **Images (GHCR):** `ghcr.io/digitalbraintech/digitalbrain-{gateway,silo,mcp}`, pinned by `--image-tag`.
- **App slots:** gateway -> external ingress, silo -> internal, mcp -> internal.
- **ACA env:** `DIGITALBRAIN_ENV=cloud`, `DigitalBrain__Llm__Provider=azureopenai`,
  `DigitalBrain__Llm__Model=<chat deployment>`.

## Build (verification for Task 6)

```bash
dotnet build framework/deploy/DigitalBrain.Deploy.csproj
```

This is a **standalone** project, intentionally kept out of `NeuroOS.slnx` so its Pulumi/Azure
dependency graph does not affect the main solution build.

## Usage (Task 10 — live provisioning)

```bash
pulumi up --stack prod --config-file Pulumi.prod.yaml -- --image-tag <tag>
```

The actual provisioning call (`InfrastructureDeployer.DeployAsync(settings)`, the live Pulumi engine)
is **not** invoked in Task 6 — `Program.cs` only declares and compiles the infrastructure model.

## What is stubbed for Task 10

- The live `DeployAsync` invocation (currently `Program.cs` builds the settings model and exports a
  placeholder gateway FQDN).
- GHCR registry credentials / image pull wiring: DeploymentKit's Container Apps component is
  ACR-oriented; the GHCR image references are pinned by tag here, registry auth is finalized in Task 10.
- The exported `gatewayFqdn` is a template; the real FQDN comes from the provisioned app at deploy time.
