# DigitalBrain / NeuroOS

**The company brain.**

Every business has critical know-how scattered across people's heads, old Slack threads, email, support tickets, and databases. Companies only work because humans remember where the knowledge lives and how to apply it.

The NeuroOS is the missing primitive: it pulls fragmented knowledge, structures it, keeps it current, and turns it into executable skills files (typed neurons with synapses, dual journals, causation, and co-located UI surfaces) that AI agents and people can run safely and consistently.

These skills are published as signed typed-C# packs, installed and updated via the marketplace into a running system — exactly like INO and other capabilities.

The kernel (this Aspire package + minimal Orleans silo) is the always-on brain runtime. It ships the core substrate (marketplace, embodiment engine, journals, tasks, self-status) and starts 3 instances by default for stability during self-improvement, pack embodiment, and rolling updates.

Built on .NET Aspire, Orleans, and local/cloud LLMs.
Licensed under [MIT](LICENSE).

---

## Run locally (no Azure account)

**Prerequisites**

- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- [Aspire workload](https://learn.microsoft.com/en-us/dotnet/aspire/fundamentals/setup-tooling): `dotnet workload install aspire`
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (for Azurite + Ollama containers)

**Start the stack**

```bash
cd framework
cp .env.example .env   # already correct for local; edit only if your Ollama port differs
aspire run
```

Aspire brings up:

| Service | What it does |
|---------|-------------|
| **Azurite** | Azure Storage emulator — Orleans clustering + grain state |
| **Ollama** (qwen2.5-coder:1.5b) | Local LLM inference |
| **Silo** | Orleans kernel (3 replicas) — neurons, marketplace, self-heal loops |
| **Gateway** | HTTP entry point — routes requests to silo grains |
| **MCP** | Model Context Protocol server for Aspire + agent tooling |

Once all resources show **Running** in the Aspire dashboard, verify the gateway:

```bash
curl http://localhost:<gateway-port>/health
```

The port is shown in the Aspire dashboard next to the `gateway` resource.

---

## Deploy to Azure

For a one-time account setup (Entra app, resource group, Pulumi state backend, GitHub variables)
follow **[scripts/bootstrap-azure.md](scripts/bootstrap-azure.md)**.

Once bootstrapped, trigger the workflow manually from the GitHub Actions UI:

```
Actions → deploy → Run workflow
```

The deploy workflow (`.github/workflows/deploy.yml`) is set to `workflow_dispatch` — manual
until the first successful end-to-end deploy is confirmed, at which point it can be switched
to `push: [main]` for continuous deployment.

The workflow:
1. Runs `dotnet test`
2. Publishes container images to GHCR (`ghcr.io/digitalbraintech/digitalbrain-{gateway,silo,mcp}`)
3. Logs into Azure via OIDC (no long-lived secrets)
4. Runs `pulumi up` (via `dotnet run --project deploy/DigitalBrain.Deploy.csproj`) to provision
   ACA, Azure Storage, Azure OpenAI, and Key Vault in `westeurope`

---

## Configuration reference

| Variable | Local value | Cloud value |
|----------|-------------|-------------|
| `DIGITALBRAIN_ENV` | `local` | `cloud` |
| `DigitalBrain__Llm__Provider` | `ollama` | `azureopenai` |
| `DigitalBrain__Llm__Model` | `qwen2.5-coder:1.5b` | set by Pulumi at deploy time |
| `DigitalBrain__Llm__OllamaEndpoint` | `http://localhost:11434` | not used in cloud |

In local mode these are read from `.env` (or environment). In cloud mode the deploy workflow
sets them as Azure Container Apps environment variables via the Pulumi IaC program.

---

## Repository layout

```
framework/
├── DigitalBrain.Aspire/      # Aspire hosting SDK (AddDigitalBrain extension)
├── DigitalBrain.Silo/        # Orleans kernel — neurons, marketplace, closed loops
├── DigitalBrain.Gateway/     # HTTP gateway (ASP.NET Core)
├── DigitalBrain.Mcp/         # MCP server
├── DigitalBrain.Tests/       # Integration tests
├── deploy/                   # Pulumi IaC (C#) — Azure provisioning
├── .github/workflows/        # ci.yml (PR/branch builds) + deploy.yml (manual/Azure)
├── scripts/                  # Ops runbooks
│   └── bootstrap-azure.md    # One-time Azure setup guide
└── .env.example              # Local environment template
```
