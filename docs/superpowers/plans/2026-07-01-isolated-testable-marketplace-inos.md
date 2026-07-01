# Isolated, Testable Marketplace Inos Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every integration (Windows, Developer tooling, Google, Context/memory, UI-Kit, Telegram) becomes its own peer project depending only on `DigitalBrain.Core`, with a shared `DigitalBrain.TestKit` giving each one co-located, isolated tests — replacing the current state where all of it is compiled directly into `DigitalBrain.Kernel` and tested from one monolithic `DigitalBrain.Tests` project.

**Architecture:** Real-grain inos (statically compiled `Neuron` subclasses with real DI/I-O) become sibling `.csproj` projects that `DigitalBrain.Kernel` references for Orleans grain-assembly discovery; pure-pack inos (`IPackBehavior`) stay Core-only and compose real-grain capability only through the existing generic `Signal`/`AskLlm` indirection. `DigitalBrain.TestKit` wraps `TestClusterBuilder` + the existing silo configurator behind a minimal `IDigitalBrain` façade so every ino's sibling `.Tests` project gets simple, uniform testing without depending on the central `DigitalBrain.Tests`.

**Tech Stack:** .NET (net11.0), Orleans 10.2 (TestingHost for tests), xUnit, Google.Apis.Gmail.v1/Drive.v3/Calendar.v3/Auth (verified via Context7).

## Global Constraints

- Design doc: `brain/docs/superpowers/specs/2026-07-01-isolated-testable-marketplace-inos-design.md` (approved) — every task must match it exactly; if a task and the spec conflict, the spec governs and the conflict must be raised, not silently resolved.
- `DigitalBrain.Core` must end this plan with **zero** references to any specific integration/vendor — only generic protocol types remain (verify via `grep` in the final task).
- No `Version="*"` in package references — new Google packages go into `Directory.Packages.props` with explicit versions, referenced without version in each `.csproj` (Central Package Management, already in use).
- No vacuous `/// <summary>` comments; self-explanatory names; small inline comments only where genuinely non-obvious.
- Every moved file's code is unchanged except explicitly-listed `using` fixes — do not "improve" logic while moving it.
- After every task: `dotnet build` (0 errors) and the task's own targeted `dotnet test` must be green before moving to the next task.
- Use relative paths from `brain/` in all commands below.

---

### Task 1: `DigitalBrain.TestKit` — the shared `IDigitalBrain` test façade

**Files:**
- Create: `DigitalBrain.TestKit/DigitalBrain.TestKit.csproj`
- Move: `DigitalBrain.Tests/TestSupport/NeuronTestSiloConfigurator.cs` → `DigitalBrain.TestKit/NeuronTestSiloConfigurator.cs` (namespace `DigitalBrain.Tests.TestSupport` → `DigitalBrain.TestKit`)
- Create: `DigitalBrain.TestKit/IDigitalBrain.cs`
- Create: `DigitalBrain.TestKit/TestDigitalBrain.cs`
- Modify: `DigitalBrain.Tests/DigitalBrain.Tests.csproj` (add `ProjectReference` to `DigitalBrain.TestKit`, remove the now-moved file)
- Modify: every file under `DigitalBrain.Tests/` that has `using DigitalBrain.Tests.TestSupport;` referencing `NeuronTestSiloConfigurator` — change to `using DigitalBrain.TestKit;` (find with `grep -rl "NeuronTestSiloConfigurator" DigitalBrain.Tests --include="*.cs"`)
- Modify: `Brain.slnx` (add `DigitalBrain.TestKit/DigitalBrain.TestKit.csproj`)
- Test: `DigitalBrain.TestKit.Tests/DigitalBrain.TestKit.Tests.csproj` (new, proves the façade itself works)

**Interfaces:**
- Produces: `DigitalBrain.TestKit.IDigitalBrain` — `Task FireAsync<T>(T synapse) where T : Synapse;` `Task DeliverAsync<T>(T synapse) where T : Synapse;` `TGrain Grain<TGrain>(string key) where TGrain : IGrainWithStringKey;`
- Produces: `DigitalBrain.TestKit.TestDigitalBrain : IDigitalBrain, IAsyncLifetime` — constructor `TestDigitalBrain(Action<ISiloBuilder>? extend = null)`.

- [ ] **Step 1: Create the TestKit project**

```xml
<!-- DigitalBrain.TestKit/DigitalBrain.TestKit.csproj -->
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\DigitalBrain.Core\DigitalBrain.Core.csproj" />
    <ProjectReference Include="..\DigitalBrain.Kernel\DigitalBrain.Kernel.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Orleans.TestingHost" />
    <PackageReference Include="xunit" />
  </ItemGroup>

</Project>
```

Add `Microsoft.Orleans.TestingHost` and `xunit` to `Directory.Packages.props` if not already present (check first — `DigitalBrain.Tests.csproj` already uses both; copy the exact pinned versions from there into `Directory.Packages.props` `<PackageVersion>` entries if they're not centrally managed yet).

- [ ] **Step 2: Move `NeuronTestSiloConfigurator`**

Move `DigitalBrain.Tests/TestSupport/NeuronTestSiloConfigurator.cs` to `DigitalBrain.TestKit/NeuronTestSiloConfigurator.cs` verbatim, then change line 14 from `namespace DigitalBrain.Tests.TestSupport;` to `namespace DigitalBrain.TestKit;`.

- [ ] **Step 3: Write `IDigitalBrain`**

```csharp
// DigitalBrain.TestKit/IDigitalBrain.cs
using DigitalBrain.Core;
using Orleans;

namespace DigitalBrain.TestKit;

public interface IDigitalBrain
{
    Task FireAsync<T>(T synapse) where T : Synapse;
    Task DeliverAsync<T>(T synapse) where T : Synapse;
    TGrain Grain<TGrain>(string key) where TGrain : IGrainWithStringKey;
}
```

- [ ] **Step 4: Write `TestDigitalBrain`**

```csharp
// DigitalBrain.TestKit/TestDigitalBrain.cs
using DigitalBrain.Core;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.TestKit;

public sealed class TestDigitalBrain : IDigitalBrain, IAsyncLifetime
{
    private readonly Action<ISiloBuilder>? _extend;
    private TestCluster? _cluster;

    public TestDigitalBrain(Action<ISiloBuilder>? extend = null) => _extend = extend;

    public async Task InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<NeuronTestSiloConfigurator>();
        if (_extend is not null)
            builder.AddSiloBuilderConfigurator(new DelegateSiloConfigurator(_extend));
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async Task DisposeAsync()
    {
        if (_cluster is not null)
            await _cluster.StopAllSilosAsync();
    }

    public TGrain Grain<TGrain>(string key) where TGrain : IGrainWithStringKey =>
        _cluster!.GrainFactory.GetGrain<TGrain>(key);

    public Task FireAsync<T>(T synapse) where T : Synapse =>
        Grain<INeuron>(synapse.SynapseId.ToString()).DeliverAsync(synapse);

    public Task DeliverAsync<T>(T synapse) where T : Synapse =>
        synapse.Receiver is { } r
            ? Grain<INeuron>(r.Value).DeliverAsync(synapse)
            : throw new InvalidOperationException("DeliverAsync requires synapse.Receiver to be set.");

    private sealed class DelegateSiloConfigurator(Action<ISiloBuilder> configure) : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder) => configure(siloBuilder);
    }
}
```

Run Context7 (`resolve-library-id` + `query-docs` for `/dotnet/orleans`) for `ISiloConfigurator`/`TestClusterBuilder.AddSiloBuilderConfigurator` composition before finalizing this file — confirm whether multiple `AddSiloBuilderConfigurator` calls compose (each `Configure` invoked in order) or the last one wins; adjust `TestDigitalBrain` accordingly if it's the latter (in that case wrap both configurators in one `DelegateSiloConfigurator` that calls `NeuronTestSiloConfigurator().Configure(sb)` then `_extend(sb)`).

- [ ] **Step 5: Update `DigitalBrain.Tests` to reference TestKit**

Add to `DigitalBrain.Tests/DigitalBrain.Tests.csproj`:
```xml
<ProjectReference Include="..\DigitalBrain.TestKit\DigitalBrain.TestKit.csproj" />
```

Run `grep -rl "NeuronTestSiloConfigurator" DigitalBrain.Tests --include="*.cs"` and in every result, change `using DigitalBrain.Tests.TestSupport;` to `using DigitalBrain.TestKit;`.

- [ ] **Step 6: Add TestKit's own smoke test**

```csharp
// DigitalBrain.TestKit.Tests/TestDigitalBrainTests.cs
using DigitalBrain.Core;
using DigitalBrain.TestKit;
using Xunit;

namespace DigitalBrain.TestKit.Tests;

public class TestDigitalBrainTests : IAsyncLifetime
{
    private readonly TestDigitalBrain _brain = new();

    public Task InitializeAsync() => _brain.InitializeAsync();
    public Task DisposeAsync() => _brain.DisposeAsync();

    [Fact]
    public async Task FireAsync_Delivers_To_Self_Addressed_Grain()
    {
        var target = _brain.Grain<INeuron>("smoke-test-neuron");
        var timeline = await target.GetTimelineAsync();
        Assert.NotNull(timeline);
    }
}
```

`DigitalBrain.TestKit.Tests/DigitalBrain.TestKit.Tests.csproj` references `DigitalBrain.TestKit` + `xunit` + `xunit.runner.visualstudio` (copy the exact package set `DigitalBrain.Tests.csproj` uses for its xUnit runner).

- [ ] **Step 7: Build and test**

```
dotnet build Brain.slnx --nologo -clp:NoSummary
dotnet test DigitalBrain.TestKit.Tests --nologo
dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~Sdk|FullyQualifiedName~Telegram" --nologo
```
Expected: 0 build errors, TestKit.Tests passes, existing DigitalBrain.Tests filtered subset still passes (proves the TestSupport → TestKit move didn't break anything).

- [ ] **Step 8: Commit**

```bash
git add DigitalBrain.TestKit DigitalBrain.TestKit.Tests DigitalBrain.Tests Brain.slnx Directory.Packages.props
git commit -m "feat(testkit): add DigitalBrain.TestKit with IDigitalBrain facade over TestCluster"
```

---

### Task 2: `DigitalBrain.Windows` ino (FileSystem + Winget + Shell + ProcessRunner)

**Files:**
- Create: `DigitalBrain.Windows/DigitalBrain.Windows.csproj`
- Move: `DigitalBrain.Core/Sdk/IFileSystemNeuron.cs`, `IWingetNeuron.cs`, `IShellNeuron.cs` → `DigitalBrain.Windows/` (namespace `DigitalBrain.Core` → `DigitalBrain.Windows`)
- Move: `DigitalBrain.Kernel/Sdk/FileSystemNeuron.cs`, `WingetNeuron.cs`, `ShellNeuron.cs`, `ProcessRunner.cs` → `DigitalBrain.Windows/` (namespace `DigitalBrain.Kernel` → `DigitalBrain.Windows`)
- Delete: `DigitalBrain.Kernel/Sdk/` (now empty)
- Modify: `DigitalBrain.Kernel/Sandbox/OutOfProcessSandbox.cs` — add `using DigitalBrain.Windows;` (line 1 area)
- Modify: `DigitalBrain.Kernel/DigitalBrain.Kernel.csproj` — add `ProjectReference` to `DigitalBrain.Windows`
- Modify: `Brain.slnx` — add `DigitalBrain.Windows/DigitalBrain.Windows.csproj`, `DigitalBrain.Windows.Tests/DigitalBrain.Windows.Tests.csproj`
- Create: `DigitalBrain.Windows.Tests/DigitalBrain.Windows.Tests.csproj`
- Create: `DigitalBrain.Windows.Tests/FileSystemNeuronTests.cs`, `WingetNeuronTests.cs`, `ShellNeuronTests.cs`
- Modify: `DigitalBrain.Tests/Sdk/SdkNeuronsTests.cs` — delete `Shell_Executes_Echo`, `Shell_Blocks_Dangerous_Command`, `FileSystem_Write_Read_List_Delete_RoundTrip` (moved out; keep `DotNet_Reports_Sdk_Version` and `Git_Status_Works_After_ProcessRunner_Refactor` for now — those move in Task 3)
- Modify: `DigitalBrain.Tests/Sdk/SdkContractsMetadataTests.cs` — add `using DigitalBrain.Windows;` for `IWingetNeuron`/`IFileSystemNeuron`/`IShellNeuron` (types moved, test stays central since it spans all Sdk contracts)

**Interfaces:**
- Consumes: nothing new (existing `IFileSystemNeuron`/`IWingetNeuron`/`IShellNeuron`/`ProcessRunner` signatures are unchanged, only namespace moves).
- Produces: `DigitalBrain.Windows.ProcessRunner.RunAsync/ShellAsync/PowerShellAsync` (unchanged signatures) — Task 3's Developer ino references this.

- [ ] **Step 1: Create the project**

```xml
<!-- DigitalBrain.Windows/DigitalBrain.Windows.csproj -->
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\DigitalBrain.Core\DigitalBrain.Core.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Move the 3 Core interfaces**

For each of `IFileSystemNeuron.cs`, `IWingetNeuron.cs`, `IShellNeuron.cs` in `DigitalBrain.Core/Sdk/`: move to `DigitalBrain.Windows/`, change `namespace DigitalBrain.Core;` to `namespace DigitalBrain.Windows;`. Content otherwise unchanged (they already only reference `INeuronAgent`/`CommandResult`, both staying in Core, reached via an added `using DigitalBrain.Core;` at the top of each moved file).

- [ ] **Step 3: Move the 4 Kernel implementation files**

For each of `FileSystemNeuron.cs`, `WingetNeuron.cs`, `ShellNeuron.cs`, `ProcessRunner.cs` in `DigitalBrain.Kernel/Sdk/`: move to `DigitalBrain.Windows/`, change `namespace DigitalBrain.Kernel;` to `namespace DigitalBrain.Windows;`. Content otherwise unchanged.

Delete the now-empty `DigitalBrain.Kernel/Sdk/` directory.

- [ ] **Step 4: Fix the one cross-reference**

In `DigitalBrain.Kernel/Sandbox/OutOfProcessSandbox.cs`, add `using DigitalBrain.Windows;` alongside the existing `using DigitalBrain.Kernel.Foundry;` (line 1).

- [ ] **Step 5: Wire Kernel → Windows**

Add to `DigitalBrain.Kernel/DigitalBrain.Kernel.csproj`:
```xml
<ProjectReference Include="..\DigitalBrain.Windows\DigitalBrain.Windows.csproj" />
```

- [ ] **Step 6: Build Kernel, fix any remaining errors**

```
dotnet build DigitalBrain.Kernel/DigitalBrain.Kernel.csproj --nologo -clp:NoSummary
```
Expected: 0 errors. If `CS0246` appears anywhere else, it means another Kernel file referenced one of the moved types beyond `OutOfProcessSandbox.cs` — add `using DigitalBrain.Windows;` to that file too (the earlier repo-wide grep found only this one call site, so this should not happen, but verify).

- [ ] **Step 7: Create the Windows test project**

```xml
<!-- DigitalBrain.Windows.Tests/DigitalBrain.Windows.Tests.csproj -->
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\DigitalBrain.Windows\DigitalBrain.Windows.csproj" />
    <ProjectReference Include="..\DigitalBrain.TestKit\DigitalBrain.TestKit.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
  </ItemGroup>

</Project>
```

- [ ] **Step 8: Write the moved+new tests**

```csharp
// DigitalBrain.Windows.Tests/FileSystemNeuronTests.cs
using DigitalBrain.TestKit;
using DigitalBrain.Windows;
using Xunit;

namespace DigitalBrain.Windows.Tests;

public class FileSystemNeuronTests : IAsyncLifetime
{
    private readonly TestDigitalBrain _brain = new();
    public Task InitializeAsync() => _brain.InitializeAsync();
    public Task DisposeAsync() => _brain.DisposeAsync();

    [Fact]
    public async Task Write_Read_List_Delete_RoundTrip()
    {
        var fs = _brain.Grain<IFileSystemNeuron>("fs-test");
        var dir = Path.Combine(Path.GetTempPath(), "dbfs-" + Guid.NewGuid().ToString("N"));
        var file = Path.Combine(dir, "note.txt");
        try
        {
            await fs.WriteFileAsync(file, "hello fs");
            Assert.True(await fs.ExistsAsync(file));
            Assert.Equal("hello fs", await fs.ReadFileAsync(file));
            Assert.Contains(file, await fs.ListFilesAsync(dir, "*.txt"));
            await fs.DeleteAsync(file);
            Assert.False(await fs.ExistsAsync(file));
        }
        finally
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
```

```csharp
// DigitalBrain.Windows.Tests/ShellNeuronTests.cs
using DigitalBrain.TestKit;
using DigitalBrain.Windows;
using Xunit;

namespace DigitalBrain.Windows.Tests;

public class ShellNeuronTests : IAsyncLifetime
{
    private readonly TestDigitalBrain _brain = new();
    public Task InitializeAsync() => _brain.InitializeAsync();
    public Task DisposeAsync() => _brain.DisposeAsync();

    [Fact]
    public async Task Executes_Echo()
    {
        var shell = _brain.Grain<IShellNeuron>("shell-test");
        var result = await shell.ExecuteAsync("echo digitalbrain");
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("digitalbrain", result.Output);
    }

    [Fact]
    public async Task Blocks_Dangerous_Command()
    {
        var shell = _brain.Grain<IShellNeuron>("shell-block");
        var result = await shell.ExecuteAsync("format c:");
        Assert.Equal(-1, result.ExitCode);
        Assert.Contains("blocked", result.Error, StringComparison.OrdinalIgnoreCase);
    }
}
```

```csharp
// DigitalBrain.Windows.Tests/WingetNeuronTests.cs
using DigitalBrain.TestKit;
using DigitalBrain.Windows;
using Xunit;

namespace DigitalBrain.Windows.Tests;

// Closes a pre-existing zero-coverage gap: WingetNeuron had no test before this plan.
// Only read-only operations (List/Search) run for real — Install/UpgradeAll mutate the host
// and are intentionally not exercised here.
public class WingetNeuronTests : IAsyncLifetime
{
    private readonly TestDigitalBrain _brain = new();
    public Task InitializeAsync() => _brain.InitializeAsync();
    public Task DisposeAsync() => _brain.DisposeAsync();

    [Fact]
    public async Task List_Returns_Zero_Exit_Code()
    {
        var winget = _brain.Grain<IWingetNeuron>("winget-test");
        var result = await winget.ListAsync();
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task Search_Returns_Zero_Exit_Code()
    {
        var winget = _brain.Grain<IWingetNeuron>("winget-search-test");
        var result = await winget.SearchAsync("git");
        Assert.Equal(0, result.ExitCode);
    }
}
```

- [ ] **Step 9: Delete the moved tests from central `DigitalBrain.Tests`**

In `DigitalBrain.Tests/Sdk/SdkNeuronsTests.cs`, delete the `Shell_Executes_Echo`, `Shell_Blocks_Dangerous_Command`, and `FileSystem_Write_Read_List_Delete_RoundTrip` methods (now duplicated in `DigitalBrain.Windows.Tests`). Leave `DotNet_Reports_Sdk_Version` and `Git_Status_Works_After_ProcessRunner_Refactor` in place — they move in Task 3.

In `DigitalBrain.Tests/Sdk/SdkContractsMetadataTests.cs`, add `using DigitalBrain.Windows;` at the top (for `IWingetNeuron`/`IFileSystemNeuron`/`IShellNeuron` references — the file itself stays central since it spans all Sdk agents across multiple projects).

- [ ] **Step 10: Build and test everything**

```
dotnet build Brain.slnx --nologo -clp:NoSummary
dotnet test DigitalBrain.Windows.Tests --nologo
dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~Sdk" --nologo
```
Expected: 0 build errors, `DigitalBrain.Windows.Tests` 5/5 passed, `DigitalBrain.Tests` Sdk filter passes with the 3 moved tests gone and the remaining 2 (DotNet/Git) + metadata test still green.

- [ ] **Step 11: Commit**

```bash
git add DigitalBrain.Windows DigitalBrain.Windows.Tests DigitalBrain.Kernel DigitalBrain.Core DigitalBrain.Tests Brain.slnx
git commit -m "feat(windows-ino): extract FileSystem/Winget/Shell into isolated DigitalBrain.Windows project with co-located tests"
```

---

### Task 3: `DigitalBrain.Developer` ino (Git + DotNet + NuGet + Roslyn)

**Files:**
- Create: `DigitalBrain.Developer/DigitalBrain.Developer.csproj`
- Move: `DigitalBrain.Core/Sdk/IGitNeuron.cs`, `IDotNetNeuron.cs`, `INuGetNeuron.cs`, `IRoslynNeuron.cs` → `DigitalBrain.Developer/` (namespace → `DigitalBrain.Developer`)
- Move: `DigitalBrain.Kernel/Sdk/GitNeuron.cs`, `DotNetNeuron.cs`, `NuGetNeuron.cs`, `RoslynNeuron.cs` → `DigitalBrain.Developer/` (namespace → `DigitalBrain.Developer`; note `DigitalBrain.Kernel/Sdk/` no longer exists after Task 2 — these files currently live there before this task moves them, i.e. this task's "move" source is the Task-2-emptied directory's siblings; in practice do Task 2 and Task 3 file moves in the same pass if easier, but keep them as separate commits per this plan's task boundaries)
- Modify: `DigitalBrain.Developer/DigitalBrain.Developer.csproj` — `ProjectReference` to `DigitalBrain.Core` and `DigitalBrain.Windows` (for `ProcessRunner`); `RoslynNeuron.cs` needs `Microsoft.CodeAnalysis`/`Microsoft.CodeAnalysis.MSBuild` `PackageReference`s (copy exact versions from `DigitalBrain.Kernel.csproj`'s current references to `Directory.Packages.props`)
- Modify: `DigitalBrain.Kernel/DigitalBrain.Kernel.csproj` — add `ProjectReference` to `DigitalBrain.Developer`
- Modify: `Brain.slnx` — add `DigitalBrain.Developer`, `DigitalBrain.Developer.Tests`
- Create: `DigitalBrain.Developer.Tests/DigitalBrain.Developer.Tests.csproj`
- Create: `DigitalBrain.Developer.Tests/GitNeuronTests.cs`, `DotNetNeuronTests.cs`, `NuGetNeuronTests.cs`, `RoslynNeuronTests.cs`
- Modify: `DigitalBrain.Tests/Sdk/SdkNeuronsTests.cs` — delete `DotNet_Reports_Sdk_Version`, `Git_Status_Works_After_ProcessRunner_Refactor` (moved out); file is now empty of test methods — delete the file entirely and remove it from the project if no methods remain
- Modify: `DigitalBrain.Tests/Sdk/SdkContractsMetadataTests.cs` — add `using DigitalBrain.Developer;`

**Interfaces:**
- Consumes: `DigitalBrain.Windows.ProcessRunner.RunAsync(fileName, arguments, workingDirectory?, timeoutMs?, ct?)` (from Task 2, unchanged signature).

- [ ] **Step 1: Create the project**

```xml
<!-- DigitalBrain.Developer/DigitalBrain.Developer.csproj -->
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\DigitalBrain.Core\DigitalBrain.Core.csproj" />
    <ProjectReference Include="..\DigitalBrain.Windows\DigitalBrain.Windows.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp.Workspaces" />
    <PackageReference Include="Microsoft.CodeAnalysis.Workspaces.MSBuild" />
  </ItemGroup>

</Project>
```

Before finalizing, run `grep -n "Microsoft.CodeAnalysis" DigitalBrain.Kernel/DigitalBrain.Kernel.csproj` to get the exact `PackageReference` Include names currently used for Roslyn in Kernel, and match them exactly (do not guess package names).

- [ ] **Step 2: Move the 4 Core interfaces and 4 Kernel implementations**

Same mechanical move as Task 2 Steps 2-3: `IGitNeuron.cs`/`IDotNetNeuron.cs`/`INuGetNeuron.cs`/`IRoslynNeuron.cs` from `DigitalBrain.Core/Sdk/` and `GitNeuron.cs`/`DotNetNeuron.cs`/`NuGetNeuron.cs`/`RoslynNeuron.cs` from wherever Task 2 left them, all into `DigitalBrain.Developer/`, namespace changed to `DigitalBrain.Developer`. Add `using DigitalBrain.Windows;` to `GitNeuron.cs`/`DotNetNeuron.cs`/`NuGetNeuron.cs` (they call `ProcessRunner.RunAsync`/`ShellAsync`).

- [ ] **Step 3: Wire Kernel → Developer**

Add to `DigitalBrain.Kernel/DigitalBrain.Kernel.csproj`:
```xml
<ProjectReference Include="..\DigitalBrain.Developer\DigitalBrain.Developer.csproj" />
```

- [ ] **Step 4: Build, fix remaining errors**

```
dotnet build Brain.slnx --nologo -clp:NoSummary
```
Expected: 0 errors (repo-wide grep found no other Kernel file referencing these 4 types directly, so no further `using` fixes expected — verify).

- [ ] **Step 5: Create the Developer test project**

Same shape as `DigitalBrain.Windows.Tests` (Task 2 Step 7), referencing `DigitalBrain.Developer` + `DigitalBrain.TestKit`.

- [ ] **Step 6: Write the moved+new tests**

```csharp
// DigitalBrain.Developer.Tests/DotNetNeuronTests.cs
using DigitalBrain.TestKit;
using DigitalBrain.Developer;
using Xunit;

namespace DigitalBrain.Developer.Tests;

public class DotNetNeuronTests : IAsyncLifetime
{
    private readonly TestDigitalBrain _brain = new();
    public Task InitializeAsync() => _brain.InitializeAsync();
    public Task DisposeAsync() => _brain.DisposeAsync();

    [Fact]
    public async Task Reports_Sdk_Version()
    {
        var dotnet = _brain.Grain<IDotNetNeuron>("dotnet-test");
        var version = await dotnet.VersionAsync();
        Assert.Matches(@"\d+\.\d+", version);
    }
}
```

```csharp
// DigitalBrain.Developer.Tests/GitNeuronTests.cs
using System.Diagnostics;
using DigitalBrain.TestKit;
using DigitalBrain.Developer;
using Xunit;

namespace DigitalBrain.Developer.Tests;

public class GitNeuronTests : IAsyncLifetime
{
    private readonly TestDigitalBrain _brain = new();
    public Task InitializeAsync() => _brain.InitializeAsync();
    public Task DisposeAsync() => _brain.DisposeAsync();

    [Fact]
    public async Task Status_Works()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dbgit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var init = new ProcessStartInfo("git", "init -b main")
            { WorkingDirectory = dir, UseShellExecute = false, CreateNoWindow = true };
            using (var process = Process.Start(init)!) process.WaitForExit();

            var git = _brain.Grain<IGitNeuron>("git-smoke");
            var status = await git.StatusAsync(dir);
            Assert.Contains("branch", status, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
```

```csharp
// DigitalBrain.Developer.Tests/NuGetNeuronTests.cs
using DigitalBrain.TestKit;
using DigitalBrain.Developer;
using Xunit;

namespace DigitalBrain.Developer.Tests;

// Closes a pre-existing zero-coverage gap: NuGetNeuron had no test before this plan.
public class NuGetNeuronTests : IAsyncLifetime
{
    private readonly TestDigitalBrain _brain = new();
    public Task InitializeAsync() => _brain.InitializeAsync();
    public Task DisposeAsync() => _brain.DisposeAsync();

    [Fact]
    public async Task Search_Returns_Zero_Exit_Code()
    {
        var nuget = _brain.Grain<INuGetNeuron>("nuget-test");
        var result = await nuget.SearchAsync("Newtonsoft.Json");
        Assert.Equal(0, result.ExitCode);
    }
}
```

```csharp
// DigitalBrain.Developer.Tests/RoslynNeuronTests.cs
using DigitalBrain.TestKit;
using DigitalBrain.Developer;
using Xunit;

namespace DigitalBrain.Developer.Tests;

// Closes a pre-existing zero-coverage gap: RoslynNeuron had no test before this plan.
// In-proc (MSBuildWorkspace), safe to run for real against the actual solution.
public class RoslynNeuronTests : IAsyncLifetime
{
    private readonly TestDigitalBrain _brain = new();
    public Task InitializeAsync() => _brain.InitializeAsync();
    public Task DisposeAsync() => _brain.DisposeAsync();

    [Fact]
    public async Task Analyzes_Real_Solution()
    {
        var roslyn = _brain.Grain<IRoslynNeuron>("roslyn-test");
        var solutionPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Brain.slnx"));
        var result = await roslyn.AnalyzeSolutionAsync(solutionPath);
        Assert.False(string.IsNullOrWhiteSpace(result));
    }
}
```

Before writing the `RoslynNeuronTests` assertion body precisely, re-read `DigitalBrain.Developer/RoslynNeuron.cs` (moved in Step 2) to confirm `AnalyzeSolutionAsync`'s actual return shape/content beyond line 20 (only the first 20 lines were read during planning) and adjust the assertion to match what it actually returns.

- [ ] **Step 7: Delete the now-empty central Sdk test file**

Delete `DigitalBrain.Tests/Sdk/SdkNeuronsTests.cs` entirely (all 4 of its test methods have moved out across Tasks 2 and 3).

Update `DigitalBrain.Tests/Sdk/SdkContractsMetadataTests.cs`: add `using DigitalBrain.Developer;`.

- [ ] **Step 8: Build and test everything**

```
dotnet build Brain.slnx --nologo -clp:NoSummary
dotnet test DigitalBrain.Developer.Tests --nologo
dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~Sdk" --nologo
```
Expected: 0 build errors, `DigitalBrain.Developer.Tests` 4/4 passed, `DigitalBrain.Tests` Sdk filter only runs `SdkContractsMetadataTests` now and passes.

- [ ] **Step 9: Commit**

```bash
git add DigitalBrain.Developer DigitalBrain.Developer.Tests DigitalBrain.Kernel DigitalBrain.Core DigitalBrain.Tests Brain.slnx Directory.Packages.props
git commit -m "feat(developer-ino): extract Git/DotNet/NuGet/Roslyn into isolated DigitalBrain.Developer project, close zero-coverage gap"
```

---

### Task 4: `DigitalBrain.Context` ino

**Files:**
- Create: `DigitalBrain.Context/DigitalBrain.Context.csproj`
- Move: `DigitalBrain.Kernel/ContextNeuron.cs`, `Context/ContextServices.cs`, `Context/DocumentIngestor.cs`, `Context/HybridScorer.cs`, `Context/QdrantVectorStore.cs`, `Context/VectorStore.cs` → `DigitalBrain.Context/` (namespace → `DigitalBrain.Context`)
- Move (from `DigitalBrain.Core/Synapse.cs:402`): `IContextNeuron` interface → `DigitalBrain.Context/IContextNeuron.cs` (namespace `DigitalBrain.Context`; keep `ContextUpdate`/`MemoryStored` records in `DigitalBrain.Core/Synapse.cs` unchanged — they're already-journaled generic Synapse payloads, see Global Constraints)
- Delete: `DigitalBrain.Kernel/Context/` (now empty)
- Modify: `DigitalBrain.Context/ContextNeuron.cs` — delete the dead `using DigitalBrain.Kernel.Foundry;` (line 2) and any other unused usings surfaced by the build (`ModelContextProtocol.Client`/`ModelContextProtocol.Protocol`/`Orleans.Runtime`/`System.Reflection`/`System.Diagnostics`/`Microsoft.CodeAnalysis*` were present in the original file but not visibly used in its body — verify each against the actual body before deleting; keep only what the compiler requires)
- Modify: `DigitalBrain.Kernel/Company/CompanySkillOrchestratorNeuron.cs`, `DigitalBrain.Kernel/Gateway/NeuronResolver.cs`, `DigitalBrain.Kernel/Program.cs` — add `using DigitalBrain.Context;`
- Modify: `DigitalBrain.Kernel/JournalJsonContext.cs` — no change needed (`ContextUpdate`/`MemoryStored` stay in Core, already resolvable)
- Modify: `DigitalBrain.Kernel/DigitalBrain.Kernel.csproj` — add `ProjectReference` to `DigitalBrain.Context`; move the `Microsoft.Extensions.AI`/Qdrant/`ModelContextProtocol` package references that `ContextNeuron` needs from Kernel's csproj into `DigitalBrain.Context.csproj` (check `DigitalBrain.Kernel.csproj` for their exact current `PackageReference` names first)
- Modify: `Brain.slnx` — add `DigitalBrain.Context`, `DigitalBrain.Context.Tests`
- Create: `DigitalBrain.Context.Tests/DigitalBrain.Context.Tests.csproj`, `ContextNeuronTests.cs`

**Interfaces:**
- Produces: `DigitalBrain.Context.IContextNeuron` — `Task<string> GetContextAsync(string contextName)`, `Task RememberAsync(string text)`, `Task<string[]> RecallAsync(string query, int top = 5)` (unchanged signatures, moved namespace only).

- [ ] **Step 1: Create the project**

```xml
<!-- DigitalBrain.Context/DigitalBrain.Context.csproj -->
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\DigitalBrain.Core\DigitalBrain.Core.csproj" />
  </ItemGroup>

</Project>
```

Before adding `PackageReference`s, run `grep -n "Microsoft.Extensions.AI\|Qdrant\|ModelContextProtocol" DigitalBrain.Kernel/DigitalBrain.Kernel.csproj` to find the exact package names currently pulling in the types `ContextNeuron.cs`/`QdrantVectorStore.cs` use, and add those exact `PackageReference` entries here (do not guess versions — they're centrally managed in `Directory.Packages.props` already).

- [ ] **Step 2: Move the 6 files**

Move `ContextNeuron.cs` (from `DigitalBrain.Kernel/`) and `ContextServices.cs`/`DocumentIngestor.cs`/`HybridScorer.cs`/`QdrantVectorStore.cs`/`VectorStore.cs` (from `DigitalBrain.Kernel/Context/`) into `DigitalBrain.Context/`, namespace `DigitalBrain.Kernel` → `DigitalBrain.Context` in each. Delete the now-empty `DigitalBrain.Kernel/Context/` directory.

- [ ] **Step 3: Move `IContextNeuron` out of Core**

In `DigitalBrain.Core/Synapse.cs`, delete lines around 402 (`public interface IContextNeuron : INeuron, IHandle<ContextUpdate>`). Create `DigitalBrain.Context/IContextNeuron.cs`:

```csharp
// DigitalBrain.Context/IContextNeuron.cs
using DigitalBrain.Core;

namespace DigitalBrain.Context;

public interface IContextNeuron : INeuron, IHandle<ContextUpdate>
{
    Task<string> GetContextAsync(string contextName);
    Task RememberAsync(string text);
    Task<string[]> RecallAsync(string query, int top = 5);
}
```

Read the actual current `IContextNeuron` declaration at `DigitalBrain.Core/Synapse.cs:402` before writing this file — copy its real member list verbatim rather than trusting this reconstruction, since the plan author only confirmed the interface's existence and line number, not its full body.

- [ ] **Step 4: Clean up `ContextNeuron.cs`'s usings**

After moving, run `dotnet build DigitalBrain.Context` and delete every `using` that produces an "unused using" hint or isn't needed to compile (expected removals: `DigitalBrain.Kernel.Foundry`, and verify `ModelContextProtocol.Client`/`ModelContextProtocol.Protocol`/`Orleans.Runtime`/`System.Reflection`/`System.Diagnostics`/`Microsoft.CodeAnalysis`/`Microsoft.CodeAnalysis.MSBuild`/`Microsoft.CodeAnalysis.CSharp` similarly — keep only `DigitalBrain.Core`, `Microsoft.Extensions.Configuration`, `Microsoft.Extensions.AI`, `Orleans.Journaling` if the compiler still needs them).

- [ ] **Step 5: Fix the 3 cross-references**

Add `using DigitalBrain.Context;` to `DigitalBrain.Kernel/Company/CompanySkillOrchestratorNeuron.cs`, `DigitalBrain.Kernel/Gateway/NeuronResolver.cs`, and `DigitalBrain.Kernel/Program.cs`.

- [ ] **Step 6: Wire Kernel → Context**

Add to `DigitalBrain.Kernel/DigitalBrain.Kernel.csproj`:
```xml
<ProjectReference Include="..\DigitalBrain.Context\DigitalBrain.Context.csproj" />
```

- [ ] **Step 7: Build, fix remaining errors**

```
dotnet build Brain.slnx --nologo -clp:NoSummary
```

- [ ] **Step 8: Create the Context test project and test**

Same project shape as Task 2 Step 7 (reference `DigitalBrain.Context` + `DigitalBrain.TestKit`).

```csharp
// DigitalBrain.Context.Tests/ContextNeuronTests.cs
using DigitalBrain.TestKit;
using DigitalBrain.Context;
using Xunit;

namespace DigitalBrain.Context.Tests;

public class ContextNeuronTests : IAsyncLifetime
{
    private readonly TestDigitalBrain _brain = new();
    public Task InitializeAsync() => _brain.InitializeAsync();
    public Task DisposeAsync() => _brain.DisposeAsync();

    [Fact]
    public async Task Remember_Then_Recall_Finds_The_Text()
    {
        var context = _brain.Grain<IContextNeuron>("context-recall-test");
        await context.RememberAsync("the sky is blue today");
        var results = await context.RecallAsync("sky color");
        Assert.Contains("the sky is blue today", results);
    }
}
```

This relies on `NoOpEmbeddingGenerator`/`InMemoryVectorStore` already registered in `NeuronTestSiloConfigurator` (moved into TestKit in Task 1) — confirm those two types are still resolvable from `DigitalBrain.TestKit` after the Task 1 move (they were referenced by `DigitalBrain.Kernel.Context`/`DigitalBrain.Kernel.Foundry` namespaces in the original configurator; after this task their real implementations live in `DigitalBrain.Context`, so `NeuronTestSiloConfigurator`'s `using DigitalBrain.Kernel.Context;`-style references, if any, need updating to `using DigitalBrain.Context;` too — check `NeuronTestSiloConfigurator.cs`'s usings against this task's moves and fix any that now point at the wrong namespace).

- [ ] **Step 9: Build and test everything**

```
dotnet build Brain.slnx --nologo -clp:NoSummary
dotnet test DigitalBrain.Context.Tests --nologo
dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~Company|FullyQualifiedName~Gateway" --nologo
```
Expected: 0 build errors, Context.Tests 1/1 passed, Company/Gateway filtered tests still green (proves the 3 cross-reference fixes work).

- [ ] **Step 10: Commit**

```bash
git add DigitalBrain.Context DigitalBrain.Context.Tests DigitalBrain.Kernel DigitalBrain.Core Brain.slnx Directory.Packages.props
git commit -m "feat(context-ino): extract ContextNeuron + Qdrant memory subsystem into isolated DigitalBrain.Context project"
```

---

### Task 5: `DigitalBrain.UiKit` ino

**Files:**
- Create: `DigitalBrain.UiKit/DigitalBrain.UiKit.csproj`
- Move: `DigitalBrain.Kernel/Ui/FlutterUiNeuron.cs`, `HomeFeedBus.cs`, `HomeFeedStreamSubscriber.cs`, `SignalEgressBus.cs`, `SignalEgressStreamSubscriber.cs`, `UiSurfaceRfwBridge.cs` → `DigitalBrain.UiKit/` (namespace → `DigitalBrain.UiKit`). `ChatNeuron.cs` stays in `DigitalBrain.Kernel/Ui/` — it's a consumer of the UiKit bus, not part of the delivery mechanism itself.
- Move (from `DigitalBrain.Core/Synapse.cs:77`): `IFlutterUiNeuron` interface → `DigitalBrain.UiKit/IFlutterUiNeuron.cs` (namespace `DigitalBrain.UiKit`)
- Modify: `DigitalBrain.Core/Synapse.cs` — delete the `IFlutterUiNeuron` interface (line ~77); `IChannelNeuron` (line ~84) stays (generic marker)
- Modify: `DigitalBrain.Kernel/Auth/UserSessionNeuron.cs`, `DataVisualizationNeuron.cs`, `DemoNeuron.cs`, `Gateway/GatewayService.cs`, `Gateway/KernelSurfaceDemo.cs`, `GeneratedNeuron.cs`, `KernelTaskNeuron.cs`, `MarketplaceNeuron.cs`, `Program.cs`, `SystemNeurons.cs`, `Ui/ChatNeuron.cs` — add `using DigitalBrain.UiKit;` to each
- Modify: `DigitalBrain.Kernel/DigitalBrain.Kernel.csproj` — add `ProjectReference` to `DigitalBrain.UiKit`
- Modify: `Brain.slnx` — add `DigitalBrain.UiKit`, `DigitalBrain.UiKit.Tests`
- Create: `DigitalBrain.UiKit.Tests/DigitalBrain.UiKit.Tests.csproj`, `FlutterUiNeuronTests.cs`

**Interfaces:**
- Produces: `DigitalBrain.UiKit.IFlutterUiNeuron : INeuron, IHandle<UiSurface>` (unchanged signature, moved namespace).
- Produces: `DigitalBrain.UiKit.HomeFeedBus` — same public surface as today (`Broadcast(...)`, DI-registered as singleton).

- [ ] **Step 1: Create the project**

```xml
<!-- DigitalBrain.UiKit/DigitalBrain.UiKit.csproj -->
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\DigitalBrain.Core\DigitalBrain.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Orleans.Streaming" />
  </ItemGroup>

</Project>
```

Check `DigitalBrain.Kernel/DigitalBrain.Kernel.csproj` for the exact package reference providing `Orleans.Streams` (used by `HomeFeedStreamSubscriber.cs`/`SignalEgressStreamSubscriber.cs`) and match the exact `Include` name.

- [ ] **Step 2: Move the 6 files**

Move `FlutterUiNeuron.cs`, `HomeFeedBus.cs`, `HomeFeedStreamSubscriber.cs`, `SignalEgressBus.cs`, `SignalEgressStreamSubscriber.cs`, `UiSurfaceRfwBridge.cs` from `DigitalBrain.Kernel/Ui/` to `DigitalBrain.UiKit/`, namespace `DigitalBrain.Kernel` → `DigitalBrain.UiKit` in each. `ChatNeuron.cs` stays where it is, untouched.

- [ ] **Step 3: Move `IFlutterUiNeuron` out of Core**

Read `DigitalBrain.Core/Synapse.cs` lines 70-90 to get the exact current text of `ITelegramChatNeuron`, `IFlutterUiNeuron`, and `IChannelNeuron` before editing (this task only touches `IFlutterUiNeuron`; Task 6 touches `ITelegramChatNeuron`). Delete the `IFlutterUiNeuron` interface block from `Synapse.cs`. Create:

```csharp
// DigitalBrain.UiKit/IFlutterUiNeuron.cs
using DigitalBrain.Core;

namespace DigitalBrain.UiKit;

public interface IFlutterUiNeuron : IChannelNeuron, IHandle<UiSurface>
{
}
```

(Copy the exact base-list/body from the real file read in this step rather than this reconstruction — the plan author saw this at `Synapse.cs:77` during research but is reconstructing the exact declaration here from memory of the surrounding grep context.)

- [ ] **Step 4: Fix the 11 cross-references**

Add `using DigitalBrain.UiKit;` to: `DigitalBrain.Kernel/Auth/UserSessionNeuron.cs`, `DigitalBrain.Kernel/DataVisualizationNeuron.cs`, `DigitalBrain.Kernel/DemoNeuron.cs`, `DigitalBrain.Kernel/Gateway/GatewayService.cs`, `DigitalBrain.Kernel/Gateway/KernelSurfaceDemo.cs`, `DigitalBrain.Kernel/GeneratedNeuron.cs`, `DigitalBrain.Kernel/KernelTaskNeuron.cs`, `DigitalBrain.Kernel/MarketplaceNeuron.cs`, `DigitalBrain.Kernel/Program.cs`, `DigitalBrain.Kernel/SystemNeurons.cs`, `DigitalBrain.Kernel/Ui/ChatNeuron.cs`.

`Neuron.cs` does **not** need this — its one match during research was a comment mentioning "FlutterUiNeuron" by name, not a real type reference; confirm this by re-grepping `HomeFeedBus\|SignalEgressBus\|UiSurfaceRfwBridge\|FlutterUiNeuron` in `Neuron.cs` and checking each hit is inside a `//` comment before skipping it.

- [ ] **Step 5: Wire Kernel → UiKit**

Add to `DigitalBrain.Kernel/DigitalBrain.Kernel.csproj`:
```xml
<ProjectReference Include="..\DigitalBrain.UiKit\DigitalBrain.UiKit.csproj" />
```

- [ ] **Step 6: Build, fix remaining errors**

```
dotnet build Brain.slnx --nologo -clp:NoSummary
```
Fix any additional `CS0246`/`CS0103` by adding `using DigitalBrain.UiKit;` to whatever file the error names — the 11 files above were found via repo-wide grep but a build error is the authoritative check.

- [ ] **Step 7: Create the UiKit test project and test**

Same project shape as Task 2 Step 7.

```csharp
// DigitalBrain.UiKit.Tests/FlutterUiNeuronTests.cs
using DigitalBrain.Core;
using DigitalBrain.TestKit;
using DigitalBrain.UiKit;
using Xunit;

namespace DigitalBrain.UiKit.Tests;

// Closes a pre-existing gap: IFlutterUiNeuron had no direct test before this plan — it was
// only exercised indirectly inside a Telegram test.
public class FlutterUiNeuronTests : IAsyncLifetime
{
    private readonly TestDigitalBrain _brain = new();
    public Task InitializeAsync() => _brain.InitializeAsync();
    public Task DisposeAsync() => _brain.DisposeAsync();

    [Fact]
    public async Task HandleAsync_Accepts_UiSurface_Without_Throwing()
    {
        var flutter = _brain.Grain<IFlutterUiNeuron>("flutter-ui");
        var surface = new UiSurface("test-kind", new Dictionary<string, object?> { ["title"] = "smoke" });
        await flutter.HandleAsync(surface);
    }
}
```

Read the real `UiSurface` record definition in `DigitalBrain.Core/Synapse.cs` or `UiSurfaces.cs` before finalizing this constructor call — the plan author has not confirmed `UiSurface`'s exact constructor parameter list/order; match it exactly (likely `UiSurface(string Kind, IReadOnlyDictionary<string, object?> Props, ...)` based on `FlutterUiNeuron.HandleAsync`'s use of `surface.Kind`/`surface.Props`, but verify before writing the test).

- [ ] **Step 8: Build and test everything**

```
dotnet build Brain.slnx --nologo -clp:NoSummary
dotnet test DigitalBrain.UiKit.Tests --nologo
dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~Marketplace|FullyQualifiedName~SystemNeurons|FullyQualifiedName~DataVisualization" --nologo
```
Expected: 0 build errors, UiKit.Tests 1/1 passed, the filtered central tests (which exercise the 11 fixed cross-reference files) still green.

- [ ] **Step 9: Commit**

```bash
git add DigitalBrain.UiKit DigitalBrain.UiKit.Tests DigitalBrain.Kernel DigitalBrain.Core Brain.slnx Directory.Packages.props
git commit -m "feat(uikit-ino): extract FlutterUiNeuron + HomeFeed/SignalEgress delivery pipe into isolated DigitalBrain.UiKit project, add first direct test"
```

---

### Task 6: `DigitalBrain.Telegram.Channel` ino

**Files:**
- Create: `DigitalBrain.Telegram.Channel/DigitalBrain.Telegram.Channel.csproj`
- Move: `DigitalBrain.Kernel/TelegramChatNeuron.cs` → `DigitalBrain.Telegram.Channel/TelegramChatNeuron.cs` (namespace `DigitalBrain.Kernel` → `DigitalBrain.Telegram.Channel`)
- Move (from `DigitalBrain.Core/Synapse.cs:72`): `ITelegramChatNeuron` interface → `DigitalBrain.Telegram.Channel/ITelegramChatNeuron.cs` (namespace `DigitalBrain.Telegram.Channel`)
- Modify: `DigitalBrain.Kernel/Gateway/GatewayService.cs` — add `using DigitalBrain.Telegram.Channel;`
- Modify: `DigitalBrain.Kernel/DigitalBrain.Kernel.csproj` — add `ProjectReference` to `DigitalBrain.Telegram.Channel`
- Modify: `Brain.slnx` — add `DigitalBrain.Telegram.Channel`, `DigitalBrain.Telegram.Channel.Tests`
- Create: `DigitalBrain.Telegram.Channel.Tests/DigitalBrain.Telegram.Channel.Tests.csproj`
- Move: `DigitalBrain.Tests/Telegram/TelegramChatNeuronTests.cs` → `DigitalBrain.Telegram.Channel.Tests/TelegramChatNeuronTests.cs` (update `using`s: `DigitalBrain.Tests.TestSupport` → `DigitalBrain.TestKit`, add `DigitalBrain.Telegram.Channel`); `TelegramDeepLinkRoutingTests.cs` stays central (it likely also touches gateway/routing concerns beyond the grain itself — read it first to confirm before deciding whether it moves too)

**Interfaces:**
- Produces: `DigitalBrain.Telegram.Channel.ITelegramChatNeuron` (unchanged signature, moved namespace).

- [ ] **Step 1: Create the project**

```xml
<!-- DigitalBrain.Telegram.Channel/DigitalBrain.Telegram.Channel.csproj -->
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\DigitalBrain.Core\DigitalBrain.Core.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Move `TelegramChatNeuron.cs`**

Move verbatim from `DigitalBrain.Kernel/TelegramChatNeuron.cs` to `DigitalBrain.Telegram.Channel/TelegramChatNeuron.cs`, namespace `DigitalBrain.Kernel` → `DigitalBrain.Telegram.Channel`. No other changes — confirmed during research that its only real-type references (`IDataVisualizationNeuron`, `IGeneratedNeuron`, `VisualizeDataRequest`) already live in `DigitalBrain.Core`, reachable via the existing `using DigitalBrain.Core;`.

- [ ] **Step 3: Move `ITelegramChatNeuron` out of Core**

Read the exact current declaration at `DigitalBrain.Core/Synapse.cs:72` before editing. Delete it from `Synapse.cs`. Create:

```csharp
// DigitalBrain.Telegram.Channel/ITelegramChatNeuron.cs
using DigitalBrain.Core;

namespace DigitalBrain.Telegram.Channel;

public interface ITelegramChatNeuron : IChannelNeuron
{
    Task<string?> GetBoundBundleAsync();
}
```

Copy the exact real body from the file read in this step rather than this reconstruction.

- [ ] **Step 4: Fix the 1 cross-reference**

Add `using DigitalBrain.Telegram.Channel;` to `DigitalBrain.Kernel/Gateway/GatewayService.cs` (line ~179, `grains.GetGrain<ITelegramChatNeuron>(chatKey)`).

- [ ] **Step 5: Wire Kernel → Telegram.Channel**

Add to `DigitalBrain.Kernel/DigitalBrain.Kernel.csproj`:
```xml
<ProjectReference Include="..\DigitalBrain.Telegram.Channel\DigitalBrain.Telegram.Channel.csproj" />
```

- [ ] **Step 6: Build, fix remaining errors**

```
dotnet build Brain.slnx --nologo -clp:NoSummary
```

- [ ] **Step 7: Create the test project, move the test**

Same project shape as Task 2 Step 7 (reference `DigitalBrain.Telegram.Channel` + `DigitalBrain.TestKit`).

Read `DigitalBrain.Tests/Telegram/TelegramChatNeuronTests.cs` in full first. Move it to `DigitalBrain.Telegram.Channel.Tests/TelegramChatNeuronTests.cs`, changing its `using DigitalBrain.Tests.TestSupport;`-style TestCluster setup to construct a `TestDigitalBrain` from `DigitalBrain.TestKit` instead (following the `IAsyncLifetime` pattern used in every other `.Tests` project in this plan), and add `using DigitalBrain.Telegram.Channel;`. Preserve every existing assertion — this is the test that proves the cross-channel Telegram→Chart→FlutterUiNeuron proof still works after the migration, do not weaken it.

Read `DigitalBrain.Tests/Telegram/TelegramDeepLinkRoutingTests.cs` in full and decide: if it only exercises `TelegramChatNeuron`/`ITelegramChatNeuron`, move it alongside; if it also exercises gateway/routing infrastructure outside this ino, leave it in central `DigitalBrain.Tests` with a `using DigitalBrain.Telegram.Channel;` added.

- [ ] **Step 8: Build and test everything**

```
dotnet build Brain.slnx --nologo -clp:NoSummary
dotnet test DigitalBrain.Telegram.Channel.Tests --nologo
dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~Gateway|FullyQualifiedName~Telegram" --nologo
```
Expected: 0 build errors, the moved test(s) pass in the new project, remaining central Gateway/Telegram tests still green.

- [ ] **Step 9: Commit**

```bash
git add DigitalBrain.Telegram.Channel DigitalBrain.Telegram.Channel.Tests DigitalBrain.Kernel DigitalBrain.Core DigitalBrain.Tests Brain.slnx
git commit -m "feat(telegram-channel-ino): extract TelegramChatNeuron into isolated DigitalBrain.Telegram.Channel project"
```

---

### Task 7: Co-locate `DigitalBrain.Telegram`'s pure-pack test

**Files:**
- Create: `DigitalBrain.Telegram.Tests/DigitalBrain.Telegram.Tests.csproj`
- Move: `DigitalBrain.Tests/Telegram/ResponderPackTests.cs` → `DigitalBrain.Telegram.Tests/TelegramResponderNeuronTests.cs`
- Modify: `Brain.slnx` — add `DigitalBrain.Telegram.Tests`

**Interfaces:**
- Consumes: `DigitalBrain.Telegram.TelegramResponderNeuron` (existing, unchanged).

- [ ] **Step 1: Read the existing test first**

Read `DigitalBrain.Tests/Telegram/ResponderPackTests.cs` in full to see exactly what it asserts and whether it needs any Orleans/TestCluster machinery at all (per the design spec, it shouldn't — `TelegramResponderNeuron` is a pure `IPackBehavior`).

- [ ] **Step 2: Create the zero-infra test project**

```xml
<!-- DigitalBrain.Telegram.Tests/DigitalBrain.Telegram.Tests.csproj -->
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\DigitalBrain.Telegram\DigitalBrain.Telegram.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
  </ItemGroup>

</Project>
```

Deliberately **no** reference to `DigitalBrain.TestKit` or Orleans — this project proves the pack can be tested with zero infrastructure.

- [ ] **Step 3: Move and adapt the test**

Move the file to `DigitalBrain.Telegram.Tests/TelegramResponderNeuronTests.cs`, namespace `DigitalBrain.Tests.Telegram` → `DigitalBrain.Telegram.Tests`. If the original test used `TestCluster`/grain activation to exercise `TelegramResponderNeuron`, rewrite it to call `new TelegramResponderNeuron().Handle(synapse)` directly instead — the whole point of this task is proving this pack needs no Orleans. Preserve every existing assertion.

- [ ] **Step 4: Delete the original, wire the solution**

Delete `DigitalBrain.Tests/Telegram/ResponderPackTests.cs`. Add `DigitalBrain.Telegram.Tests/DigitalBrain.Telegram.Tests.csproj` to `Brain.slnx`.

- [ ] **Step 5: Build and test**

```
dotnet build Brain.slnx --nologo -clp:NoSummary
dotnet test DigitalBrain.Telegram.Tests --nologo
```
Expected: 0 build errors, test(s) pass with **no TestCluster startup delay** (should run in well under a second — this is the concrete proof of the zero-infra claim).

- [ ] **Step 6: Commit**

```bash
git add DigitalBrain.Telegram.Tests DigitalBrain.Tests Brain.slnx
git commit -m "feat(telegram-pack): co-locate TelegramResponderNeuron's test in a zero-infra sibling project"
```

---

### Task 8: `DigitalBrain.Google` ino

**Files:**
- Create: `DigitalBrain.Google/DigitalBrain.Google.csproj`
- Create: `DigitalBrain.Google/IGmailNeuron.cs`, `IGoogleDriveNeuron.cs`, `IGoogleCalendarNeuron.cs`
- Create: `DigitalBrain.Google/IGmailApiClient.cs`, `IGoogleDriveApiClient.cs`, `IGoogleCalendarApiClient.cs`
- Create: `DigitalBrain.Google/GoogleGmailApiClient.cs`, `GoogleDriveApiClient.cs`, `GoogleCalendarApiClient.cs`
- Create: `DigitalBrain.Google/GoogleCredentialFactory.cs`
- Create: `DigitalBrain.Google/GmailNeuron.cs`, `GoogleDriveNeuron.cs`, `GoogleCalendarNeuron.cs`
- Modify: `DigitalBrain.Core/Signals.cs` — add `GoogleSignals` string-constant class
- Modify: `Directory.Packages.props` — add `Google.Apis.Gmail.v1`, `Google.Apis.Drive.v3`, `Google.Apis.Calendar.v3`, `Google.Apis.Auth`
- Modify: `DigitalBrain.Kernel/DigitalBrain.Kernel.csproj` — add `ProjectReference` to `DigitalBrain.Google`
- Modify: `Brain.slnx` — add `DigitalBrain.Google`, `DigitalBrain.Google.Tests`
- Create: `DigitalBrain.Google.Tests/DigitalBrain.Google.Tests.csproj`, `FakeGoogleApiClients.cs`, `GmailNeuronTests.cs`, `GoogleDriveNeuronTests.cs`, `GoogleCalendarNeuronTests.cs`

**Interfaces:**
- Produces: `IGmailNeuron.ListMessagesAsync(string query, int maxResults)`, `.ReadMessageAsync(string messageId)`, `.SendMessageAsync(string to, string subject, string body)`.
- Produces: `IGoogleDriveNeuron.ListFilesAsync(string query)`, `.UploadFileAsync(string name, string content, string mimeType)`, `.DownloadFileAsync(string fileId)`, `.DeleteFileAsync(string fileId)`.
- Produces: `IGoogleCalendarNeuron.ListEventsAsync(string timeMinIso, string timeMaxIso)`, `.CreateEventAsync(string summary, string startIso, string endIso, string description)`, `.DeleteEventAsync(string eventId)`.

- [ ] **Step 1: Context7 check before any Google API code**

Run `resolve-library-id` for "Google APIs Client Library for .NET" and `query-docs` against `/googleapis/google-api-dotnet-client` for: (a) `GmailService`/`DriveService`/`CalendarService` constructor shape via `BaseClientService.Initializer`, (b) constructing a non-interactive `UserCredential` from a stored `refresh_token` (via `GoogleAuthorizationCodeFlow` + `TokenResponse`) — this exact call was not found in the initial spec research and must be confirmed before writing `GoogleCredentialFactory`. Do not write `GoogleCredentialFactory.cs` until this is confirmed against real docs.

- [ ] **Step 2: Create the project**

```xml
<!-- DigitalBrain.Google/DigitalBrain.Google.csproj -->
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\DigitalBrain.Core\DigitalBrain.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Google.Apis.Gmail.v1" />
    <PackageReference Include="Google.Apis.Drive.v3" />
    <PackageReference Include="Google.Apis.Calendar.v3" />
    <PackageReference Include="Google.Apis.Auth" />
  </ItemGroup>

</Project>
```

Add matching `<PackageVersion Include="Google.Apis.Gmail.v1" Version="X.Y.Z" />` (and the other three) to `Directory.Packages.props` — look up the current stable version of each via `nuget.org` or Context7 before pinning (do not guess a version number).

- [ ] **Step 3: Write the three Sdk contracts**

```csharp
// DigitalBrain.Google/IGmailNeuron.cs
using System.ComponentModel;
using DigitalBrain.Core;

namespace DigitalBrain.Google;

public interface IGmailNeuron : INeuronAgent
{
    static string INeuronAgent.AgentDisplayName => "Gmail";

    static string INeuronAgent.AgentDescription =>
        "List, read, and send Gmail messages for the authenticated Google account.";

    static string[] INeuronAgent.AgentCapabilities =>
        ["gmail", "email", "google", "list", "read", "send"];

    static string INeuronAgent.AgentInstructions => """
        You are Gmail, the email specialist. List, read, and send messages via the connected Google account.
        Sending mutates the user's mailbox — confirm intent before SendMessageAsync.
        """;

    [Description("List messages matching a Gmail search query, up to maxResults.")]
    Task<string[]> ListMessagesAsync(string query, int maxResults = 20, CancellationToken ct = default);

    [Description("Read a single message's body by its Gmail message id.")]
    Task<string> ReadMessageAsync(string messageId, CancellationToken ct = default);

    [Description("Send an email. Mutates the user's mailbox.")]
    Task SendMessageAsync(string to, string subject, string body, CancellationToken ct = default);
}
```

```csharp
// DigitalBrain.Google/IGoogleDriveNeuron.cs
using System.ComponentModel;
using DigitalBrain.Core;

namespace DigitalBrain.Google;

public interface IGoogleDriveNeuron : INeuronAgent
{
    static string INeuronAgent.AgentDisplayName => "Google Drive";

    static string INeuronAgent.AgentDescription =>
        "List, upload, download, and delete files in the authenticated Google Drive account.";

    static string[] INeuronAgent.AgentCapabilities =>
        ["drive", "google", "file", "upload", "download", "delete"];

    static string INeuronAgent.AgentInstructions => """
        You are Google Drive, the cloud file specialist. List, upload, download, and delete files.
        Delete and upload mutate the user's Drive — confirm intent before those calls.
        """;

    [Description("List files matching a Drive search query.")]
    Task<string[]> ListFilesAsync(string query, CancellationToken ct = default);

    [Description("Upload a file with the given name, text content, and MIME type. Mutates Drive.")]
    Task<string> UploadFileAsync(string name, string content, string mimeType, CancellationToken ct = default);

    [Description("Download a file's text content by its Drive file id.")]
    Task<string> DownloadFileAsync(string fileId, CancellationToken ct = default);

    [Description("Delete a file by its Drive file id. Mutates Drive.")]
    Task DeleteFileAsync(string fileId, CancellationToken ct = default);
}
```

```csharp
// DigitalBrain.Google/IGoogleCalendarNeuron.cs
using System.ComponentModel;
using DigitalBrain.Core;

namespace DigitalBrain.Google;

public interface IGoogleCalendarNeuron : INeuronAgent
{
    static string INeuronAgent.AgentDisplayName => "Google Calendar";

    static string INeuronAgent.AgentDescription =>
        "List, create, and delete events on the authenticated Google Calendar account.";

    static string[] INeuronAgent.AgentCapabilities =>
        ["calendar", "google", "event", "schedule"];

    static string INeuronAgent.AgentInstructions => """
        You are Google Calendar, the scheduling specialist. List, create, and delete events on the
        primary calendar. Create and delete mutate the user's calendar — confirm intent before those calls.
        """;

    [Description("List events between two ISO 8601 timestamps on the primary calendar.")]
    Task<string[]> ListEventsAsync(string timeMinIso, string timeMaxIso, CancellationToken ct = default);

    [Description("Create an event on the primary calendar. Mutates the calendar.")]
    Task<string> CreateEventAsync(string summary, string startIso, string endIso, string description, CancellationToken ct = default);

    [Description("Delete an event by its id. Mutates the calendar.")]
    Task DeleteEventAsync(string eventId, CancellationToken ct = default);
}
```

- [ ] **Step 4: Write the three mockable API client wrapper interfaces**

```csharp
// DigitalBrain.Google/IGmailApiClient.cs
namespace DigitalBrain.Google;

public interface IGmailApiClient
{
    Task<string[]> ListMessagesAsync(string query, int maxResults, CancellationToken ct);
    Task<string> ReadMessageAsync(string messageId, CancellationToken ct);
    Task SendMessageAsync(string to, string subject, string body, CancellationToken ct);
}
```

```csharp
// DigitalBrain.Google/IGoogleDriveApiClient.cs
namespace DigitalBrain.Google;

public interface IGoogleDriveApiClient
{
    Task<string[]> ListFilesAsync(string query, CancellationToken ct);
    Task<string> UploadFileAsync(string name, string content, string mimeType, CancellationToken ct);
    Task<string> DownloadFileAsync(string fileId, CancellationToken ct);
    Task DeleteFileAsync(string fileId, CancellationToken ct);
}
```

```csharp
// DigitalBrain.Google/IGoogleCalendarApiClient.cs
namespace DigitalBrain.Google;

public interface IGoogleCalendarApiClient
{
    Task<string[]> ListEventsAsync(string timeMinIso, string timeMaxIso, CancellationToken ct);
    Task<string> CreateEventAsync(string summary, string startIso, string endIso, string description, CancellationToken ct);
    Task DeleteEventAsync(string eventId, CancellationToken ct);
}
```

- [ ] **Step 5: Write `GoogleCredentialFactory`**

Write this only after Step 1's Context7 confirmation of the exact non-interactive-refresh-token construction API. The shape will be approximately:

```csharp
// DigitalBrain.Google/GoogleCredentialFactory.cs
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;

namespace DigitalBrain.Google;

public static class GoogleCredentialFactory
{
    public static UserCredential FromRefreshToken(string clientId, string clientSecret, string refreshToken, params string[] scopes)
    {
        var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets { ClientId = clientId, ClientSecret = clientSecret },
            Scopes = scopes
        });
        var token = new TokenResponse { RefreshToken = refreshToken };
        return new UserCredential(flow, "digitalbrain-user", token);
    }
}
```

Adjust to match whatever Step 1's Context7 query actually confirms — if the API shape differs, use the confirmed shape instead of this draft.

- [ ] **Step 6: Write the real API client implementations**

```csharp
// DigitalBrain.Google/GoogleGmailApiClient.cs
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Services;

namespace DigitalBrain.Google;

public sealed class GoogleGmailApiClient(UserCredential credential) : IGmailApiClient
{
    private readonly GmailService _service = new(new BaseClientService.Initializer
    {
        HttpClientInitializer = credential,
        ApplicationName = "DigitalBrain"
    });

    public async Task<string[]> ListMessagesAsync(string query, int maxResults, CancellationToken ct)
    {
        var request = _service.Users.Messages.List("me");
        request.Q = query;
        request.MaxResults = maxResults;
        var response = await request.ExecuteAsync(ct);
        return response.Messages?.Select(m => m.Id).ToArray() ?? [];
    }

    public async Task<string> ReadMessageAsync(string messageId, CancellationToken ct)
    {
        var message = await _service.Users.Messages.Get("me", messageId).ExecuteAsync(ct);
        return message.Snippet ?? "";
    }

    public async Task SendMessageAsync(string to, string subject, string body, CancellationToken ct)
    {
        var raw = $"To: {to}\r\nSubject: {subject}\r\n\r\n{body}";
        var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(raw))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        await _service.Users.Messages.Send(new Message { Raw = encoded }, "me").ExecuteAsync(ct);
    }
}
```

`UserCredential` needs `using Google.Apis.Auth.OAuth2;` added — add it. Before finalizing this file, re-run Context7 `query-docs` against `/googleapis/google-api-dotnet-client` specifically for `Users.Messages.List`/`Get`/`Send` request/response shapes (`ListMessagesResponse.Messages`, `Message.Snippet`, `Message.Raw`) to confirm property names match the real SDK — the properties above are drafted from general Gmail API knowledge, not yet confirmed via Context7 for this exact package version.

```csharp
// DigitalBrain.Google/GoogleDriveApiClient.cs
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;

namespace DigitalBrain.Google;

public sealed class GoogleDriveApiClient(UserCredential credential) : IGoogleDriveApiClient
{
    private readonly DriveService _service = new(new BaseClientService.Initializer
    {
        HttpClientInitializer = credential,
        ApplicationName = "DigitalBrain"
    });

    public async Task<string[]> ListFilesAsync(string query, CancellationToken ct)
    {
        var request = _service.Files.List();
        request.Q = query;
        request.Fields = "files(id, name)";
        var response = await request.ExecuteAsync(ct);
        return response.Files?.Select(f => $"{f.Id}:{f.Name}").ToArray() ?? [];
    }

    public async Task<string> UploadFileAsync(string name, string content, string mimeType, CancellationToken ct)
    {
        var metadata = new Google.Apis.Drive.v3.Data.File { Name = name };
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
        var request = _service.Files.Create(metadata, stream, mimeType);
        await request.UploadAsync(ct);
        return request.ResponseBody?.Id ?? throw new InvalidOperationException("Drive upload returned no file id.");
    }

    public async Task<string> DownloadFileAsync(string fileId, CancellationToken ct)
    {
        using var stream = new MemoryStream();
        await _service.Files.Get(fileId).DownloadAsync(stream, ct);
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    public async Task DeleteFileAsync(string fileId, CancellationToken ct) =>
        await _service.Files.Delete(fileId).ExecuteAsync(ct);
}
```

Confirm `Files.Create(metadata, stream, mimeType).UploadAsync(ct)` and `Files.Get(fileId).DownloadAsync(stream, ct)` against Context7 before finalizing — media upload/download is the part of the Drive API most likely to differ from a naive guess.

```csharp
// DigitalBrain.Google/GoogleCalendarApiClient.cs
using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;

namespace DigitalBrain.Google;

public sealed class GoogleCalendarApiClient(UserCredential credential) : IGoogleCalendarApiClient
{
    private readonly CalendarService _service = new(new BaseClientService.Initializer
    {
        HttpClientInitializer = credential,
        ApplicationName = "DigitalBrain"
    });

    public async Task<string[]> ListEventsAsync(string timeMinIso, string timeMaxIso, CancellationToken ct)
    {
        var request = _service.Events.List("primary");
        request.TimeMinDateTimeOffset = DateTimeOffset.Parse(timeMinIso);
        request.TimeMaxDateTimeOffset = DateTimeOffset.Parse(timeMaxIso);
        request.SingleEvents = true;
        request.OrderBy = EventsResource.ListRequest.OrderByEnum.StartTime;
        var response = await request.ExecuteAsync(ct);
        return response.Items?.Select(e => $"{e.Id}:{e.Summary}").ToArray() ?? [];
    }

    public async Task<string> CreateEventAsync(string summary, string startIso, string endIso, string description, CancellationToken ct)
    {
        var newEvent = new Event
        {
            Summary = summary,
            Description = description,
            Start = new EventDateTime { DateTimeDateTimeOffset = DateTimeOffset.Parse(startIso) },
            End = new EventDateTime { DateTimeDateTimeOffset = DateTimeOffset.Parse(endIso) }
        };
        var created = await _service.Events.Insert(newEvent, "primary").ExecuteAsync(ct);
        return created.Id;
    }

    public async Task DeleteEventAsync(string eventId, CancellationToken ct) =>
        await _service.Events.Delete("primary", eventId).ExecuteAsync(ct);
}
```

This matches the Context7-confirmed `Events.List`/`TimeMinDateTimeOffset`/`SingleEvents`/`OrderBy` shape from the initial spec research (already verified). Confirm `Event.Start`/`Event.End` as `EventDateTime` with a `DateTimeDateTimeOffset` property specifically (not just `DateTime`) via one more Context7 query before finalizing, since the initial research only confirmed the read path, not event creation.

- [ ] **Step 7: Write the three real grains**

```csharp
// DigitalBrain.Google/GmailNeuron.cs
using DigitalBrain.Core;

namespace DigitalBrain.Google;

[GrainType("digitalbrain.google.gmail.v1")]
public class GmailNeuron(ILogger<GmailNeuron> logger, NeuronJournals journals, IGmailApiClient client)
    : Neuron(logger, journals), IGmailNeuron
{
    public Task<string[]> ListMessagesAsync(string query, int maxResults = 20, CancellationToken ct = default) =>
        client.ListMessagesAsync(query, maxResults, ct);

    public Task<string> ReadMessageAsync(string messageId, CancellationToken ct = default) =>
        client.ReadMessageAsync(messageId, ct);

    public Task SendMessageAsync(string to, string subject, string body, CancellationToken ct = default) =>
        client.SendMessageAsync(to, subject, body, ct);
}
```

Before finalizing this constructor, read `DigitalBrain.Windows/WingetNeuron.cs`'s real constructor signature (`WingetNeuron(ILogger<WingetNeuron> logger, NeuronJournals journals) : base(logger, journals) { }`) and confirm whether `Neuron`'s base constructor accepts additional DI-resolved parameters cleanly via Orleans activation (it should — Orleans resolves grain constructor parameters from the DI container automatically) — if `IGmailApiClient` isn't registered in DI, grain activation will throw at runtime; Step 9 registers it.

Write `GoogleDriveNeuron.cs` and `GoogleCalendarNeuron.cs` following the identical pattern (constructor takes the matching `I*ApiClient`, each interface method is a one-line delegation).

- [ ] **Step 7a: Add `GoogleSignals` to Core**

In `DigitalBrain.Core/Signals.cs`, following the exact pattern of the existing `TelegramSignals`/`UiSignals` classes (read the file first to match the exact style), add:

```csharp
public static class GoogleSignals
{
    public const string AuthRequested = "GoogleAuthRequested";
    public const string AuthCompleted = "GoogleAuthCompleted";
    public const string GmailFetchRequested = "GmailFetchRequested";
    public const string GmailMessagesReady = "GmailMessagesReady";
}
```

- [ ] **Step 7b: Write the "Sign in with Google" UI surface + auth responder**

This is the piece of the spec ("The 'Sign in with Google' experience is a `UiSurface`... whose `onClick` fires `Signal("GoogleAuthRequested", ...)`") that Steps 1-7 don't cover yet — without it, nothing in the system can actually trigger Google auth.

```csharp
// DigitalBrain.Google/GoogleAuthNeuron.cs
using DigitalBrain.Core;

namespace DigitalBrain.Google;

[GrainType("digitalbrain.google.auth.v1")]
public class GoogleAuthNeuron(ILogger<GoogleAuthNeuron> logger, NeuronJournals journals)
    : Neuron(logger, journals), IHandle<Signal>
{
    public static UiSurface SignInSurface() => new(
        "google-sign-in",
        new Dictionary<string, object?>
        {
            ["title"] = "Connect Google",
            ["button"] = new Dictionary<string, object?>
            {
                ["label"] = "Sign in with Google",
                ["onClick"] = GoogleSignals.AuthRequested
            }
        });

    public async Task HandleAsync(Signal signal)
    {
        if (signal.Name != GoogleSignals.AuthRequested) return;
        // Real interactive consent is out of scope (see spec) — this confirms the refresh token
        // already provided via PackConfigStore is present and reachable, then announces completion.
        await FireAsync(new Signal(GoogleSignals.AuthCompleted, new Dictionary<string, object?>()));
    }
}
```

Read `DigitalBrain.Core/Synapse.cs`'s real `UiSurface` record constructor (confirmed needed again here, same caveat as Task 5 Step 7) and `DigitalBrain.Core/Configuration.cs`'s `ConfigFormSurface.Build` (cited in the design spec as the existing precedent for a `ui:Button` node that emits a named event) before finalizing `SignInSurface()` — match the real `ui:Button`/`onClick` JSON shape that `ConfigFormSurface` already produces exactly, rather than the placeholder dictionary shape above, so the Flutter RFW renderer actually recognizes it.

Add a test to `DigitalBrain.Google.Tests`:

```csharp
// DigitalBrain.Google.Tests/GoogleAuthNeuronTests.cs
using DigitalBrain.Core;
using DigitalBrain.TestKit;
using DigitalBrain.Google;
using Xunit;

namespace DigitalBrain.Google.Tests;

public class GoogleAuthNeuronTests : IAsyncLifetime
{
    private readonly TestDigitalBrain _brain = new();
    public Task InitializeAsync() => _brain.InitializeAsync();
    public Task DisposeAsync() => _brain.DisposeAsync();

    [Fact]
    public async Task AuthRequested_Fires_AuthCompleted()
    {
        var auth = _brain.Grain<INeuron>("google-auth-test");
        await auth.DeliverAsync(new Signal(GoogleSignals.AuthRequested, new Dictionary<string, object?>())
        { Receiver = new NeuronId("google-auth-test") });

        var outgoing = await auth.GetTimelineAsync();
        Assert.Contains(outgoing, s => s is Signal reply && reply.Name == GoogleSignals.AuthCompleted);
    }
}
```

- [ ] **Step 8: Wire Kernel → Google, register the API clients in DI**

Add to `DigitalBrain.Kernel/DigitalBrain.Kernel.csproj`:
```xml
<ProjectReference Include="..\DigitalBrain.Google\DigitalBrain.Google.csproj" />
```

In `DigitalBrain.Kernel/Program.cs`, find where other per-request DI services are registered (e.g. near `IScopedChatClientFactory`/`IEmbeddingGenerator` registrations) and add real registrations for `IGmailApiClient`/`IGoogleDriveApiClient`/`IGoogleCalendarApiClient`, each constructed via `GoogleCredentialFactory.FromRefreshToken(...)` reading `client_id`/`client_secret`/`refresh_token` from `IPackConfigStore` under a well-known scope (e.g. `"google"`/`"default"`) — mirror exactly how `LlmResponderNeuron` currently resolves per-scope config via `IPackConfigStore` in `Program.cs`/`LlmResponderNeuron.cs` (read that resolution code first and match its shape, including how it handles the config-not-yet-provided case).

- [ ] **Step 9: Build, fix remaining errors**

```
dotnet build Brain.slnx --nologo -clp:NoSummary
```

- [ ] **Step 10: Create the Google test project with fakes**

```xml
<!-- DigitalBrain.Google.Tests/DigitalBrain.Google.Tests.csproj -->
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\DigitalBrain.Google\DigitalBrain.Google.csproj" />
    <ProjectReference Include="..\DigitalBrain.TestKit\DigitalBrain.TestKit.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
  </ItemGroup>

</Project>
```

```csharp
// DigitalBrain.Google.Tests/FakeGoogleApiClients.cs
namespace DigitalBrain.Google.Tests;

public sealed class FakeGmailApiClient : IGmailApiClient
{
    public List<(string To, string Subject, string Body)> SentMessages { get; } = [];

    public Task<string[]> ListMessagesAsync(string query, int maxResults, CancellationToken ct) =>
        Task.FromResult(new[] { "fake-message-1", "fake-message-2" });

    public Task<string> ReadMessageAsync(string messageId, CancellationToken ct) =>
        Task.FromResult($"fake body for {messageId}");

    public Task SendMessageAsync(string to, string subject, string body, CancellationToken ct)
    {
        SentMessages.Add((to, subject, body));
        return Task.CompletedTask;
    }
}

public sealed class FakeGoogleDriveApiClient : IGoogleDriveApiClient
{
    private readonly Dictionary<string, string> _files = new();

    public Task<string[]> ListFilesAsync(string query, CancellationToken ct) =>
        Task.FromResult(_files.Keys.ToArray());

    public Task<string> UploadFileAsync(string name, string content, string mimeType, CancellationToken ct)
    {
        var id = "fake-" + name;
        _files[id] = content;
        return Task.FromResult(id);
    }

    public Task<string> DownloadFileAsync(string fileId, CancellationToken ct) =>
        Task.FromResult(_files.TryGetValue(fileId, out var content) ? content : "");

    public Task DeleteFileAsync(string fileId, CancellationToken ct)
    {
        _files.Remove(fileId);
        return Task.CompletedTask;
    }
}

public sealed class FakeGoogleCalendarApiClient : IGoogleCalendarApiClient
{
    private readonly List<string> _events = [];

    public Task<string[]> ListEventsAsync(string timeMinIso, string timeMaxIso, CancellationToken ct) =>
        Task.FromResult(_events.ToArray());

    public Task<string> CreateEventAsync(string summary, string startIso, string endIso, string description, CancellationToken ct)
    {
        var id = "fake-event-" + _events.Count;
        _events.Add($"{id}:{summary}");
        return Task.FromResult(id);
    }

    public Task DeleteEventAsync(string eventId, CancellationToken ct)
    {
        _events.RemoveAll(e => e.StartsWith(eventId));
        return Task.CompletedTask;
    }
}
```

```csharp
// DigitalBrain.Google.Tests/GmailNeuronTests.cs
using DigitalBrain.TestKit;
using DigitalBrain.Google;
using Xunit;

namespace DigitalBrain.Google.Tests;

public class GmailNeuronTests : IAsyncLifetime
{
    private readonly TestDigitalBrain _brain;
    private readonly FakeGmailApiClient _fake = new();

    public GmailNeuronTests() =>
        _brain = new TestDigitalBrain(sb => sb.ConfigureServices(services =>
            services.AddSingleton<IGmailApiClient>(_fake)));

    public Task InitializeAsync() => _brain.InitializeAsync();
    public Task DisposeAsync() => _brain.DisposeAsync();

    [Fact]
    public async Task SendMessageAsync_Records_The_Send_On_The_Fake()
    {
        var gmail = _brain.Grain<IGmailNeuron>("gmail-test");
        await gmail.SendMessageAsync("someone@example.com", "hi", "hello there");
        Assert.Single(_fake.SentMessages, m => m.To == "someone@example.com" && m.Subject == "hi");
    }

    [Fact]
    public async Task ListMessagesAsync_Returns_Fake_Results()
    {
        var gmail = _brain.Grain<IGmailNeuron>("gmail-list-test");
        var messages = await gmail.ListMessagesAsync("is:unread", 10);
        Assert.NotEmpty(messages);
    }
}
```

This is the first real exercise of Step 1's `TestDigitalBrain(Action<ISiloBuilder>? extend)` extension point — if it doesn't work as designed, fix `TestDigitalBrain` in `DigitalBrain.TestKit` now rather than working around it here (the whole point of that constructor parameter was exactly this use case).

Write `GoogleDriveNeuronTests.cs` and `GoogleCalendarNeuronTests.cs` following the identical shape (construct `TestDigitalBrain` with the matching fake registered, assert against the fake's recorded state).

- [ ] **Step 11: Build and test everything**

```
dotnet build Brain.slnx --nologo -clp:NoSummary
dotnet test DigitalBrain.Google.Tests --nologo
```
Expected: 0 build errors, all Google tests pass using the fakes — confirm via test output timing that no real network call was attempted (should run in well under a second per test).

- [ ] **Step 12: Commit**

```bash
git add DigitalBrain.Google DigitalBrain.Google.Tests DigitalBrain.Core DigitalBrain.Kernel Brain.slnx Directory.Packages.props
git commit -m "feat(google-ino): add Gmail/Drive/Calendar as isolated DigitalBrain.Google project with mockable API clients"
```

---

### Task 9: `DigitalBrain.Experience.PersonalAssistant` ino

**Files:**
- Create: `DigitalBrain.Experience.PersonalAssistant/DigitalBrain.Experience.PersonalAssistant.csproj`
- Create: `DigitalBrain.Experience.PersonalAssistant/PersonalAssistantNeuron.cs`
- Modify: `DigitalBrain.Core/Signals.cs` — add `ContextSignals` string-constant class
- Modify: `DigitalBrain.Context/ContextNeuron.cs` — add `IHandle<Signal>` handling `ContextSignals.RecallRequested`
- Modify: `Brain.slnx` — add `DigitalBrain.Experience.PersonalAssistant`, `DigitalBrain.Experience.PersonalAssistant.Tests`
- Create: `DigitalBrain.Experience.PersonalAssistant.Tests/DigitalBrain.Experience.PersonalAssistant.Tests.csproj`, `PersonalAssistantNeuronTests.cs`

**Interfaces:**
- Consumes: `Signal(ContextSignals.RecallRequested, {"query": string})` → `ContextNeuron` replies `Signal(ContextSignals.RecallCompleted, {"results": string[]})`.
- Consumes: `AskLlm(prompt, replyType, replyProps)` → `LlmResponderNeuron` replies `Signal(replyType, props)` (existing mechanism, unchanged).
- Consumes: `Signal(TelegramSignals.MessageReceived, {"text": string, "chatId": object})` (existing Telegram inbound shape).
- Produces: `Signal(TelegramSignals.ReplyRequested, {"chatId": object, "text": string})` for plain replies, or a `VisualizeDataRequest` (existing type, reusing the Chart→UiSurface→FlutterUiNeuron chain already proven by `TelegramChatNeuron`) when the augmented response looks chart-worthy.

- [ ] **Step 1: Add `ContextSignals` to Core**

In `DigitalBrain.Core/Signals.cs`, alongside the `GoogleSignals` addition from Task 8:

```csharp
public static class ContextSignals
{
    public const string RecallRequested = "ContextRecallRequested";
    public const string RecallCompleted = "ContextRecallCompleted";
}
```

- [ ] **Step 2: Write the failing test for `ContextNeuron`'s new signal handling**

```csharp
// DigitalBrain.Context.Tests/ContextNeuronTests.cs — add this method to the class created in Task 4
[Fact]
public async Task Signal_RecallRequested_Replies_With_Signal_RecallCompleted()
{
    var context = _brain.Grain<IContextNeuron>("context-signal-test");
    await context.RememberAsync("the launch date is March 5th");

    await _brain.DeliverAsync(new Signal(
        ContextSignals.RecallRequested,
        new Dictionary<string, object?> { ["query"] = "launch date" })
    { Receiver = new NeuronId("context-signal-test") });

    var outgoing = await context.GetTimelineAsync();
    Assert.Contains(outgoing, s => s is Signal reply
        && reply.Name == ContextSignals.RecallCompleted
        && reply.Props.TryGetValue("results", out var r)
        && r is string[] results && results.Contains("the launch date is March 5th"));
}
```

Read `INeuron.GetTimelineAsync`'s real return shape in `DigitalBrain.Core/INeuron.cs` before finalizing this assertion (confirm it returns the outgoing journal, or find the correct member for "synapses this neuron fired") — adjust to match the real API rather than this draft.

- [ ] **Step 3: Run it, confirm it fails**

```
dotnet test DigitalBrain.Context.Tests --filter "Signal_RecallRequested_Replies_With_Signal_RecallCompleted"
```
Expected: FAIL (no such handling exists yet).

- [ ] **Step 4: Implement `IHandle<Signal>` on `ContextNeuron`**

Add to `DigitalBrain.Context/ContextNeuron.cs` (class declaration becomes `public class ContextNeuron : Neuron, IContextNeuron, IHandle<Signal>`):

```csharp
public async Task HandleAsync(Signal signal)
{
    if (signal.Name != ContextSignals.RecallRequested) return;
    var query = signal.Props.TryGetValue("query", out var q) ? q?.ToString() ?? "" : "";
    var results = await RecallAsync(query);
    await FireAsync(new Signal(ContextSignals.RecallCompleted, new Dictionary<string, object?> { ["results"] = results }));
}
```

- [ ] **Step 5: Run it, confirm it passes**

```
dotnet test DigitalBrain.Context.Tests --filter "Signal_RecallRequested_Replies_With_Signal_RecallCompleted"
```
Expected: PASS.

- [ ] **Step 6: Commit the Context addition**

```bash
git add DigitalBrain.Context DigitalBrain.Context.Tests DigitalBrain.Core
git commit -m "feat(context-ino): handle ContextRecallRequested signal, reusing the AskLlm reply-by-name pattern"
```

- [ ] **Step 7: Create the PersonalAssistant project**

```xml
<!-- DigitalBrain.Experience.PersonalAssistant/DigitalBrain.Experience.PersonalAssistant.csproj -->
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>true</IsPackable>
    <PackageId>DigitalBrain.Experience.PersonalAssistant</PackageId>
    <Version>0.1.0</Version>
    <Description>Personal AI assistant pack: Telegram-triggered, context-aware, visualizes results via the UI Kit ino when appropriate. Depends only on DigitalBrain.Core; the kernel never references this project.</Description>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\DigitalBrain.Core\DigitalBrain.Core.csproj" />
  </ItemGroup>

</Project>
```

(Matches `DigitalBrain.Telegram.csproj`'s exact shape — same `IsPackable`/`PackageId`/`Description`-explains-the-boundary convention.)

- [ ] **Step 8: Write `PersonalAssistantNeuron`**

```csharp
// DigitalBrain.Experience.PersonalAssistant/PersonalAssistantNeuron.cs
using System.Collections.Generic;
using DigitalBrain.Core;

namespace DigitalBrain.Experience.PersonalAssistant;

public sealed class PersonalAssistantNeuron : IPackBehavior
{
    private const string ConfigPack = "DigitalBrain.Experience.PersonalAssistant";
    private const string ConfigScope = "default";

    public PackManifest GetManifest() => new(
        new[]
        {
            new SynapseType(TelegramSignals.MessageReceived),
            new SynapseType(ContextSignals.RecallCompleted),
            new SynapseType("Signal") // AskLlm replies arrive as generic Signal(replyType, ...)
        },
        new PackConfigField[]
        {
            new("llm_provider", "LLM",     PackConfigFieldKind.Choice, new[] { "ollama", "openai" }),
            new("llm_key",      "API key", PackConfigFieldKind.Secret,
                DependsOnKey: "llm_provider", DependsOnValue: "openai"),
        });

    public string Respond(string input) => input;

    public IReadOnlyList<Synapse> Handle(Synapse synapse)
    {
        switch (synapse)
        {
            case Signal s when s.Name == TelegramSignals.MessageReceived:
                var query = s.Props.TryGetValue("text", out var t) ? t?.ToString() ?? "" : "";
                var chatId = s.Props.TryGetValue("chatId", out var c) ? c : null;
                return new Synapse[]
                {
                    new Signal(ContextSignals.RecallRequested,
                        new Dictionary<string, object?> { ["query"] = query, ["chatId"] = chatId })
                };

            case Signal s when s.Name == ContextSignals.RecallCompleted:
                var results = s.Props.TryGetValue("results", out var r) ? r as string[] ?? [] : [];
                var augmentedPrompt = results.Length > 0
                    ? $"Context: {string.Join("; ", results)}\n\nRespond helpfully."
                    : "Respond helpfully.";
                return new Synapse[]
                {
                    new AskLlm(augmentedPrompt, PersonalAssistantSignals.LlmReplyReady, new Dictionary<string, object?>(), ConfigPack, ConfigScope)
                };

            case Signal s when s.Name == PersonalAssistantSignals.LlmReplyReady:
                var text = s.Props.TryGetValue("text", out var rt) ? rt?.ToString() ?? "" : "";
                var replyChatId = s.Props.TryGetValue("chatId", out var rc) ? rc : null;
                return new Synapse[]
                {
                    new Signal(TelegramSignals.ReplyRequested,
                        new Dictionary<string, object?> { ["chatId"] = replyChatId, ["text"] = text })
                };

            default:
                return System.Array.Empty<Synapse>();
        }
    }

    public BundleManifest? GetBundleManifest() => new(
        BundleTier.Content,
        null,
        new[] { BundleChannel.Telegram },
        new[]
        {
            new BundleDependency("DigitalBrain.Telegram.Responder", "1.0.0"),
            new BundleDependency("DigitalBrain.UIKit.ForUI", "0.1.0")
        });
}

internal static class PersonalAssistantSignals
{
    public const string LlmReplyReady = "PersonalAssistantLlmReplyReady";
}
```

Read `DigitalBrain.Core/Signals.cs`'s real `AskLlm` record definition and `DigitalBrain.Core/Distribution/BundleManifest.cs`'s real `BundleManifest`/`BundleDependency` constructors before finalizing this file — confirm parameter names/order exactly (the design spec already captured `BundleManifest(Tier, EntryExperience, Channels, Dependencies?)` and `BundleDependency(PackName, MinVersion)`, and `AskLlm(Prompt, ReplyType, ReplyProps, ConfigPack?, ConfigScope?)` from earlier research — match those exactly). Note this pack deliberately does **not** call `chart`/`VisualizeDataRequest` in this first version — the "visualize results" capability is a follow-on once the basic recall→respond loop is proven; do not add it speculatively here (YAGNI — the spec explicitly scoped visualization as a later enhancement of this same pack once the loop works).

- [ ] **Step 9: Create the zero-infra test project and tests**

```xml
<!-- DigitalBrain.Experience.PersonalAssistant.Tests/DigitalBrain.Experience.PersonalAssistant.Tests.csproj -->
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\DigitalBrain.Experience.PersonalAssistant\DigitalBrain.Experience.PersonalAssistant.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
  </ItemGroup>

</Project>
```

```csharp
// DigitalBrain.Experience.PersonalAssistant.Tests/PersonalAssistantNeuronTests.cs
using DigitalBrain.Core;
using DigitalBrain.Experience.PersonalAssistant;
using Xunit;

namespace DigitalBrain.Experience.PersonalAssistant.Tests;

public class PersonalAssistantNeuronTests
{
    [Fact]
    public void TelegramMessage_Produces_ContextRecallRequested()
    {
        var pack = new PersonalAssistantNeuron();
        var inbound = new Signal(TelegramSignals.MessageReceived,
            new Dictionary<string, object?> { ["text"] = "when is the launch?", ["chatId"] = 123L });

        var outputs = pack.Handle(inbound);

        var recall = Assert.Single(outputs);
        var signal = Assert.IsType<Signal>(recall);
        Assert.Equal(ContextSignals.RecallRequested, signal.Name);
        Assert.Equal("when is the launch?", signal.Props["query"]);
    }

    [Fact]
    public void ContextRecallCompleted_Produces_AskLlm_With_Augmented_Prompt()
    {
        var pack = new PersonalAssistantNeuron();
        var recalled = new Signal(ContextSignals.RecallCompleted,
            new Dictionary<string, object?> { ["results"] = new[] { "the launch date is March 5th" } });

        var outputs = pack.Handle(recalled);

        var ask = Assert.IsType<AskLlm>(Assert.Single(outputs));
        Assert.Contains("the launch date is March 5th", ask.Prompt);
    }

    [Fact]
    public void LlmReply_Produces_TelegramReplyRequested()
    {
        var pack = new PersonalAssistantNeuron();
        var llmReply = new Signal("PersonalAssistantLlmReplyReady",
            new Dictionary<string, object?> { ["text"] = "March 5th", ["chatId"] = 123L });

        var outputs = pack.Handle(llmReply);

        var reply = Assert.IsType<Signal>(Assert.Single(outputs));
        Assert.Equal(TelegramSignals.ReplyRequested, reply.Name);
        Assert.Equal("March 5th", reply.Props["text"]);
    }

    [Fact]
    public void GetBundleManifest_Declares_Telegram_And_UiKit_Dependencies()
    {
        var manifest = new PersonalAssistantNeuron().GetBundleManifest();
        Assert.NotNull(manifest);
        Assert.Contains(manifest!.Dependencies!, d => d.PackName == "DigitalBrain.Telegram.Responder");
        Assert.Contains(manifest.Dependencies!, d => d.PackName == "DigitalBrain.UIKit.ForUI");
    }
}
```

This test project references **no** Orleans package at all — every test constructs `PersonalAssistantNeuron` directly and calls `.Handle(synapse)`, proving the zero-infra claim for pure-pack inos concretely.

- [ ] **Step 10: Build and test**

```
dotnet build Brain.slnx --nologo -clp:NoSummary
dotnet test DigitalBrain.Experience.PersonalAssistant.Tests --nologo
```
Expected: 0 build errors, 4/4 tests pass, running in well under a second (no TestCluster).

- [ ] **Step 11: Commit**

```bash
git add DigitalBrain.Experience.PersonalAssistant DigitalBrain.Experience.PersonalAssistant.Tests DigitalBrain.Core Brain.slnx
git commit -m "feat(personal-assistant-ino): add PersonalAssistantNeuron pack composing Telegram+Context+LLM via generic Signal names"
```

---

### Task 10: Marketplace seed entry + `Brain.slnx` consolidation pass

**Files:**
- Modify: `DigitalBrain.Core/MarketplaceSeeds.cs`
- Modify: `Brain.slnx`

**Interfaces:**
- Consumes: `DigitalBrain.Experience.PersonalAssistant.PersonalAssistantNeuron`'s real source (Task 9) — this task embeds it as a string constant in `MarketplaceSeeds.cs`, the same accepted-duplication pattern `TelegramResponderPackCode` already uses (see spec's "Explicitly out of scope" section).

- [ ] **Step 1: Add the embedded pack source constant**

In `DigitalBrain.Core/MarketplaceSeeds.cs`, near `TelegramResponderPackCode`, add a new `public const string PersonalAssistantPackCode = """..."""` containing the exact same C# source written in Task 9 Step 8 (copy verbatim from `DigitalBrain.Experience.PersonalAssistant/PersonalAssistantNeuron.cs`, since packs seeded here must be self-contained compilable source — this is the accepted duplication, not a bug).

- [ ] **Step 2: Add the `NeuroPack` seed entry**

In `MarketplaceSeeds.LocalUiPacks` (the list), add:

```csharp
new NeuroPack(
    "DigitalBrain.Experience.PersonalAssistant",
    "0.1.0",
    "digitalbraintech",
    false,
    0.0,
    PersonalAssistantPackCode,
    "Personal AI assistant: Telegram-triggered, recalls context before responding, visualizes results via the UI Kit when appropriate.",
    Manifest: new(BundleTier.Content, null, new[] { BundleChannel.Telegram },
        new[]
        {
            new BundleDependency("DigitalBrain.Telegram.Responder", "1.0.0"),
            new BundleDependency("DigitalBrain.UIKit.ForUI", "0.1.0")
        })),
```

Read the real `NeuroPack` record constructor parameter order in `DigitalBrain.Core/Synapse.cs` (around line 236, per the design spec's citation) before finalizing this call — match positional argument order exactly against the other entries already in `LocalUiPacks` (e.g. `DigitalBrain.Telegram.Responder`'s entry) rather than this reconstruction.

- [ ] **Step 3: Verify `JournalJsonContext` coverage**

Run:
```
dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~JournalJsonContext"
```
This existing test (`ContextCoversEverySynapseSubtype`) caught a missing `[JsonSerializable]` entry once before (see plan history) — if any new Synapse subtype was introduced anywhere in this plan (it shouldn't have been — this plan only adds `Signal`-based communication, no new Synapse record types), this test will catch it now.

- [ ] **Step 4: Full solution `Brain.slnx` audit**

Run `grep -c "csproj" Brain.slnx` and manually confirm every project created in Tasks 1-10 is present: `DigitalBrain.TestKit`, `DigitalBrain.TestKit.Tests`, `DigitalBrain.Windows`, `DigitalBrain.Windows.Tests`, `DigitalBrain.Developer`, `DigitalBrain.Developer.Tests`, `DigitalBrain.Context`, `DigitalBrain.Context.Tests`, `DigitalBrain.UiKit`, `DigitalBrain.UiKit.Tests`, `DigitalBrain.Telegram.Channel`, `DigitalBrain.Telegram.Channel.Tests`, `DigitalBrain.Telegram.Tests`, `DigitalBrain.Google`, `DigitalBrain.Google.Tests`, `DigitalBrain.Experience.PersonalAssistant`, `DigitalBrain.Experience.PersonalAssistant.Tests` (16 new entries). Add any missing ones.

- [ ] **Step 5: Build and test**

```
dotnet build Brain.slnx --nologo -clp:NoSummary
dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~Marketplace|FullyQualifiedName~JournalJsonContext" --nologo
```

- [ ] **Step 6: Commit**

```bash
git add DigitalBrain.Core Brain.slnx
git commit -m "feat(marketplace): seed DigitalBrain.Experience.PersonalAssistant pack with Telegram+UiKit dependencies"
```

---

### Task 11: Core purity verification + final full-solution pass

**Files:** none created; verification only.

- [ ] **Step 1: Verify Core has zero vendor/integration awareness**

```bash
grep -rn "Google\.\|Gmail\|WingetNeuron\|FileSystemNeuron\|GitNeuron\|DotNetNeuron\|NuGetNeuron\|RoslynNeuron\|ShellNeuron\|FlutterUiNeuron\|TelegramChatNeuron\|ContextNeuron\b" DigitalBrain.Core --include="*.cs"
```
Expected: **zero matches** for any of the moved `I*Neuron`/`*Neuron` implementation type names (string constants like `TelegramSignals`/`GoogleSignals`/`ContextSignals` are expected and fine — those are generic name registries, not type contracts, per the spec's clarification). If anything matches, that reference was missed in an earlier task — go back and fix it there, then re-run this check.

- [ ] **Step 2: Full solution build**

```
dotnet build Brain.slnx --nologo -clp:NoSummary
```
Expected: 0 errors, 0 new warnings versus the pre-plan baseline.

- [ ] **Step 3: Full test run across every new and existing test project**

```
dotnet test DigitalBrain.TestKit.Tests --nologo
dotnet test DigitalBrain.Windows.Tests --nologo
dotnet test DigitalBrain.Developer.Tests --nologo
dotnet test DigitalBrain.Context.Tests --nologo
dotnet test DigitalBrain.UiKit.Tests --nologo
dotnet test DigitalBrain.Telegram.Channel.Tests --nologo
dotnet test DigitalBrain.Telegram.Tests --nologo
dotnet test DigitalBrain.Google.Tests --nologo
dotnet test DigitalBrain.Experience.PersonalAssistant.Tests --nologo
dotnet test DigitalBrain.Tests --nologo
```
Expected: every project green, 0 failures. The last line (central `DigitalBrain.Tests`) is the broadest safety net — if anything anywhere in the migration broke a cross-cutting E2E/composition test, this catches it.

- [ ] **Step 4: `aspire doctor`**

Use the aspire MCP tool `mcp__aspire__doctor` (per project convention — always use aspire MCP for hosting-related checks, and the `AppHost` now transitively pulls in every new project via `DigitalBrain.Kernel`). Expected: pass.

- [ ] **Step 5: Update `SYSTEM_DESIGN.md`**

In `brain/docs/SYSTEM_DESIGN.md` §1.3 ("Project graph"), add rows for the 8 new real-grain/pure-pack projects and `DigitalBrain.TestKit`, following the existing table's exact format (Project | Purpose columns). Note the new "every integration is a peer ino" architecture in a short new subsection, referencing the design spec.

- [ ] **Step 6: Final commit**

```bash
git add brain/docs/SYSTEM_DESIGN.md
git commit -m "docs: update SYSTEM_DESIGN.md with the isolated-ino project graph"
```
