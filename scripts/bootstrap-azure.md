# Azure Bootstrap — one-time setup

Run these steps once per environment before the first deploy.  
All `az` commands assume you are logged in (`az login`) with Owner or Contributor + User Access Administrator rights on the subscription.

**Known values used throughout:**

| Item | Value |
|------|-------|
| Subscription ID | `08e2e8fa-a9bf-4a1a-be54-56664d2c6cc9` |
| Tenant ID | `10b9647a-65af-44e0-9e55-d8f9fc93a381` |
| Resource group | `digitalbrain-rg` |
| Region | `westeurope` |
| GitHub repo | `digitalbraintech/framework` |

---

## Step 1 — Entra app registration + federated credential (GitHub OIDC)

This lets the `deploy.yml` workflow authenticate to Azure without storing any long-lived secret.

```bash
# 1a. Create the app registration
APP_ID=$(az ad app create \
  --display-name "digitalbrain-github-deploy" \
  --query appId -o tsv)

echo "App (client) ID: $APP_ID"

# 1b. Create the service principal for the app
az ad sp create --id "$APP_ID"

# 1c. Grant Contributor on the subscription so Pulumi can provision resources
az role assignment create \
  --assignee "$APP_ID" \
  --role Contributor \
  --scope "/subscriptions/08e2e8fa-a9bf-4a1a-be54-56664d2c6cc9"

# 1d. Retrieve the app's object ID (needed for federated-credential, different from appId)
APP_OBJECT_ID=$(az ad app show --id "$APP_ID" --query id -o tsv)

# 1e. Add the federated credential that trusts pushes to the main branch
az ad app federated-credential create \
  --id "$APP_OBJECT_ID" \
  --parameters '{
    "name": "github-main",
    "issuer": "https://token.actions.githubusercontent.com",
    "subject": "repo:digitalbraintech/framework:ref:refs/heads/main",
    "audiences": ["api://AzureADTokenExchange"]
  }'

# 1f. Also trust workflow_dispatch runs (same subject covers them via branch ref)
# No additional credential needed — workflow_dispatch on main uses the same subject.
```

> **Note:** `az ad app federated-credential create --id` takes the **object ID** of the app
> registration, not the client (app) ID. Step 1d retrieves it explicitly.

---

## Step 2 — Resource group

```bash
az group create \
  --name digitalbrain-rg \
  --location westeurope
```

---

## Step 3 — Storage account + blob container for Pulumi state

Pulumi uses an Azure Blob backend (`azblob://pulumi-state`). The storage account name must be
globally unique — adjust the suffix if needed.

```bash
# 3a. Create storage account (LRS is sufficient for IaC state)
az storage account create \
  --name "digitalbainpulumi" \
  --resource-group digitalbrain-rg \
  --location westeurope \
  --sku Standard_LRS \
  --allow-blob-public-access false

# 3b. Retrieve the account key
STORAGE_KEY=$(az storage account keys list \
  --account-name "digitalbainpulumi" \
  --resource-group digitalbrain-rg \
  --query "[0].value" -o tsv)

# 3c. Create the blob container Pulumi expects
az storage container create \
  --name pulumi-state \
  --account-name "digitalbainpulumi" \
  --account-key "$STORAGE_KEY"
```

> The `deploy.yml` workflow runs `pulumi login azblob://pulumi-state`.  
> Pulumi resolves the storage account via the `AZURE_STORAGE_ACCOUNT` and
> `AZURE_STORAGE_KEY` environment variables, or via the ambient Azure login.
> Because the workflow already logs in via OIDC (Step 1), no separate storage
> credentials are required as long as the service principal has Storage Blob Data
> Contributor on this account.

```bash
# 3d. Grant the service principal access to the Pulumi state container
az role assignment create \
  --assignee "$APP_ID" \
  --role "Storage Blob Data Contributor" \
  --scope "/subscriptions/08e2e8fa-a9bf-4a1a-be54-56664d2c6cc9/resourceGroups/digitalbrain-rg/providers/Microsoft.Storage/storageAccounts/digitalbainpulumi"
```

---

## Step 4 — GitHub repository variables and secret

Use the GitHub CLI (`gh`) or the GitHub web UI under **Settings → Secrets and variables → Actions**.

```bash
# Variables (not secrets — safe to expose in logs)
gh variable set AZURE_CLIENT_ID     --body "$APP_ID"
gh variable set AZURE_TENANT_ID     --body "10b9647a-65af-44e0-9e55-d8f9fc93a381"
gh variable set AZURE_SUBSCRIPTION_ID --body "08e2e8fa-a9bf-4a1a-be54-56664d2c6cc9"

# Secret (kept encrypted, not visible in logs)
gh secret set PULUMI_PASSPHRASE     --body "<choose-a-strong-passphrase>"
```

The `deploy.yml` workflow references these as:
- `vars.AZURE_CLIENT_ID` / `vars.AZURE_TENANT_ID` / `vars.AZURE_SUBSCRIPTION_ID` (repo variables)
- `secrets.PULUMI_PASSPHRASE` (repo secret, passed as `PULUMI_CONFIG_PASSPHRASE`)

---

## Step 5 — Verify Azure OpenAI quota in westeurope

Before triggering the first deploy, confirm the subscription has access to Azure OpenAI and
the required model SKU is available in `westeurope`:

```bash
# List available OpenAI SKUs in westeurope
az cognitiveservices account list-skus \
  --kind OpenAI \
  --location westeurope \
  --output table
```

Look for `gpt-4o` or `gpt-4` in the output. If nothing is listed, your subscription does not
yet have Azure OpenAI access in this region — submit a capacity request through the Azure portal
(`Cognitive Services → Azure OpenAI → Request access`) before deploying.

---

## Before first deploy — checklist

- [ ] Steps 1–5 above completed
- [ ] `gh variable list` and `gh secret list` show all four names
- [ ] **Pulumi CLI installed** on the machine or CI runner: the deploy workflow runs
  `pulumi login` + `dotnet run --project deploy/...` which invokes Pulumi internally.
  The GitHub-hosted runner (`ubuntu-latest`) does **not** include Pulumi by default —
  add a `pulumi/actions@v6` setup step (or `curl -fsSL https://get.pulumi.com | sh`) to
  the workflow before the "Provision and deploy" step.
- [ ] **GHCR pull credentials for ACA:** Azure Container Apps pulls images from GHCR
  (`ghcr.io/digitalbraintech/*`). Make the packages public, or add registry credentials
  via `az containerapp registry set` / DeploymentKit's container app configuration before
  the first `pulumi up` runs.
- [ ] **Azure OpenAI quota** confirmed in `westeurope` (Step 5 above)
- [ ] Run `aspire run` locally first to confirm the local stack boots cleanly before
  attempting a cloud deploy
