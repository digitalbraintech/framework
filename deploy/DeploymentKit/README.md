# DeploymentKit

Reusable deployment library for provisioning Azure infrastructure with Pulumi. Includes the canonical deployment facade, fluent settings builder, configuration loading, validation, and deployment orchestration.

This library is designed to provision and manage cloud infrastructure, supporting PostgreSQL, Redis, Container Apps, Key Vault, Application Gateway, Azure Monitor, and other services.

## Prerequisites

| Tool | Version | Install command (Windows) |
|------|---------|---------------------------|
| .NET SDK | 11.0 Preview | `winget install Microsoft.DotNet.SDK.Preview` |
| Azure CLI | Latest | `winget install Microsoft.AzureCLI` |
| Pulumi CLI | Latest | `winget install Pulumi.Pulumi` |

Make sure you are authenticated with Azure CLI:
```powershell
az login
```

And configured with Pulumi:
```powershell
pulumi login
```

## Repository Structure

Key directories:
* `Components` - Pulumi custom component resources for applications, databases, and networking.
* `Deployer` - Main orchestration entry points (`InfrastructureDeployer`).
* `Services` - Infrastructure building services (networking, Key Vault, databases, ingress, monitoring, DNS).
* `Settings` - Strong configuration types for environments, features, and azure settings.
* `Validators` - Validation logic to ensure settings correctness prior to executing deployments.

## Getting Started

### Restore Dependencies

```powershell
dotnet restore
```

### Build Project

```powershell
dotnet build
```

## Main Core Features

1. **Configuration Providers & Validation**: Binding and validating Azure/infrastructure settings with `ValidationOrchestratorService` and strong validators.
2. **Infrastructure Building Block**: Fluent building of networking, storage, databases, container apps, and security layers.
3. **Drift Detection & Recovery**: Automated checking of infrastructure state vs actual Azure state (`DriftDetectionService`, `StateDriftRecoveryService`).
4. **Green-Blue Deployment Support**: Live swapping of container application slots with traffic routing and active health checks.
5. **Secrets Bridge**: Secure key storage and secret fetching via Key Vault.
