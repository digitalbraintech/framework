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

> **AMENDED 2026-07-01:** revised per `docs/superpowers/specs/2026-07-01-real-grain-ino-neuron-placement-amendment.md`.
> Original Task 2 (below, superseded) tried to move the concrete grain classes into the ino; they can't
> compile there because `Neuron` lives in `DigitalBrain.Kernel`. The interfaces + `ProcessRunner` (already
> Orleans-free) still move to the ino. `FileSystemNeuron.cs`/`WingetNeuron.cs`/`ShellNeuron.cs` **stay in
> `DigitalBrain.Kernel/Sdk/`** — `WingetNeuron`/`ShellNeuron` are already trivial `ProcessRunner` one-liners
> with nothing worth extracting; `FileSystemNeuron`'s real file-I/O body is extracted into a new plain class,
> `FileSystemOperations`, in the ino, which the Kernel-side grain delegates to.

**Files:**
- Create: `DigitalBrain.Windows/DigitalBrain.Windows.csproj`
- Move: `DigitalBrain.Core/Sdk/IFileSystemNeuron.cs`, `IWingetNeuron.cs`, `IShellNeuron.cs` → `DigitalBrain.Windows/` (namespace `DigitalBrain.Core` → `DigitalBrain.Windows`)
- Move: `DigitalBrain.Kernel/Sdk/ProcessRunner.cs` → `DigitalBrain.Windows/ProcessRunner.cs` (namespace `DigitalBrain.Kernel` → `DigitalBrain.Windows`; content otherwise unchanged — it's already a pure static class with no `Neuron`/Orleans dependency)
- Create: `DigitalBrain.Windows/FileSystemOperations.cs` (new plain class — the real `System.IO` logic extracted verbatim from `FileSystemNeuron.cs`'s current body)
- Modify: `DigitalBrain.Kernel/Sdk/FileSystemNeuron.cs` — **stays in Kernel** (does not move); add `using DigitalBrain.Windows;`; body changes from direct `System.IO` calls to delegating to an injected `FileSystemOperations`
- Modify: `DigitalBrain.Kernel/Sdk/WingetNeuron.cs`, `ShellNeuron.cs` — **stay in Kernel** (do not move); add `using DigitalBrain.Windows;` (for `ProcessRunner`/interfaces); no other change — their bodies are already literal `ProcessRunner` one-liners with nothing to extract
- Modify: `DigitalBrain.Kernel/Sandbox/OutOfProcessSandbox.cs` — add `using DigitalBrain.Windows;` (line 1 area)
- Modify: `DigitalBrain.Kernel/DigitalBrain.Kernel.csproj` — add `ProjectReference` to `DigitalBrain.Windows`
- Modify: `Brain.slnx` — add `DigitalBrain.Windows/DigitalBrain.Windows.csproj`, `DigitalBrain.Windows.Tests/DigitalBrain.Windows.Tests.csproj`
- Create: `DigitalBrain.Windows.Tests/DigitalBrain.Windows.Tests.csproj`
- Create: `DigitalBrain.Windows.Tests/FileSystemNeuronTests.cs`, `WingetNeuronTests.cs`, `ShellNeuronTests.cs`, `FileSystemOperationsTests.cs`
- Modify: `DigitalBrain.Tests/Sdk/SdkNeuronsTests.cs` — delete `Shell_Executes_Echo`, `Shell_Blocks_Dangerous_Command`, `FileSystem_Write_Read_List_Delete_RoundTrip` (moved out; keep `DotNet_Reports_Sdk_Version` and `Git_Status_Works_After_ProcessRunner_Refactor` for now — those move in Task 3)
- Modify: `DigitalBrain.Tests/Sdk/SdkContractsMetadataTests.cs` — add `using DigitalBrain.Windows;` for `IWingetNeuron`/`IFileSystemNeuron`/`IShellNeuron` (types moved, test stays central since it spans all Sdk contracts)

**Interfaces:**
- Consumes: nothing new (existing `IFileSystemNeuron`/`IWingetNeuron`/`IShellNeuron`/`ProcessRunner` signatures are unchanged, only namespace moves).
- Produces: `DigitalBrain.Windows.ProcessRunner.RunAsync/ShellAsync/PowerShellAsync` (unchanged signatures) — Task 3's Developer ino references this indirectly through Kernel (see amendment: `GitNeuron`/`DotNetNeuron`/`NuGetNeuron` stay in Kernel too and already reference `DigitalBrain.Windows` directly, no change needed there).
- Produces: `DigitalBrain.Windows.FileSystemOperations` — plain class with the same 8 methods as `IFileSystemNeuron` (`ReadFileAsync`, `WriteFileAsync`, `ListFilesAsync`, `ExistsAsync`, `CopyAsync`, `MoveAsync`, `DeleteAsync`, `GetInfoAsync`), each identical in behavior to `FileSystemNeuron`'s current body. `DigitalBrain.Kernel.FileSystemNeuron` constructs one directly (`new FileSystemOperations()`) or takes it via constructor DI — no external state, so either works; use constructor DI to match the Google ino's established pattern (Task 8) and keep the grain trivially testable in isolation later if needed.

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

- [ ] **Step 3: Move `ProcessRunner`, extract `FileSystemOperations`**

Move `DigitalBrain.Kernel/Sdk/ProcessRunner.cs` to `DigitalBrain.Windows/ProcessRunner.cs`, change `namespace DigitalBrain.Kernel;` to `namespace DigitalBrain.Windows;`. Content otherwise unchanged — it's already a pure static class (no `Neuron`/Orleans reference).

Create `DigitalBrain.Windows/FileSystemOperations.cs` — the real file-I/O logic, extracted verbatim from `FileSystemNeuron.cs`'s current body (read the real file first to confirm nothing has drifted from this plan's snapshot):

```csharp
// DigitalBrain.Windows/FileSystemOperations.cs
namespace DigitalBrain.Windows;

public sealed class FileSystemOperations
{
    private const int MaxReadChars = 50 * 1024;

    public async Task<string> ReadFileAsync(string path, CancellationToken ct = default)
    {
        var text = await File.ReadAllTextAsync(path, ct);
        return text.Length > MaxReadChars ? text[..MaxReadChars] + "\n... [truncated]" : text;
    }

    public async Task WriteFileAsync(string path, string content, CancellationToken ct = default)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(path, content, ct);
    }

    public Task<string[]> ListFilesAsync(string directory, string pattern = "*", CancellationToken ct = default)
        => Task.FromResult(Directory.GetFiles(directory, pattern, SearchOption.TopDirectoryOnly));

    public Task<bool> ExistsAsync(string path, CancellationToken ct = default)
        => Task.FromResult(File.Exists(path) || Directory.Exists(path));

    public Task<string> CopyAsync(string source, string destination, CancellationToken ct = default)
    {
        var directory = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.Copy(source, destination, overwrite: true);
        return Task.FromResult($"Copied '{source}' -> '{destination}'.");
    }

    public Task<string> MoveAsync(string source, string destination, CancellationToken ct = default)
    {
        File.Move(source, destination, overwrite: true);
        return Task.FromResult($"Moved '{source}' -> '{destination}'.");
    }

    public Task<string> DeleteAsync(string path, CancellationToken ct = default)
    {
        if (Directory.Exists(path))
            return Task.FromResult($"Refused: '{path}' is a directory.");
        File.Delete(path);
        return Task.FromResult($"Deleted '{path}'.");
    }

    public Task<string> GetInfoAsync(string path, CancellationToken ct = default)
    {
        if (File.Exists(path))
        {
            var info = new FileInfo(path);
            return Task.FromResult($"File '{path}': {info.Length} bytes, modified {info.LastWriteTimeUtc:u}.");
        }
        if (Directory.Exists(path))
        {
            var info = new DirectoryInfo(path);
            return Task.FromResult($"Directory '{path}': created {info.CreationTimeUtc:u}.");
        }
        return Task.FromResult($"Path '{path}' does not exist.");
    }
}
```

`DigitalBrain.Kernel/Sdk/` is **not** deleted — `FileSystemNeuron.cs`, `WingetNeuron.cs`, `ShellNeuron.cs` stay there (and `GitNeuron.cs`/`DotNetNeuron.cs`/`NuGetNeuron.cs`/`RoslynNeuron.cs` stay there too per Task 3's amendment).

- [ ] **Step 4: Update the 3 grain classes left in Kernel**

`DigitalBrain.Kernel/Sdk/FileSystemNeuron.cs` — add `using DigitalBrain.Windows;`, change the class to hold and delegate to a `FileSystemOperations`:

```csharp
// DigitalBrain.Kernel/Sdk/FileSystemNeuron.cs
using DigitalBrain.Core;
using DigitalBrain.Windows;

namespace DigitalBrain.Kernel;

[GrainType("digitalbrain.sdk.filesystem.v1")]
public class FileSystemNeuron(ILogger<FileSystemNeuron> logger, NeuronJournals journals, FileSystemOperations ops)
    : Neuron(logger, journals), IFileSystemNeuron
{
    public Task<string> ReadFileAsync(string path, CancellationToken ct = default) => ops.ReadFileAsync(path, ct);
    public Task WriteFileAsync(string path, string content, CancellationToken ct = default) => ops.WriteFileAsync(path, content, ct);
    public Task<string[]> ListFilesAsync(string directory, string pattern = "*", CancellationToken ct = default) => ops.ListFilesAsync(directory, pattern, ct);
    public Task<bool> ExistsAsync(string path, CancellationToken ct = default) => ops.ExistsAsync(path, ct);
    public Task<string> CopyAsync(string source, string destination, CancellationToken ct = default) => ops.CopyAsync(source, destination, ct);
    public Task<string> MoveAsync(string source, string destination, CancellationToken ct = default) => ops.MoveAsync(source, destination, ct);
    public Task<string> DeleteAsync(string path, CancellationToken ct = default) => ops.DeleteAsync(path, ct);
    public Task<string> GetInfoAsync(string path, CancellationToken ct = default) => ops.GetInfoAsync(path, ct);
}
```

`FileSystemOperations` has no dependencies of its own, so Orleans DI resolves `new FileSystemOperations()` automatically as long as it's registered — add `services.AddSingleton<FileSystemOperations>();` in `DigitalBrain.Kernel/Program.cs` near the other SDK neuron/service registrations (read that file first to match the existing registration style).

`DigitalBrain.Kernel/Sdk/WingetNeuron.cs` and `ShellNeuron.cs` — add `using DigitalBrain.Windows;` only. No other change; their bodies are already pure `ProcessRunner` one-liners with nothing to extract:

```csharp
// DigitalBrain.Kernel/Sdk/WingetNeuron.cs — only the using changes
using DigitalBrain.Core;
using DigitalBrain.Windows;

namespace DigitalBrain.Kernel;
// ... rest of the class body unchanged (still `: Neuron, IWingetNeuron`, still calls ProcessRunner.RunAsync directly)
```

- [ ] **Step 5: Fix the one cross-reference**

In `DigitalBrain.Kernel/Sandbox/OutOfProcessSandbox.cs`, add `using DigitalBrain.Windows;` alongside the existing `using DigitalBrain.Kernel.Foundry;` (line 1).

- [ ] **Step 6: Wire Kernel → Windows**

Add to `DigitalBrain.Kernel/DigitalBrain.Kernel.csproj`:
```xml
<ProjectReference Include="..\DigitalBrain.Windows\DigitalBrain.Windows.csproj" />
```

- [ ] **Step 6a: Build Kernel, fix any remaining errors**

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

Also add a zero-infra unit test directly against the extracted `FileSystemOperations` — no Orleans/TestKit needed, since it's now a plain class (this is the concrete isolation win the amendment describes):

```csharp
// DigitalBrain.Windows.Tests/FileSystemOperationsTests.cs
using DigitalBrain.Windows;
using Xunit;

namespace DigitalBrain.Windows.Tests;

public class FileSystemOperationsTests
{
    [Fact]
    public async Task Write_Read_List_Delete_RoundTrip()
    {
        var fs = new FileSystemOperations();
        var dir = Path.Combine(Path.GetTempPath(), "dbfsops-" + Guid.NewGuid().ToString("N"));
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

- [ ] **Step 9: Delete the moved tests from central `DigitalBrain.Tests`**

In `DigitalBrain.Tests/Sdk/SdkNeuronsTests.cs`, delete the `Shell_Executes_Echo`, `Shell_Blocks_Dangerous_Command`, and `FileSystem_Write_Read_List_Delete_RoundTrip` methods (now duplicated in `DigitalBrain.Windows.Tests`). Leave `DotNet_Reports_Sdk_Version` and `Git_Status_Works_After_ProcessRunner_Refactor` in place — they move in Task 3.

In `DigitalBrain.Tests/Sdk/SdkContractsMetadataTests.cs`, add `using DigitalBrain.Windows;` at the top (for `IWingetNeuron`/`IFileSystemNeuron`/`IShellNeuron` references — the file itself stays central since it spans all Sdk agents across multiple projects).

- [ ] **Step 10: Build and test everything**

```
dotnet build Brain.slnx --nologo -clp:NoSummary
dotnet test DigitalBrain.Windows.Tests --nologo
dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~Sdk" --nologo
```
Expected: 0 build errors, `DigitalBrain.Windows.Tests` 6/6 passed (5 grain-level TestKit tests + the new zero-infra `FileSystemOperationsTests`), `DigitalBrain.Tests` Sdk filter passes with the 3 moved tests gone and the remaining 2 (DotNet/Git) + metadata test still green.

- [ ] **Step 11: Commit**

```bash
git add DigitalBrain.Windows DigitalBrain.Windows.Tests DigitalBrain.Kernel DigitalBrain.Core DigitalBrain.Tests Brain.slnx
git commit -m "feat(windows-ino): extract FileSystem/Winget/Shell interfaces + ProcessRunner/FileSystemOperations into isolated DigitalBrain.Windows project; grain classes stay in Kernel per neuron-placement amendment"
```

---

### Task 3: `DigitalBrain.Developer` ino (Git + DotNet + NuGet + Roslyn)

> **AMENDED 2026-07-01:** revised per `docs/superpowers/specs/2026-07-01-real-grain-ino-neuron-placement-amendment.md`.
> `GitNeuron`/`DotNetNeuron`/`NuGetNeuron` are already pure `ProcessRunner` wrappers (`GitNeuron.GetMetricsAsync`
> additionally reads the grain's own `OutgoingJournal`, `CommitAsync`/`RevertAsync` call `FireAsync` —
> inherently grain-coupled, nothing to extract) — they **stay in `DigitalBrain.Kernel/Sdk/`** unchanged except
> a `using DigitalBrain.Developer;` addition. `RoslynNeuron`'s `MSBuildWorkspace` analysis body has zero
> grain coupling beyond its final `FireAsync` call — that part extracts cleanly into a new plain
> `RoslynAnalysisService` in the ino; `RoslynNeuron` itself stays in Kernel as a thin wrapper. Because none of
> the 4 grain classes move, `DigitalBrain.Developer` no longer needs a `ProjectReference` to
> `DigitalBrain.Windows` — it only needs `DigitalBrain.Core`.

**Files:**
- Create: `DigitalBrain.Developer/DigitalBrain.Developer.csproj` (`ProjectReference`: `DigitalBrain.Core` only)
- Move: `DigitalBrain.Core/Sdk/IGitNeuron.cs`, `IDotNetNeuron.cs`, `INuGetNeuron.cs`, `IRoslynNeuron.cs` → `DigitalBrain.Developer/` (namespace → `DigitalBrain.Developer`)
- Create: `DigitalBrain.Developer/RoslynAnalysisService.cs` (new plain class — the `MSBuildWorkspace` analysis logic extracted verbatim from `RoslynNeuron.cs`'s current body, minus the `FireAsync` call)
- Modify: `DigitalBrain.Kernel/Sdk/GitNeuron.cs`, `DotNetNeuron.cs`, `NuGetNeuron.cs` — **stay in Kernel** (do not move); add `using DigitalBrain.Developer;` (for the interfaces); no other change — their bodies are unchanged
- Modify: `DigitalBrain.Kernel/Sdk/RoslynNeuron.cs` — **stays in Kernel** (does not move); drop the `Microsoft.CodeAnalysis`/`Microsoft.CodeAnalysis.MSBuild` `using`s, add `using DigitalBrain.Developer;`, delegate to an injected `RoslynAnalysisService`
- Modify: `DigitalBrain.Developer/DigitalBrain.Developer.csproj` — `Microsoft.CodeAnalysis.CSharp.Workspaces`/`Microsoft.CodeAnalysis.Workspaces.MSBuild` `PackageReference`s move here (copy exact versions from `DigitalBrain.Kernel.csproj`'s current references into `Directory.Packages.props` if not already centrally pinned)
- Modify: `DigitalBrain.Kernel/DigitalBrain.Kernel.csproj` — add `ProjectReference` to `DigitalBrain.Developer` (still needed: Kernel's `GitNeuron`/`DotNetNeuron`/`NuGetNeuron`/`RoslynNeuron` reference the ino's interfaces + `RoslynAnalysisService`)
- Modify: `Brain.slnx` — add `DigitalBrain.Developer`, `DigitalBrain.Developer.Tests`
- Create: `DigitalBrain.Developer.Tests/DigitalBrain.Developer.Tests.csproj`
- Create: `DigitalBrain.Developer.Tests/GitNeuronTests.cs`, `DotNetNeuronTests.cs`, `NuGetNeuronTests.cs`, `RoslynNeuronTests.cs`, `RoslynAnalysisServiceTests.cs`
- Modify: `DigitalBrain.Tests/Sdk/SdkNeuronsTests.cs` — delete `DotNet_Reports_Sdk_Version`, `Git_Status_Works_After_ProcessRunner_Refactor` (moved out); file is now empty of test methods — delete the file entirely and remove it from the project if no methods remain
- Modify: `DigitalBrain.Tests/Sdk/SdkContractsMetadataTests.cs` — add `using DigitalBrain.Developer;`

**Interfaces:**
- Consumes: nothing new from Task 2 — `GitNeuron`/`DotNetNeuron`/`NuGetNeuron` already reference `DigitalBrain.Windows.ProcessRunner` directly from within Kernel (unchanged from before this task).
- Produces: `DigitalBrain.Developer.RoslynAnalysisService.AnalyzeSolutionAsync(solutionPath, ct)` — returns the same report string `RoslynNeuron.AnalyzeSolutionAsync` used to build directly.

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
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp.Workspaces" />
    <PackageReference Include="Microsoft.CodeAnalysis.Workspaces.MSBuild" />
  </ItemGroup>

</Project>
```

Before finalizing, run `grep -n "Microsoft.CodeAnalysis" DigitalBrain.Kernel/DigitalBrain.Kernel.csproj` to get the exact `PackageReference` Include names currently used for Roslyn in Kernel, and match them exactly (do not guess package names). No `DigitalBrain.Windows` reference is needed — the 4 grain classes that need `ProcessRunner` stay in Kernel, which already references `DigitalBrain.Windows` from Task 2.

- [ ] **Step 2: Move the 4 Core interfaces; extract `RoslynAnalysisService`; update the 4 Kernel grain classes**

Move `IGitNeuron.cs`/`IDotNetNeuron.cs`/`INuGetNeuron.cs`/`IRoslynNeuron.cs` from `DigitalBrain.Core/Sdk/` into `DigitalBrain.Developer/`, namespace changed to `DigitalBrain.Developer`. Content otherwise unchanged.

Read `DigitalBrain.Kernel/Sdk/RoslynNeuron.cs`'s real current body first to confirm it matches this plan's snapshot, then create `DigitalBrain.Developer/RoslynAnalysisService.cs` with its `MSBuildWorkspace` logic, minus the `FireAsync` call:

```csharp
// DigitalBrain.Developer/RoslynAnalysisService.cs
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace DigitalBrain.Developer;

public sealed class RoslynAnalysisService
{
    public async Task<string> AnalyzeSolutionAsync(string solutionPath, CancellationToken ct = default)
    {
        using var workspace = MSBuildWorkspace.Create();
        var solution = await workspace.OpenSolutionAsync(solutionPath, cancellationToken: ct);
        var projectCount = solution.Projects.Count();

        var diagnostics = new List<string>();
        foreach (var project in solution.Projects.Take(5))
        {
            var compilation = await project.GetCompilationAsync(ct);
            var errors = compilation!.GetDiagnostics(ct)
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Take(3);
            diagnostics.AddRange(errors.Select(e => $"{project.Name}:{e.Location} {e.GetMessage()}"));
        }

        return $"Solution {solutionPath}: {projectCount} projects. Sample issues: {string.Join("; ", diagnostics)}";
    }
}
```

Update `DigitalBrain.Kernel/Sdk/RoslynNeuron.cs` to delegate to it (drop the `Microsoft.CodeAnalysis`/`Microsoft.CodeAnalysis.MSBuild` `using`s, add `using DigitalBrain.Developer;`):

```csharp
// DigitalBrain.Kernel/Sdk/RoslynNeuron.cs
using DigitalBrain.Core;
using DigitalBrain.Developer;

namespace DigitalBrain.Kernel;

[GrainType("digitalbrain.sdk.roslyn.v1")]
public class RoslynNeuron(ILogger<RoslynNeuron> logger, NeuronJournals journals, RoslynAnalysisService analysis)
    : Neuron(logger, journals), IRoslynNeuron
{
    public async Task<string> AnalyzeSolutionAsync(string solutionPath, CancellationToken ct = default)
    {
        var report = await analysis.AnalyzeSolutionAsync(solutionPath, ct);
        await FireAsync(new ArchitectReport(solutionPath, report));
        return report;
    }
}
```

Add `using DigitalBrain.Developer;` to `DigitalBrain.Kernel/Sdk/GitNeuron.cs`, `DotNetNeuron.cs`, `NuGetNeuron.cs` (for their `IGitNeuron`/`IDotNetNeuron`/`INuGetNeuron` interfaces). No other changes — these 3 classes stay exactly as they are today, calling `ProcessRunner`/`OutgoingJournal`/`FireAsync` directly; none of that is separable from `Neuron` (`GitNeuron.CommitAsync`/`RevertAsync` call `FireAsync`, `GetMetricsAsync` reads `OutgoingJournal` — genuinely grain-coupled, not worth an artificial split).

In `DigitalBrain.Kernel/Program.cs`, register `services.AddSingleton<RoslynAnalysisService>();` near the other SDK neuron/service registrations (read the file first to match the existing registration style).

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

`AnalyzeSolutionAsync`'s return shape is confirmed by this amendment's research pass (the full original body is quoted in Step 2 above) — the assertion above (`Assert.False(string.IsNullOrWhiteSpace(result))`) matches its `"Solution {path}: {N} projects. Sample issues: ..."` return format.

Also add a zero-infra unit test directly against the extracted `RoslynAnalysisService` — no Orleans/TestKit needed:

```csharp
// DigitalBrain.Developer.Tests/RoslynAnalysisServiceTests.cs
using DigitalBrain.Developer;
using Xunit;

namespace DigitalBrain.Developer.Tests;

public class RoslynAnalysisServiceTests
{
    [Fact]
    public async Task AnalyzeSolutionAsync_Analyzes_Real_Solution()
    {
        var service = new RoslynAnalysisService();
        var solutionPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Brain.slnx"));
        var result = await service.AnalyzeSolutionAsync(solutionPath);
        Assert.False(string.IsNullOrWhiteSpace(result));
    }
}
```

- [ ] **Step 7: Delete the now-empty central Sdk test file**

Delete `DigitalBrain.Tests/Sdk/SdkNeuronsTests.cs` entirely (all 4 of its test methods have moved out across Tasks 2 and 3).

Update `DigitalBrain.Tests/Sdk/SdkContractsMetadataTests.cs`: add `using DigitalBrain.Developer;`.

- [ ] **Step 8: Build and test everything**

```
dotnet build Brain.slnx --nologo -clp:NoSummary
dotnet test DigitalBrain.Developer.Tests --nologo
dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~Sdk" --nologo
```
Expected: 0 build errors, `DigitalBrain.Developer.Tests` 5/5 passed (4 grain-level TestKit tests + the new zero-infra `RoslynAnalysisServiceTests`), `DigitalBrain.Tests` Sdk filter only runs `SdkContractsMetadataTests` now and passes.

- [ ] **Step 9: Commit**

```bash
git add DigitalBrain.Developer DigitalBrain.Developer.Tests DigitalBrain.Kernel DigitalBrain.Core DigitalBrain.Tests Brain.slnx Directory.Packages.props
git commit -m "feat(developer-ino): extract Git/DotNet/NuGet/Roslyn interfaces + RoslynAnalysisService into isolated DigitalBrain.Developer project; grain classes stay in Kernel per neuron-placement amendment, close zero-coverage gap"
```

---

### Task 4: `DigitalBrain.Context` ino

> **AMENDED 2026-07-01:** revised per `docs/superpowers/specs/2026-07-01-real-grain-ino-neuron-placement-amendment.md`.
> `ContextNeuron.cs`'s real body (verified) is entirely grain/journal-coupled — `RememberAsync`/`RecallAsync`
> store and scan `MemoryStored` synapses via the grain's own `OutgoingJournal`/`IncomingJournal` and
> `FireAsync`, not via `QdrantVectorStore`/`DocumentIngestor` (those are a separate, already Orleans-free
> subsystem `ContextNeuron` doesn't currently call into). So `ContextNeuron.cs` **stays in
> `DigitalBrain.Kernel`** (does not move); everything else in the original Task 4 Files list is unchanged —
> `ContextServices`/`DocumentIngestor`/`HybridScorer`/`QdrantVectorStore`/`VectorStore` were never
> grain-coupled and move to the ino exactly as originally planned, and so does the `IContextNeuron` interface.

**Files:**
- Create: `DigitalBrain.Context/DigitalBrain.Context.csproj`
- Move: `Context/ContextServices.cs`, `Context/DocumentIngestor.cs`, `Context/HybridScorer.cs`, `Context/QdrantVectorStore.cs`, `Context/VectorStore.cs` → `DigitalBrain.Context/` (namespace → `DigitalBrain.Context`) — **`DigitalBrain.Kernel/ContextNeuron.cs` does NOT move**, it stays where it is
- Move (from `DigitalBrain.Core/Synapse.cs:402`): `IContextNeuron` interface → `DigitalBrain.Context/IContextNeuron.cs` (namespace `DigitalBrain.Context`; keep `ContextUpdate`/`MemoryStored` records in `DigitalBrain.Core/Synapse.cs` unchanged — they're already-journaled generic Synapse payloads, see Global Constraints)
- Delete: `DigitalBrain.Kernel/Context/` (now empty — it only ever held the 5 moved files, not `ContextNeuron.cs`, which lives directly under `DigitalBrain.Kernel/`)
- Modify: `DigitalBrain.Kernel/ContextNeuron.cs` — add `using DigitalBrain.Context;` (for `IContextNeuron` and `HybridScorer`, which `RecallAsync` calls); delete the dead `using DigitalBrain.Kernel.Foundry;` (line 2) and other unused usings confirmed dead against the real body: `Microsoft.Extensions.Configuration`, `ModelContextProtocol.Client`, `ModelContextProtocol.Protocol`, `Orleans.Journaling`, `Orleans.Runtime`, `System.Reflection`, `System.Diagnostics`, `Microsoft.CodeAnalysis`, `Microsoft.CodeAnalysis.MSBuild`, `Microsoft.CodeAnalysis.CSharp` — none of these are referenced in the method bodies (`Logger`/`FireAsync`/`OutgoingJournal`/`IncomingJournal`/`ServiceProvider` all come from the base `Neuron` class already in scope). Keep `Microsoft.Extensions.AI` — the body directly uses `IEmbeddingGenerator<string, Embedding<float>>`.
- Modify: `DigitalBrain.Kernel/Company/CompanySkillOrchestratorNeuron.cs`, `DigitalBrain.Kernel/Gateway/NeuronResolver.cs`, `DigitalBrain.Kernel/Program.cs` — add `using DigitalBrain.Context;`
- Modify: `DigitalBrain.Kernel/JournalJsonContext.cs` — no change needed (`ContextUpdate`/`MemoryStored` stay in Core, already resolvable)
- Modify: `DigitalBrain.Kernel/DigitalBrain.Kernel.csproj` — add `ProjectReference` to `DigitalBrain.Context`; move the Qdrant/`ModelContextProtocol` package references that only `ContextServices`/`QdrantVectorStore`/`DocumentIngestor` need into `DigitalBrain.Context.csproj` — **keep `Microsoft.Extensions.AI` in Kernel too** (`DigitalBrain.Kernel/ContextNeuron.cs` still references `IEmbeddingGenerator`/`Embedding<float>` directly; check `DigitalBrain.Kernel.csproj` for the exact current `PackageReference` names before moving anything)
- Modify: `Brain.slnx` — add `DigitalBrain.Context`, `DigitalBrain.Context.Tests`
- Create: `DigitalBrain.Context.Tests/DigitalBrain.Context.Tests.csproj`, `ContextNeuronTests.cs`

**Interfaces:**
- Produces: `DigitalBrain.Context.IContextNeuron` — `Task<string> GetContextAsync(string contextName)`, `Task RememberAsync(string text)`, `Task<string[]> RecallAsync(string query, int top = 5)` (unchanged signatures, moved namespace only). The concrete implementation (`DigitalBrain.Kernel.ContextNeuron`) stays in Kernel.
- Produces: `DigitalBrain.Context.HybridScorer.Score(...)` (unchanged signature) — `DigitalBrain.Kernel.ContextNeuron.RecallAsync` calls this directly.

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

- [ ] **Step 2: Move the 5 Context files (not `ContextNeuron.cs`)**

Move `ContextServices.cs`/`DocumentIngestor.cs`/`HybridScorer.cs`/`QdrantVectorStore.cs`/`VectorStore.cs` (from `DigitalBrain.Kernel/Context/`) into `DigitalBrain.Context/`, namespace `DigitalBrain.Kernel` → `DigitalBrain.Context` in each. Content otherwise unchanged — verified none of these 5 files reference `Neuron`/Orleans grain types. Delete the now-empty `DigitalBrain.Kernel/Context/` directory. **`DigitalBrain.Kernel/ContextNeuron.cs` stays exactly where it is** — it is not part of this move.

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

- [ ] **Step 4: Clean up `DigitalBrain.Kernel/ContextNeuron.cs`'s usings, add the `DigitalBrain.Context` reference**

`ContextNeuron.cs` stays in `DigitalBrain.Kernel` (Step 2), so this cleanup happens in place, not as part of a move. Its current top-of-file usings are:
```csharp
using DigitalBrain.Core;
using DigitalBrain.Kernel.Foundry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Orleans.Journaling;
using Orleans.Runtime;
using System.Reflection;
using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.CSharp;
```
Replace with:
```csharp
using DigitalBrain.Core;
using DigitalBrain.Context;
using Microsoft.Extensions.AI;
```
`DigitalBrain.Context` is new (for `IContextNeuron` and `HybridScorer.Score`, which `RecallAsync` calls). `Microsoft.Extensions.AI` is kept (`EmbedAsync` uses `IEmbeddingGenerator<string, Embedding<float>>` directly). Every other using was confirmed unused in the method bodies — `Logger`/`FireAsync`/`OutgoingJournal`/`IncomingJournal`/`ServiceProvider` are inherited from the base `Neuron` class already in scope via the `DigitalBrain.Kernel` namespace, needing no explicit using. Run `dotnet build DigitalBrain.Kernel` after this change and delete any further usings the compiler flags as unused.

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
git commit -m "feat(context-ino): extract IContextNeuron + Qdrant memory subsystem into isolated DigitalBrain.Context project; ContextNeuron grain stays in Kernel per neuron-placement amendment"
```

---

### Task 5: `DigitalBrain.UiKit` ino

> **AMENDED 2026-07-01:** revised per `docs/superpowers/specs/2026-07-01-real-grain-ino-neuron-placement-amendment.md`,
> **plus a second, independent correction**: the original Task 5 Files list below contradicted the base
> design spec's own "Project inventory" table, which explicitly scopes `DigitalBrain.UiKit` to
> `IFlutterUiNeuron`/`FlutterUiNeuron` **only** — "not `HomeFeedBus`/`ChatNeuron`/`SignalEgressBus`/stream
> subscribers/`UiSurfaceRfwBridge`, which stay in Kernel as cross-cutting broadcast infra used by many
> neurons beyond this one channel." Per this plan's Global Constraints ("if a task and the spec conflict,
> the spec governs"), the spec wins: those 5 files were never supposed to move, and this revision fixes that
> in addition to applying the Neuron-placement amendment. `FlutterUiNeuron.cs`'s real body (verified) calls
> `ServiceProvider.GetService<HomeFeedBus>()` and `UiSurfaceRfwBridge.FromUiSurface(...)` directly — both
> staying in Kernel confirms it belongs there too, consistent with the Neuron-placement amendment
> independently reaching the same conclusion (its body is 100% grain/Kernel-coupled, nothing to extract).
> Net effect: **only the `IFlutterUiNeuron` interface moves.** Everything else in `DigitalBrain.Kernel/Ui/`
> stays exactly where it is.

**Files:**
- Create: `DigitalBrain.UiKit/DigitalBrain.UiKit.csproj` (`ProjectReference`: `DigitalBrain.Core` only — no `Microsoft.Orleans.Streaming` package needed, since no stream-subscriber code moves here)
- Move (from `DigitalBrain.Core/Synapse.cs:77`): `IFlutterUiNeuron` interface → `DigitalBrain.UiKit/IFlutterUiNeuron.cs` (namespace `DigitalBrain.UiKit`)
- Modify: `DigitalBrain.Core/Synapse.cs` — delete the `IFlutterUiNeuron` interface (line ~77); `IChannelNeuron` (line ~84) stays (generic marker)
- Modify: `DigitalBrain.Kernel/Ui/FlutterUiNeuron.cs` — **stays in Kernel** (does not move); add `using DigitalBrain.UiKit;` (for the interface); no other change — `HomeFeedBus`/`UiSurfaceRfwBridge` it calls are already in scope via the shared `DigitalBrain.Kernel` namespace
- `DigitalBrain.Kernel/Ui/HomeFeedBus.cs`, `HomeFeedStreamSubscriber.cs`, `SignalEgressBus.cs`, `SignalEgressStreamSubscriber.cs`, `UiSurfaceRfwBridge.cs`, `ChatNeuron.cs` — **all stay in `DigitalBrain.Kernel/Ui/` unchanged**, per both the design spec and the Neuron-placement amendment
- Modify: `DigitalBrain.Kernel/Auth/UserSessionNeuron.cs`, `DataVisualizationNeuron.cs`, `DemoNeuron.cs`, `Gateway/GatewayService.cs`, `Gateway/KernelSurfaceDemo.cs`, `GeneratedNeuron.cs`, `KernelTaskNeuron.cs`, `MarketplaceNeuron.cs`, `Program.cs`, `SystemNeurons.cs`, `Ui/ChatNeuron.cs` — add `using DigitalBrain.UiKit;` to each (for the interface)
- Modify: `DigitalBrain.Kernel/DigitalBrain.Kernel.csproj` — add `ProjectReference` to `DigitalBrain.UiKit`
- Modify: `Brain.slnx` — add `DigitalBrain.UiKit`, `DigitalBrain.UiKit.Tests`
- Create: `DigitalBrain.UiKit.Tests/DigitalBrain.UiKit.Tests.csproj`, `FlutterUiNeuronTests.cs`

**Interfaces:**
- Produces: `DigitalBrain.UiKit.IFlutterUiNeuron : INeuron, IHandle<UiSurface>` (unchanged signature, moved namespace). The concrete implementation (`DigitalBrain.Kernel.FlutterUiNeuron`) stays in Kernel, as does `HomeFeedBus`.

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

</Project>
```

No `Microsoft.Orleans.Streaming` package reference is needed — `HomeFeedStreamSubscriber.cs`/`SignalEgressStreamSubscriber.cs` (the files that use `Orleans.Streams`) stay in Kernel, not this project (see amendment note above).

- [ ] **Step 2: Move only the `IFlutterUiNeuron` interface — no `.cs` files move**

Nothing moves out of `DigitalBrain.Kernel/Ui/` in this step. `FlutterUiNeuron.cs`, `HomeFeedBus.cs`, `HomeFeedStreamSubscriber.cs`, `SignalEgressBus.cs`, `SignalEgressStreamSubscriber.cs`, `UiSurfaceRfwBridge.cs`, `ChatNeuron.cs` all stay exactly where they are, byte-for-byte unchanged except `FlutterUiNeuron.cs` gaining one `using` (Step 3). The only file movement in this task is the `IFlutterUiNeuron` interface, handled in Step 3 below.

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

Add `using DigitalBrain.UiKit;` to the top of `DigitalBrain.Kernel/Ui/FlutterUiNeuron.cs` (its only change this task — the class itself, and its calls to `HomeFeedBus`/`UiSurfaceRfwBridge`, are unaffected since those stay in the same `DigitalBrain.Kernel` namespace).

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
    public async Task HandleAsync_Records_The_Delivered_UiSurface_In_The_Incoming_Journal()
    {
        var flutter = _brain.Grain<IFlutterUiNeuron>("flutter-ui");
        var surface = new UiSurface("test-kind", new Dictionary<string, object?> { ["title"] = "smoke" });

        await flutter.HandleAsync(surface);

        var incoming = await flutter.GetIncomingTimelineAsync();
        Assert.Contains(incoming, s => s is UiSurface delivered && delivered.Kind == "test-kind");
    }
}
```

`UiSurface`'s real constructor is confirmed: `UiSurface(string Kind, IReadOnlyDictionary<string, object?> Props)` (`DigitalBrain.Core/UiSurfaces.cs:6`) — the call above matches it exactly, no adjustment needed.

This asserts on observable grain state (the delivered synapse landing in `IncomingTimelineAsync`) rather than merely "didn't throw," per the plan's pre-flight review decision. `FlutterUiNeuron.HandleAsync`'s real body doesn't call `FireAsync` (so `GetTimelineAsync`/outgoing journal won't show anything from this call) — `DeliverAsync` itself is what records the incoming synapse, which is what this assertion checks. Verifying the deeper `HomeFeedBus.Broadcast` fan-out itself would need a test double wired through `TestDigitalBrain`'s `extend` hook and is out of scope for this smoke test.

- [ ] **Step 8: Build and test everything**

```
dotnet build Brain.slnx --nologo -clp:NoSummary
dotnet test DigitalBrain.UiKit.Tests --nologo
dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~Marketplace|FullyQualifiedName~SystemNeurons|FullyQualifiedName~DataVisualization" --nologo
```
Expected: 0 build errors, UiKit.Tests 1/1 passed, the filtered central tests (which exercise the 11 fixed cross-reference files) still green.

- [ ] **Step 9: Commit**

```bash
git add DigitalBrain.UiKit DigitalBrain.UiKit.Tests DigitalBrain.Kernel DigitalBrain.Core Brain.slnx
git commit -m "feat(uikit-ino): extract IFlutterUiNeuron into isolated DigitalBrain.UiKit project, add first direct test; FlutterUiNeuron/HomeFeed/SignalEgress stay in Kernel per spec + neuron-placement amendment"
```

---

### Task 6: `DigitalBrain.Telegram.Channel` ino

> **AMENDED 2026-07-01:** revised per `docs/superpowers/specs/2026-07-01-real-grain-ino-neuron-placement-amendment.md`.
> `TelegramChatNeuron.cs`'s real body (verified, 102 lines) is entirely grain/journal-coupled — reads
> `IncomingJournal`, calls `Broadcast`/`FireAsync`-derived helpers, `GrainFactory.GetGrain<>`,
> `StampCurrent`/`Self`/`CurrentCause` — every method except the trivial static `TryParseStart` string
> parser needs `Neuron`. It **stays in `DigitalBrain.Kernel`** (does not move); only the `ITelegramChatNeuron`
> interface moves to the ino.

**Files:**
- Create: `DigitalBrain.Telegram.Channel/DigitalBrain.Telegram.Channel.csproj`
- Move (from `DigitalBrain.Core/Synapse.cs:72`): `ITelegramChatNeuron` interface → `DigitalBrain.Telegram.Channel/ITelegramChatNeuron.cs` (namespace `DigitalBrain.Telegram.Channel`)
- Modify: `DigitalBrain.Kernel/TelegramChatNeuron.cs` — **stays in Kernel** (does not move); add `using DigitalBrain.Telegram.Channel;` (for the interface); no other change
- Modify: `DigitalBrain.Kernel/Gateway/GatewayService.cs` — add `using DigitalBrain.Telegram.Channel;`
- Modify: `DigitalBrain.Kernel/DigitalBrain.Kernel.csproj` — add `ProjectReference` to `DigitalBrain.Telegram.Channel`
- Modify: `Brain.slnx` — add `DigitalBrain.Telegram.Channel`, `DigitalBrain.Telegram.Channel.Tests`
- Create: `DigitalBrain.Telegram.Channel.Tests/DigitalBrain.Telegram.Channel.Tests.csproj`
- Move: `DigitalBrain.Tests/Telegram/TelegramChatNeuronTests.cs` → `DigitalBrain.Telegram.Channel.Tests/TelegramChatNeuronTests.cs` (update `using`s: `DigitalBrain.Tests.TestSupport` → `DigitalBrain.TestKit`, add `DigitalBrain.Telegram.Channel`) — this move is unaffected by the amendment: the test only ever referenced the `ITelegramChatNeuron` interface + `TestDigitalBrain`, and `TestDigitalBrain` already resolves `DigitalBrain.Kernel`'s concrete `TelegramChatNeuron` transitively (confirmed in Task 1); `TelegramDeepLinkRoutingTests.cs` stays central (it likely also touches gateway/routing concerns beyond the grain itself — read it first to confirm before deciding whether it moves too)

**Interfaces:**
- Produces: `DigitalBrain.Telegram.Channel.ITelegramChatNeuron` (unchanged signature, moved namespace). The concrete implementation (`DigitalBrain.Kernel.TelegramChatNeuron`) stays in Kernel.

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

- [ ] **Step 2: `TelegramChatNeuron.cs` stays in Kernel — no file move this step**

`DigitalBrain.Kernel/TelegramChatNeuron.cs` is not moved. Its `using DigitalBrain.Telegram.Channel;` addition (for the interface) happens in Step 3 below, alongside the interface's own move.

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

Add `using DigitalBrain.Telegram.Channel;` to the top of `DigitalBrain.Kernel/TelegramChatNeuron.cs`.

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
git commit -m "feat(telegram-channel-ino): extract ITelegramChatNeuron into isolated DigitalBrain.Telegram.Channel project; TelegramChatNeuron grain stays in Kernel per neuron-placement amendment"
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

> **AMENDED 2026-07-01:** revised per `docs/superpowers/specs/2026-07-01-real-grain-ino-neuron-placement-amendment.md`.
> `GmailNeuron`/`GoogleDriveNeuron`/`GoogleCalendarNeuron`/`GoogleAuthNeuron` all derive from `Neuron`
> (`: Neuron(logger, journals), IGmailNeuron`, etc. — see Step 7/7b below), so they cannot live in the
> Core-only `DigitalBrain.Google` project as originally written. This is net-new code (not an extraction from
> an existing file), so the fix is simple: **the 4 grain classes are created directly in
> `DigitalBrain.Kernel/Google/` instead of `DigitalBrain.Google/`.** Everything else — the 3 public
> interfaces, the 3 API client interfaces + real implementations, `GoogleCredentialFactory` — stays in
> `DigitalBrain.Google` exactly as originally written; none of those ever referenced `Neuron`.

**Files:**
- Create: `DigitalBrain.Google/DigitalBrain.Google.csproj`
- Create: `DigitalBrain.Google/IGmailNeuron.cs`, `IGoogleDriveNeuron.cs`, `IGoogleCalendarNeuron.cs`
- Create: `DigitalBrain.Google/IGmailApiClient.cs`, `IGoogleDriveApiClient.cs`, `IGoogleCalendarApiClient.cs`
- Create: `DigitalBrain.Google/GoogleGmailApiClient.cs`, `GoogleDriveApiClient.cs`, `GoogleCalendarApiClient.cs`
- Create: `DigitalBrain.Google/GoogleCredentialFactory.cs`
- Create: `DigitalBrain.Kernel/Google/GmailNeuron.cs`, `GoogleDriveNeuron.cs`, `GoogleCalendarNeuron.cs`, `GoogleAuthNeuron.cs` (namespace `DigitalBrain.Kernel` — **not** `DigitalBrain.Google`; new subfolder under Kernel, following the existing `DigitalBrain.Kernel/Ui/`, `DigitalBrain.Kernel/Auth/`, `DigitalBrain.Kernel/Gateway/` convention of grouping related grains in a subfolder)
- Modify: `DigitalBrain.Core/Signals.cs` — add `GoogleSignals` string-constant class
- Modify: `Directory.Packages.props` — add `Google.Apis.Gmail.v1`, `Google.Apis.Drive.v3`, `Google.Apis.Calendar.v3`, `Google.Apis.Auth`
- Modify: `DigitalBrain.Kernel/DigitalBrain.Kernel.csproj` — add `ProjectReference` to `DigitalBrain.Google` (still needed: Kernel's `Google/*.cs` grain classes reference the ino's interfaces + API client types)
- Modify: `Brain.slnx` — add `DigitalBrain.Google`, `DigitalBrain.Google.Tests`
- Create: `DigitalBrain.Google.Tests/DigitalBrain.Google.Tests.csproj`, `FakeGoogleApiClients.cs`, `GmailNeuronTests.cs`, `GoogleDriveNeuronTests.cs`, `GoogleCalendarNeuronTests.cs`

**Interfaces:**
- Produces: `IGmailNeuron.ListMessagesAsync(string query, int maxResults)`, `.ReadMessageAsync(string messageId)`, `.SendMessageAsync(string to, string subject, string body)`. Concrete implementation lives in `DigitalBrain.Kernel.GmailNeuron`.
- Produces: `IGoogleDriveNeuron.ListFilesAsync(string query)`, `.UploadFileAsync(string name, string content, string mimeType)`, `.DownloadFileAsync(string fileId)`, `.DeleteFileAsync(string fileId)`. Concrete implementation lives in `DigitalBrain.Kernel.GoogleDriveNeuron`.
- Produces: `IGoogleCalendarNeuron.ListEventsAsync(string timeMinIso, string timeMaxIso)`, `.CreateEventAsync(string summary, string startIso, string endIso, string description)`, `.DeleteEventAsync(string eventId)`. Concrete implementation lives in `DigitalBrain.Kernel.GoogleCalendarNeuron`.

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

- [ ] **Step 7: Write the three real grains — in `DigitalBrain.Kernel/Google/`, not `DigitalBrain.Google/`**

```csharp
// DigitalBrain.Kernel/Google/GmailNeuron.cs
using DigitalBrain.Core;
using DigitalBrain.Google;

namespace DigitalBrain.Kernel;

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

Note the namespace is `DigitalBrain.Kernel` (matching its physical location, same convention as every other grain in this codebase) with `using DigitalBrain.Google;` for `IGmailNeuron`/`IGmailApiClient`. `NeuronJournals`/`Neuron` are already in scope via the shared `DigitalBrain.Kernel` namespace — no extra using needed for those, unlike the real-grain inos in Tasks 2-6 where the grain and its interface are split across two projects.

Before finalizing this constructor, read `DigitalBrain.Kernel/Sdk/WingetNeuron.cs`'s real constructor signature (`WingetNeuron(ILogger<WingetNeuron> logger, NeuronJournals journals) : base(logger, journals) { }`) and confirm whether `Neuron`'s base constructor accepts additional DI-resolved parameters cleanly via Orleans activation (it should — Orleans resolves grain constructor parameters from the DI container automatically) — if `IGmailApiClient` isn't registered in DI, grain activation will throw at runtime; Step 8 registers it.

Write `DigitalBrain.Kernel/Google/GoogleDriveNeuron.cs` and `GoogleCalendarNeuron.cs` following the identical pattern (namespace `DigitalBrain.Kernel`, `using DigitalBrain.Google;`, constructor takes the matching `I*ApiClient`, each interface method is a one-line delegation).

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
// DigitalBrain.Kernel/Google/GoogleAuthNeuron.cs
using DigitalBrain.Core;
using DigitalBrain.Google;

namespace DigitalBrain.Kernel;

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
(This reference is for the interfaces/API client types Kernel's `Google/*.cs` grain classes consume — it's the same `ProjectReference` the original plan already specified, still needed under the amendment even though the grain classes themselves now live in Kernel rather than `DigitalBrain.Google`.)

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

This is the first real exercise of Step 1's `TestDigitalBrain(Action<ISiloBuilder>? extend)` extension point — if it doesn't work as designed, fix `TestDigitalBrain` in `DigitalBrain.TestKit` now rather than working around it here (the whole point of that constructor parameter was exactly this use case). This test code is unaffected by the amendment's grain relocation: `_brain.Grain<IGmailNeuron>(...)` resolves to `DigitalBrain.Kernel.GmailNeuron` transitively through `DigitalBrain.TestKit`'s existing reference to `DigitalBrain.Kernel` (confirmed in Task 1), exactly as it would have resolved to a `DigitalBrain.Google`-hosted grain under the original (uncompilable) plan.

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
git commit -m "feat(google-ino): add Gmail/Drive/Calendar interfaces + mockable API clients as isolated DigitalBrain.Google project; grain classes live in Kernel/Google per neuron-placement amendment"
```

---

### Task 9: `DigitalBrain.Experience.PersonalAssistant` ino

> **AMENDED 2026-07-01:** per `docs/superpowers/specs/2026-07-01-real-grain-ino-neuron-placement-amendment.md`
> and Task 4's revision, `ContextNeuron` (the concrete grain) lives in `DigitalBrain.Kernel`, not
> `DigitalBrain.Context`. This task's `IHandle<Signal>` addition below goes into
> `DigitalBrain.Kernel/ContextNeuron.cs`. `PersonalAssistantNeuron` itself is unaffected — it's a pure-pack
> `IPackBehavior`, never derives from `Neuron`, and was never blocked by the Neuron-placement issue.

**Files:**
- Create: `DigitalBrain.Experience.PersonalAssistant/DigitalBrain.Experience.PersonalAssistant.csproj`
- Create: `DigitalBrain.Experience.PersonalAssistant/PersonalAssistantNeuron.cs`
- Modify: `DigitalBrain.Core/Signals.cs` — add `ContextSignals` string-constant class
- Modify: `DigitalBrain.Kernel/ContextNeuron.cs` — add `IHandle<Signal>` handling `ContextSignals.RecallRequested` (**not** `DigitalBrain.Context/ContextNeuron.cs` — the concrete grain stays in Kernel per Task 4's amendment)
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

Add to `DigitalBrain.Kernel/ContextNeuron.cs` (class declaration becomes `public class ContextNeuron : Neuron, IContextNeuron, IHandle<Signal>`):

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
git add DigitalBrain.Kernel DigitalBrain.Context.Tests DigitalBrain.Core
git commit -m "feat(context): handle ContextRecallRequested signal on the Kernel-hosted ContextNeuron grain, reusing the AskLlm reply-by-name pattern"
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

> **AMENDED 2026-07-01 (pre-flight decision, independent of the Neuron-placement amendment):** the original
> Step 1 below hand-copied `PersonalAssistantNeuron.cs`'s source into a `const string` in `MarketplaceSeeds.cs`
> — a second, manually-maintained copy that drifts the moment the real file changes. Reviewed before
> execution began and revised: instead of a hand-copied duplicate, `DigitalBrain.Core` embeds
> `PersonalAssistantNeuron.cs` itself as a build-time `EmbeddedResource`, read back at runtime. The file's
> content is the single source of truth — no copy-paste, no drift. This does not create a project reference
> from Core to `DigitalBrain.Experience.PersonalAssistant` (it's a file-system-path resource include, not an
> assembly reference) and does not change Core's "zero vendor/integration type references" property verified
> in Task 11 — it's data (a resource), not a compiled type dependency, same category as the existing
> `TelegramResponderPackCode` pattern this replaces the naive form of.

**Files:**
- Modify: `DigitalBrain.Core/DigitalBrain.Core.csproj` — add an `EmbeddedResource` include for `PersonalAssistantNeuron.cs`
- Modify: `DigitalBrain.Core/MarketplaceSeeds.cs`
- Modify: `Brain.slnx`

**Interfaces:**
- Consumes: `DigitalBrain.Experience.PersonalAssistant.PersonalAssistantNeuron`'s real source (Task 9), embedded as a resource and read back at runtime — never hand-copied.

- [ ] **Step 1: Embed `PersonalAssistantNeuron.cs` as a resource, expose its content via a property**

Add to `DigitalBrain.Core/DigitalBrain.Core.csproj`:
```xml
<ItemGroup>
  <EmbeddedResource Include="..\DigitalBrain.Experience.PersonalAssistant\PersonalAssistantNeuron.cs">
    <LogicalName>PersonalAssistantNeuron.cs</LogicalName>
  </EmbeddedResource>
</ItemGroup>
```

In `DigitalBrain.Core/MarketplaceSeeds.cs`, near `TelegramResponderPackCode`, add:

```csharp
public static string PersonalAssistantPackCode => _personalAssistantPackCode.Value;

private static readonly Lazy<string> _personalAssistantPackCode = new(() => ReadEmbeddedSource("PersonalAssistantNeuron.cs"));

private static string ReadEmbeddedSource(string logicalName)
{
    using var stream = typeof(MarketplaceSeeds).Assembly.GetManifestResourceStream(logicalName)
        ?? throw new InvalidOperationException($"Embedded resource '{logicalName}' not found.");
    using var reader = new StreamReader(stream);
    return reader.ReadToEnd();
}
```

`PersonalAssistantPackCode` is a property (not `const`), since its value is read from the embedded resource at first access rather than known at compile time — every call site in `LocalUiPacks` (Step 2 below) works identically either way.

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

In `brain/docs/SYSTEM_DESIGN.md` §1.3 ("Project graph"), add rows for the 8 new real-grain/pure-pack projects and `DigitalBrain.TestKit`, following the existing table's exact format (Project | Purpose columns). Note the new "every integration's contract is a peer ino" architecture in a short new subsection, referencing both the design spec and the neuron-placement amendment (`docs/superpowers/specs/2026-07-01-real-grain-ino-neuron-placement-amendment.md`) — be precise that the concrete `Neuron`-derived grain classes for Windows/Developer/Context/UiKit/Telegram.Channel/Google stay in (or moved into) `DigitalBrain.Kernel`, while the isolated inos hold the public interfaces plus whatever real capability logic is genuinely Orleans-independent (full for Windows/Developer/Context/Google, interface-only for UiKit/Telegram.Channel). Don't describe every real-grain ino as "hosting its own grain" — only the pure-pack inos (Telegram, Experience.PersonalAssistant) are fully self-contained.

- [ ] **Step 6: Final commit**

```bash
git add brain/docs/SYSTEM_DESIGN.md
git commit -m "docs: update SYSTEM_DESIGN.md with the isolated-ino project graph"
```
