# DigitalBrain.Deploy

Minimal Pulumi C# program (Pulumi.AzureNative, ~250 LOC) that provisions exactly the Azure resources
the NeuroOS kernel needs: RG, StorageV2 (for Orleans clustering/grain/journal), Azure OpenAI (gpt-4o-mini),
Log Analytics + App Insights, ACA Managed Environment, and the kernel ContainerApp (`digitalbrain-jobs`)
with public Auto ingress on 8080 (gRPC-Web + gRPC).

The previous vendored DeploymentKit (hundreds of files) was deleted; this single `Program.cs` is the
current "deployment kit". See DEPLOY-STATUS.md for live resources and history.

**Preferred deployment:** push to `master` (triggers `.github/workflows/deploy.yml` which builds, tests,
pushes the kernel image to Docker Hub, then `pulumi up --stack dev` using OIDC + azblob state).

- Stack: `dev`
- Image tag driven by `imageTag` config or DIGITALBRAIN_IMAGE_TAG (workflow uses git sha or input)
- Checkpoint encryption key (required): from `secrets.CHECKPOINT_KEY` as DIGITALBRAIN_CHECKPOINT_KEY or local `pulumi config set --secret checkpointKey`

Local (for verification only; prod deploys are GH Actions only):

```pwsh
$env:PULUMI_HOME = 'E:/tools/pulumi-home'
$env:PULUMI_CONFIG_PASSPHRASE = 'digitalbrain-dryrun'
$env:AZURE_STORAGE_ACCOUNT = 'digitalbrainstprod'
# ... full env + pulumi login azblob://pulumi-state ...
dotnet run --project DigitalBrain.Deploy.csproj -- up --stack dev -- --image-tag sp1
```

The project is intentionally outside Brain.slnx.
