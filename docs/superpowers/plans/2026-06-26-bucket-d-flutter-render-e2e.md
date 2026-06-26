# Bucket D — Flutter Render E2E Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prove the server-driven UI path end-to-end in a real browser — a real embodied pack emits a surface, and a Playwright-driven Chromium loads the live Flutter web app (served by the kernel) and asserts the specific pack-driven widget rendered.

**Architecture:** Single-origin: the kernel's Kestrel serves the built Flutter web bundle AND gRPC-Web on one HTTP/1-capable endpoint; the browser loads the SPA and gRPC-Webs `WatchHomeFeed` back to the same origin. The render E2E boots the full Aspire AppHost forced to **one** kernel replica (so the per-silo `HomeFeedBus` fanout is deterministic) and is gated so it never runs in the default suite.

**Tech Stack:** .NET net11.0, ASP.NET Core (static files + `Grpc.AspNetCore.Web`), Orleans 10.2, Aspire 13.4.6 + `Aspire.Hosting.Testing`, xUnit 2.9.3 + `Xunit.SkippableFact`, `Microsoft.Playwright` 1.49.0; Flutter (`Semantics`, `SemanticsBinding.ensureSemantics`, RFW runtime).

## Global Constraints

- Target framework **net11.0**. Central package versions only (`Directory.Packages.props`); never `Version="*"` or inline versions.
- **Context7 / Dart-Flutter MCP first** before writing code against any framework API (`Grpc.AspNetCore.Web` `UseGrpcWeb`, ASP.NET Core static files / `MapFallbackToFile`, `Xunit.SkippableFact`, Flutter `Semantics(identifier:)` / `SemanticsBinding.instance.ensureSemantics()`, Playwright .NET locators). Verified for this plan: `SemanticsBinding.instance.ensureSemantics()` (guard with `kIsWeb`); gRPC-Web server = `app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true })` after `UseRouting`.
- No vacuous `///` XML docs; self-explanatory names; small inline comments only where non-obvious; relative paths.
- **Two repos:** Tasks 1, 2, 4, 5, 6 are in `brain/` (branch `hardening/bucket-d`). Task 3 is in `app/` (its own git repo, separate commit there).
- **Per-task verification ritual:** `dotnet build` + the task's targeted `dotnet test --filter` (brain) or `flutter test` (app). The default high-sev suite must stay green AND must never select the E2E collection.
- Build/test working dir for brain: `E:\digitalbraintech\brain`. For app: `E:\digitalbraintech\app`.

---

### Task 1: Kernel serves the Flutter web bundle (gated on `DIGITALBRAIN_WEBROOT`)

Serve static files + SPA fallback from the kernel only when `DIGITALBRAIN_WEBROOT` is set, so normal/production boots are unchanged.

**Files:**
- Modify: `DigitalBrain.Kernel/Program.cs` (after `var app = builder.Build();`, around line 107-115)
- Create: `DigitalBrain.Tests/Kernel/KernelStaticServingTests.cs`

**Interfaces:**
- Consumes: `WebApplicationFactory<Program>` (existing pattern, see `DigitalBrain.Tests/Gateway/GatewayGrpcWireTests.cs`), env var `DIGITALBRAIN_WEBROOT`.
- Produces: kernel serves `index.html` at `/` and SPA-falls-back unknown non-API routes to `index.html` when `DIGITALBRAIN_WEBROOT` points at a directory containing `index.html`.

- [ ] **Step 1: Write the failing test**

Create `DigitalBrain.Tests/Kernel/KernelStaticServingTests.cs`:

```csharp
using System.IO;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace DigitalBrain.Tests.Kernel;

[Collection("silo-host")]
public class KernelStaticServingTests
{
    [Fact]
    public async Task Serves_Index_Html_From_Configured_WebRoot()
    {
        var webRoot = Path.Combine(Path.GetTempPath(), "dbtest-webroot-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(webRoot);
        await File.WriteAllTextAsync(Path.Combine(webRoot, "index.html"), "<!doctype html><title>db-e2e-marker</title>");
        try
        {
            using var factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(b => b.UseSetting("DIGITALBRAIN_WEBROOT", webRoot));
            using var client = factory.CreateClient();

            var root = await client.GetStringAsync("/");
            Assert.Contains("db-e2e-marker", root);

            // SPA fallback: an unknown non-API path returns index.html, not 404.
            var deep = await client.GetStringAsync("/canvas/anything");
            Assert.Contains("db-e2e-marker", deep);
        }
        finally
        {
            Directory.Delete(webRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Without_WebRoot_Root_Is_Not_Served_As_Index()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        var resp = await client.GetAsync("/");
        Assert.NotEqual(System.Net.HttpStatusCode.OK, resp.StatusCode);
    }
}
```

- [ ] **Step 2: Run the test, expect FAIL**

Run: `dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "FullyQualifiedName~KernelStaticServingTests" --logger "console;verbosity=minimal"`
Expected: FAIL — `/` does not return the index marker (no static serving yet).

- [ ] **Step 3: Add gated static serving to the kernel**

In `DigitalBrain.Kernel/Program.cs`, immediately after `var app = builder.Build();` (line 107) and before `app.MapGrpcService...`:

```csharp
var webRoot = builder.Configuration["DIGITALBRAIN_WEBROOT"];
var serveWebBundle = !string.IsNullOrWhiteSpace(webRoot) && Directory.Exists(webRoot);
if (serveWebBundle)
{
    var fileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(Path.GetFullPath(webRoot!));
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fileProvider });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = fileProvider });
}
```

And after the gRPC service maps (after line 110), add the SPA fallback (only when serving):

```csharp
if (serveWebBundle)
{
    var indexPath = Path.Combine(Path.GetFullPath(webRoot!), "index.html");
    app.MapFallback(async context =>
    {
        context.Response.ContentType = "text/html";
        await context.Response.SendFileAsync(indexPath);
    });
}
```

- [ ] **Step 4: Run the test, expect PASS**

Run: `dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "FullyQualifiedName~KernelStaticServingTests" --logger "console;verbosity=minimal"`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add DigitalBrain.Kernel/Program.cs DigitalBrain.Tests/Kernel/KernelStaticServingTests.cs
git commit -m "feat(kernel): serve Flutter web bundle + SPA fallback when DIGITALBRAIN_WEBROOT set"
```

---

### Task 2: Kernel speaks gRPC-Web

Enable gRPC-Web so a browser can call `WatchHomeFeed`. Additive — native gRPC over HTTP/2 keeps working.

**Files:**
- Modify: `Directory.Packages.props` (add two package versions)
- Modify: `DigitalBrain.Kernel/DigitalBrain.Kernel.csproj` (add `Grpc.AspNetCore.Web`)
- Modify: `DigitalBrain.Tests/DigitalBrain.Tests.csproj` (add `Grpc.Net.Client.Web` for the test)
- Modify: `DigitalBrain.Kernel/Program.cs` (add `UseGrpcWeb`)
- Create: `DigitalBrain.Tests/Kernel/KernelGrpcWebTests.cs`

**Interfaces:**
- Consumes: existing `DigitalBrainGateway` gRPC service (`HealthAsync`), `GrpcWebHandler` (from `Grpc.Net.Client.Web`).
- Produces: gRPC-Web requests to the kernel succeed over HTTP/1.1.

- [ ] **Step 1: Add central package versions**

In `Directory.Packages.props`, under the gRPC comment (after the `Grpc.AspNetCore` line ~78):

```xml
    <PackageVersion Include="Grpc.AspNetCore.Web" Version="2.71.0" />
    <PackageVersion Include="Grpc.Net.Client.Web" Version="2.71.0" />
```

- [ ] **Step 2: Reference the packages**

In `DigitalBrain.Kernel/DigitalBrain.Kernel.csproj`, in the `ItemGroup` with the other `Grpc.AspNetCore` reference, add:

```xml
    <PackageReference Include="Grpc.AspNetCore.Web" />
```

In `DigitalBrain.Tests/DigitalBrain.Tests.csproj`, in the package `ItemGroup`, add:

```xml
    <PackageReference Include="Grpc.Net.Client.Web" />
```

- [ ] **Step 3: Write the failing test**

Create `DigitalBrain.Tests/Kernel/KernelGrpcWebTests.cs`:

```csharp
using DigitalBrain.Runtime.Grpc;
using Grpc.Net.Client;
using Grpc.Net.Client.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace DigitalBrain.Tests.Kernel;

[Collection("silo-host")]
public class KernelGrpcWebTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public KernelGrpcWebTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Health_Over_GrpcWeb_Succeeds()
    {
        var handler = new GrpcWebHandler(GrpcWebMode.GrpcWeb, _factory.Server.CreateHandler());
        using var channel = GrpcChannel.ForAddress(_factory.Server.BaseAddress, new GrpcChannelOptions { HttpHandler = handler });
        var client = new DigitalBrainGateway.DigitalBrainGatewayClient(channel);

        var reply = await client.HealthAsync(new HealthRequest());
        Assert.True(reply.Ok);
    }
}
```

- [ ] **Step 4: Run the test, expect FAIL**

Run: `dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "FullyQualifiedName~KernelGrpcWebTests" --logger "console;verbosity=minimal"`
Expected: FAIL — gRPC-Web not enabled (the request is not unwrapped; call errors).

- [ ] **Step 5: Enable gRPC-Web in the kernel**

In `DigitalBrain.Kernel/Program.cs`, after `var app = builder.Build();` and before the `app.MapGrpcService` calls, add:

```csharp
app.UseRouting();
app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });
```

(`UseGrpcWeb` with `DefaultEnabled = true` enables it for all mapped gRPC services; no per-service `.EnableGrpcWeb()` needed.)

- [ ] **Step 6: Run the test, expect PASS**

Run: `dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "FullyQualifiedName~KernelGrpcWebTests|FullyQualifiedName~GatewayGrpcWireTests" --logger "console;verbosity=minimal"`
Expected: PASS — gRPC-Web call succeeds AND the existing native-gRPC `GatewayGrpcWireTests` still pass (additive).

- [ ] **Step 7: Commit**

```bash
git add Directory.Packages.props DigitalBrain.Kernel/DigitalBrain.Kernel.csproj DigitalBrain.Tests/DigitalBrain.Tests.csproj DigitalBrain.Kernel/Program.cs DigitalBrain.Tests/Kernel/KernelGrpcWebTests.cs
git commit -m "feat(kernel): enable gRPC-Web (browser clients) alongside native gRPC"
```

---

### Task 3: Flutter exposes identified semantics for rendered surfaces (app repo)

Wrap each RFW-rendered surface root in `Semantics(identifier:, label:)` so a browser test can locate it, and force-enable the web semantics tree under a test-only dart-define. **This task is in `E:\digitalbraintech\app` (its own git repo).**

**Files:**
- Modify: `app/lib/rfw_host/rfw_runtime_host.dart` (`render` method)
- Modify: `app/lib/main.dart` (force-enable semantics under define)
- Create: `app/test/rfw_host/rfw_semantics_test.dart`

**Interfaces:**
- Consumes: `RfwRuntimeHost.render` (existing signature: `Widget render(String key, {required Map<String,Object?> data, required RemoteEventHandler onEvent, String rootWidget = 'root'})`).
- Produces: `render(...)` accepts an optional `semanticsId` (defaults to `key`); its output carries a `Semantics` node whose `identifier == semanticsId`. On web under `--dart-define=DIGITALBRAIN_E2E=true`, the semantics tree is force-enabled at boot.

- [ ] **Step 1: Verify the Flutter APIs via the Dart/Flutter MCP**

Confirm with the Dart/Flutter MCP (or api.flutter.dev) the exact spellings used below: `Semantics(identifier:, label:, child:)`, `SemanticsBinding.instance.ensureSemantics()`, and the widget-test finder `find.bySemanticsIdentifier(...)` (fallback: `tester.getSemantics(find.byKey(...)).identifier`). The mechanism is fixed; adjust only spellings if the API differs.

- [ ] **Step 2: Write the failing widget test**

Create `app/test/rfw_host/rfw_semantics_test.dart`:

```dart
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:digitalbrain_flutter/rfw_host/rfw_runtime_host.dart';

void main() {
  testWidgets('rendered surface carries a stable semantics identifier', (tester) async {
    final handle = tester.ensureSemantics();
    final host = RfwRuntimeHost();
    const src = '''
      import core.widgets;
      widget root = Text(text: "hello-pack");
    ''';
    host.ensureLoaded('e2e-doc', src);

    await tester.pumpWidget(MaterialApp(
      home: host.render(
        'e2e-doc',
        data: const <String, Object?>{},
        onEvent: (_, __) {},
        semanticsId: 'pack-surface-e2e',
        semanticsLabel: 'pack-surface-e2e',
      ),
    ));
    await tester.pumpAndSettle();

    expect(find.bySemanticsIdentifier('pack-surface-e2e'), findsOneWidget);
    handle.dispose();
  });
}
```

- [ ] **Step 3: Run the test, expect FAIL**

Run (in `E:\digitalbraintech\app`): `flutter test test/rfw_host/rfw_semantics_test.dart`
Expected: FAIL to compile — `render` has no `semanticsId`/`semanticsLabel` parameters yet.

- [ ] **Step 4: Add the semantics wrap to `render`**

In `app/lib/rfw_host/rfw_runtime_host.dart`, replace the `render` method:

```dart
  Widget render(
    String key, {
    required Map<String, Object?> data,
    required RemoteEventHandler onEvent,
    String rootWidget = 'root',
    String? semanticsId,
    String? semanticsLabel,
  }) {
    final remote = RemoteWidget(
      runtime: _runtime,
      data: DynamicContent(data),
      widget: FullyQualifiedWidgetName(LibraryName(['doc', key]), rootWidget),
      onEvent: onEvent,
    );
    return Semantics(
      identifier: semanticsId ?? key,
      label: semanticsLabel,
      container: true,
      child: remote,
    );
  }
```

- [ ] **Step 5: Run the test, expect PASS**

Run (in `E:\digitalbraintech\app`): `flutter test test/rfw_host/rfw_semantics_test.dart`
Expected: PASS.

- [ ] **Step 6: Pass the surface id at the live render call site**

Find the live-surface render call (the panel that renders incoming `RfwCardEnvelope`s — search `lib/features/canvas` / `lib/rfw_host/rfw_card_sources.dart` for `.render(`). Pass the card's stable id through as both `semanticsId` and `semanticsLabel`. Use the envelope's `rootWidget` plus a marker carried in `dataJson` (key `"source"` value, or the surface `SurfaceId`) so a pack's surface yields a deterministic id. Concretely, where the live card calls `host.render(docKey, data: ..., onEvent: ...)`, add `semanticsId: surfaceId, semanticsLabel: surfaceId` where `surfaceId` is the value already used as `docKey` for that card.

- [ ] **Step 7: Force-enable web semantics under the E2E dart-define**

In `app/lib/main.dart`, inside `main()` after `WidgetsFlutterBinding.ensureInitialized();` (line 19), add:

```dart
  if (kIsWeb && const bool.fromEnvironment('DIGITALBRAIN_E2E')) {
    SemanticsBinding.instance.ensureSemantics();
  }
```

Add the import at the top: `import 'package:flutter/semantics.dart';`

- [ ] **Step 8: Run the app analyzer + the semantics test**

Run (in `E:\digitalbraintech\app`): `flutter analyze lib/rfw_host/rfw_runtime_host.dart lib/main.dart && flutter test test/rfw_host/rfw_semantics_test.dart`
Expected: analyze clean (no new issues in the touched files); test PASS.

- [ ] **Step 9: Commit (in the app repo)**

```bash
cd E:/digitalbraintech/app
git add lib/rfw_host/rfw_runtime_host.dart lib/main.dart test/rfw_host/rfw_semantics_test.dart
git commit -m "feat(rfw): stable semantics identifier on rendered surfaces; force web semantics under DIGITALBRAIN_E2E"
```

---

### Task 4: Aspire enabling — dedicated HTTP/1 web endpoint + env-driven replica count

Give the kernel a browser-facing HTTP/1 endpoint (separate from the h2 `grpc` endpoint) and let the AppHost run a single replica for deterministic fanout. **No cheap unit test — verified by build here and by the Task 6 E2E.**

**Files:**
- Modify: `DigitalBrain.Kernel/Program.cs` (Kestrel: add an Http1AndHttp2 web port in Aspire mode)
- Modify: `DigitalBrain.Aspire/DigitalBrainBuilderExtensions.cs` (`WireKernelSilo`: add `web` endpoint + external; read replicas from env in `AddDigitalBrain`)
- Modify: `NeuroOSPrototype.AppHost/AppHost.cs` (no change if replicas read in extension; otherwise pass through)

**Interfaces:**
- Consumes: env `DIGITALBRAIN_WEB_PORT` (kernel binds Http1AndHttp2 there when set under Aspire), `DIGITALBRAIN_KERNEL_REPLICAS` (AppHost replica count, default 3).
- Produces: an externally-reachable `web` endpoint on the kernel resource named `"web"`, scheme `http`, protocols Http1AndHttp2.

- [ ] **Step 1: Bind an HTTP/1 web port in the kernel (Aspire mode)**

In `DigitalBrain.Kernel/Program.cs`, in `ConfigureKestrel`, replace the Aspire-hosted branch (lines 28-32) so an optional web port listens Http1AndHttp2:

```csharp
    if (isAspireHosted)
    {
        options.ConfigureEndpointDefaults(listen => listen.Protocols = HttpProtocols.Http2);
        var webPort = Environment.GetEnvironmentVariable("DIGITALBRAIN_WEB_PORT");
        if (int.TryParse(webPort, out var port))
        {
            options.ListenAnyIP(port, listen => listen.Protocols = HttpProtocols.Http1AndHttp2);
        }
        return;
    }
```

- [ ] **Step 2: Read replica count from env in `AddDigitalBrain`**

In `DigitalBrain.Aspire/DigitalBrainBuilderExtensions.cs`, in `AddDigitalBrain`, after `configure?.Invoke(options);` (line 41), allow an env override:

```csharp
        if (int.TryParse(Environment.GetEnvironmentVariable("DIGITALBRAIN_KERNEL_REPLICAS"), out var replicaOverride) && replicaOverride > 0)
        {
            options.KernelReplicas = replicaOverride;
        }
```

- [ ] **Step 3: Add the external web endpoint in `WireKernelSilo`**

In `DigitalBrain.Aspire/DigitalBrainBuilderExtensions.cs`, in `WireKernelSilo`, extend the builder chain (after the existing `.WithEndpoint(name: "grpc", ...)` on line 108):

```csharp
            .WithEndpoint(name: "web", scheme: "http", env: "DIGITALBRAIN_WEB_PORT", isProxied: true)
            .WithExternalHttpEndpoints()
```

- [ ] **Step 4: Build and confirm the AppHost graph compiles**

Run: `dotnet build NeuroOSPrototype.AppHost/NeuroOSPrototype.AppHost.csproj -c Debug`
Expected: 0 errors. (Endpoint/replica wiring is exercised by the Task 6 E2E.)

- [ ] **Step 5: Commit**

```bash
git add DigitalBrain.Kernel/Program.cs DigitalBrain.Aspire/DigitalBrainBuilderExtensions.cs
git commit -m "feat(aspire): dedicated HTTP/1 web endpoint on kernel + env-driven replica count"
```

---

### Task 5: Fix + gate the E2E fixture

Fix the `silo`→`kernel` hang with a bounded timeout, tag the collection `E2E` (fixing the Bucket A filter leak), wire the web bundle + single replica, and add opt-in/prereq skipping.

**Files:**
- Modify: `Directory.Packages.props` (add `Xunit.SkippableFact`)
- Modify: `DigitalBrain.Tests/DigitalBrain.Tests.csproj` (reference it)
- Modify: `DigitalBrain.Tests/E2E/DigitalBrainAppHostFixture.cs` (resource name, bounded wait, env wiring)
- Modify: `DigitalBrain.Tests/E2E/DigitalBrainE2ECollection.cs` (add `[Trait]`)
- Create: `DigitalBrain.Tests/E2E/E2EPrerequisites.cs` (skip helper)

**Interfaces:**
- Consumes: `Aspire.Hosting.Testing`, env vars set by the fixture.
- Produces: `E2EPrerequisites.WebBundleDir` (absolute `app/build/web`), `E2EPrerequisites.RequireRenderE2E()` (calls `Skip.IfNot(...)`); fixture sets `DIGITALBRAIN_KERNEL_REPLICAS=1` and `DIGITALBRAIN_WEBROOT=<app/build/web>`; the E2E collection is tagged `[Trait("Category","E2E")]`.

- [ ] **Step 1: Add the SkippableFact package**

In `Directory.Packages.props` (Testing group): `<PackageVersion Include="Xunit.SkippableFact" Version="1.5.23" />` (use the latest that restores).
In `DigitalBrain.Tests/DigitalBrain.Tests.csproj`: `<PackageReference Include="Xunit.SkippableFact" />`

- [ ] **Step 2: Add the prerequisites helper**

Create `DigitalBrain.Tests/E2E/E2EPrerequisites.cs`:

```csharp
using System;
using System.IO;
using Xunit;

namespace DigitalBrain.Tests.E2E;

// Locates the prebuilt Flutter web bundle and gates the render E2E so it only runs deliberately.
public static class E2EPrerequisites
{
    public static string WebBundleDir =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "app", "build", "web"));

    public static bool OptedIn =>
        string.Equals(Environment.GetEnvironmentVariable("RUN_FLUTTER_E2E"), "true", StringComparison.OrdinalIgnoreCase);

    public static bool WebBundlePresent => File.Exists(Path.Combine(WebBundleDir, "index.html"));

    public static void RequireRenderE2E()
    {
        Skip.IfNot(OptedIn, "Set RUN_FLUTTER_E2E=true to run the Flutter render E2E.");
        Skip.IfNot(WebBundlePresent,
            $"Flutter web bundle not found at {WebBundleDir}. Build it first: " +
            "cd app && flutter build web --release --dart-define=DIGITALBRAIN_E2E=true");
    }
}
```

(Confirm the relative depth from `bin/Debug/net11.0` up to the workspace root, then into `app/build/web`; adjust the number of `..` if the test assembly path differs.)

- [ ] **Step 3: Tag the E2E collection**

In `DigitalBrain.Tests/E2E/DigitalBrainE2ECollection.cs`:

```csharp
using Xunit;

namespace DigitalBrain.Tests.E2E;

[Trait("Category", "E2E")]
[CollectionDefinition(nameof(DigitalBrainE2ECollection))]
public sealed class DigitalBrainE2ECollection : ICollectionFixture<DigitalBrainBrowserFixture>
{
}
```

Also add `[Trait("Category", "E2E")]` to the test class `PackEmbodimentRendersE2ETests` (traits on a collection definition do not always propagate to test discovery filters; tagging the class guarantees `Category!=E2E` excludes it).

- [ ] **Step 4: Fix the fixture (resource name, bounded wait, env wiring)**

In `DigitalBrain.Tests/E2E/DigitalBrainAppHostFixture.cs`, in `InitializeAsync`:

Before `var builder = await DistributedApplicationTestingBuilder.CreateAsync(programType);` add:

```csharp
        Environment.SetEnvironmentVariable("DIGITALBRAIN_KERNEL_REPLICAS", "1");
        Environment.SetEnvironmentVariable("DIGITALBRAIN_WEBROOT", E2EPrerequisites.WebBundleDir);
```

Replace the wait block (lines 43-44) with a bounded wait on the correct resource name:

```csharp
        using var startupCts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await App.ResourceNotifications.WaitForResourceHealthyAsync("gateway", startupCts.Token);
        await App.ResourceNotifications.WaitForResourceHealthyAsync("kernel", startupCts.Token);
```

Update the endpoint-resolution fallbacks (lines 47-64) to prefer the kernel `web` endpoint (the browser origin):

```csharp
        string url = "https://localhost:8080";
        try { url = App.GetEndpoint("kernel", "web").ToString(); }
        catch
        {
            try { url = App.GetEndpoint("gateway", "https").ToString(); }
            catch { try { url = App.GetEndpoint("gateway", "http").ToString(); } catch { } }
        }
        GatewayHttpsUrl = url;
```

(Add `using System.Threading;` if not present.)

- [ ] **Step 5: Verify the default suite no longer selects the E2E collection**

Run: `dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Debug --filter "Category!=E2E" --list-tests`
Expected: the listing does NOT include `PackEmbodimentRendersE2ETests` or any `DigitalBrain.Tests.E2E.*` test. (If `--list-tests` is unavailable, run the full `Category!=E2E` suite and confirm it completes without booting the AppHost — no `Aspire.Hosting` log lines, no hang.)

- [ ] **Step 6: Commit**

```bash
git add Directory.Packages.props DigitalBrain.Tests/DigitalBrain.Tests.csproj DigitalBrain.Tests/E2E/E2EPrerequisites.cs DigitalBrain.Tests/E2E/DigitalBrainE2ECollection.cs DigitalBrain.Tests/E2E/DigitalBrainAppHostFixture.cs
git commit -m "test(e2e): fix silo->kernel hang, tag E2E collection, gate render E2E on opt-in + prebuilt bundle"
```

---

### Task 6: The real-pack browser render E2E

Replace the skipped stub with a real, gated test: publish + install a real `IPackBehavior` pack whose embodiment emits a renderable surface, then drive Chromium to assert the rendered widget by its semantics identifier.

**Files:**
- Modify: `DigitalBrain.Tests/E2E/PackEmbodimentRendersE2ETests.cs` (replace the skipped test)

**Interfaces:**
- Consumes: `DigitalBrainBrowserFixture` (`Page`, `GatewayHttpsUrl`, `PublishPackAsync`, `InstallPackAsync`, `SendSynapseAsync`, `CreateGatewayGrpcChannel`), `E2EPrerequisites.RequireRenderE2E()`.
- Produces: nothing downstream.

- [ ] **Step 1: Confirm the renderable-surface contract**

Read `app/lib/features/canvas/living_canvas_screen.dart` `_isRenderableSurface` and `DigitalBrain.Kernel/Ui/UiSurfaceRfwBridge.cs`. The pack must emit a surface that becomes an `RfwCard` the app will render (non-empty `dataJson`, an RFW `source` or ui-layout tree, not `synapse-broadcast`) and whose live-render `docKey`/surface id is a known marker (e.g. `"pack-surface-e2e"`). Confirm the synapse the pack handles to emit on trigger.

- [ ] **Step 2: Write the gated render test**

Replace the body of `DigitalBrain.Tests/E2E/PackEmbodimentRendersE2ETests.cs` test method (remove `Skip = "..."`, use `[SkippableFact]`):

```csharp
using Microsoft.Playwright;
using Xunit;

namespace DigitalBrain.Tests.E2E;

[Trait("Category", "E2E")]
[Collection(nameof(DigitalBrainE2ECollection))]
public sealed class PackEmbodimentRendersE2ETests(DigitalBrainBrowserFixture fixture)
{
    private readonly DigitalBrainBrowserFixture _fx = fixture;

    [SkippableFact]
    public async Task InstallsRealPack_EmbodiedCode_RendersSurface_ObservedInFlutter()
    {
        E2EPrerequisites.RequireRenderE2E();

        const string packName = "E2ESurfacePack";
        const string version = "1.0";
        const string surfaceId = "pack-surface-e2e";

        // Real pack: embodies and, on trigger, emits a renderable UiSurface carrying surfaceId.
        await _fx.PublishPackAsync(packName, version,
            code: TestPacks.RenderableSurfacePack(surfaceId),
            description: "E2E pack that emits a renderable surface");
        await _fx.InstallPackAsync(packName, version, buyer: "e2e-ui-watcher");
        await _fx.SendSynapseAsync("DigitalBrain.Kernel.SurfaceDemoRequested",
            $"{{\"source\":\"{surfaceId}\"}}", "e2e-" + surfaceId);

        await _fx.Page.GotoAsync(_fx.GatewayHttpsUrl, new() { WaitUntil = WaitUntilState.Load });

        var node = _fx.Page.Locator($"[flt-semantics-identifier=\"{surfaceId}\"]");
        await node.WaitForAsync(new() { Timeout = 30_000 });
        Assert.Equal(1, await node.CountAsync());

        var shot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"e2e-render-{surfaceId}.png");
        await _fx.Page.ScreenshotAsync(new() { Path = shot });
    }
}
```

If a real pack that emits a renderable surface on `SurfaceDemoRequested` does not yet exist, add a `TestPacks.RenderableSurfacePack(string surfaceId)` helper returning pack C# (an `IPackBehavior` whose handler fires a `UiSurface` whose `dataJson` carries `source = surfaceId` and an RFW layout). Otherwise trigger the existing surface-demo pack and set `surfaceId` to the id it emits.

- [ ] **Step 3: Build a Flutter web bundle for the run**

Run (in `E:\digitalbraintech\app`): `flutter build web --release --dart-define=DIGITALBRAIN_E2E=true`
Expected: `app/build/web/index.html` exists.

- [ ] **Step 4: Run the gated E2E (prereqs present)**

Ensure Playwright browsers are installed (`pwsh DigitalBrain.Tests/bin/Debug/net11.0/playwright.ps1 install chromium`).
Run: `RUN_FLUTTER_E2E=true dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Debug --filter "Category=E2E" --logger "console;verbosity=minimal"`
Expected: the render test PASSES (Docker + AppHost boot to 1 kernel replica; browser asserts the semantics node). Requires Docker running.

- [ ] **Step 5: Confirm it skips cleanly without opt-in**

Run: `dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Debug --filter "Category=E2E" --logger "console;verbosity=minimal"` (no `RUN_FLUTTER_E2E`)
Expected: the render test is reported **Skipped** with the remediation message — no hang, no AppHost boot.

- [ ] **Step 6: Commit**

```bash
git add DigitalBrain.Tests/E2E/PackEmbodimentRendersE2ETests.cs
git commit -m "test(e2e): real pack-embodiment browser render assertion via Flutter semantics"
```

---

### Task 7: Combined verification + docs

**Files:**
- Modify: `brain/CONTINUITY.md` (append a Bucket D entry)

- [ ] **Step 1: Default high-sev suite stays green and excludes E2E**

Run: `dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Debug --filter "Category!=E2E&FullyQualifiedName!~Browser&FullyQualifiedName!~E2E" --logger "console;verbosity=minimal" --blame-hang-timeout 4m`
Expected: all green (≥136 now: prior 134 + the 2 new Phase-1 tests), no E2E selected, no hang.

- [ ] **Step 2: `aspire doctor`**

Use the aspire MCP `doctor` tool. Expected: all checks pass.

- [ ] **Step 3: Gated render E2E (if Docker + Flutter available)**

Run the Task 6 Step 4 command. Expected: PASS (or a clean Skip if infra absent).

- [ ] **Step 4: Append CONTINUITY note**

Add a dated Bucket D entry to `brain/CONTINUITY.md`: same-origin kernel-served bundle + gRPC-Web; Flutter semantics identifiers; fixture fixed (silo→kernel, bounded wait, Trait=E2E); single-replica AppHost for deterministic fanout; gated render E2E; verification evidence. Note the `app/` repo commit (Task 3) separately.

- [ ] **Step 5: Commit**

```bash
git add CONTINUITY.md
git commit -m "docs: record Bucket D Flutter render E2E completion + verification"
```

---

## Self-Review

**Spec coverage:**
- gRPC-Web on kernel → Task 2. ✓
- Static serving of the bundle (gated `DIGITALBRAIN_WEBROOT`) → Task 1. ✓
- RFW semantics identifiers + force-enable semantics → Task 3. ✓
- Same-origin web endpoint (HTTP/1) + single-replica determinism → Task 4. ✓
- Fixture fix (silo→kernel, bounded wait), Trait=E2E gating, opt-in/prereq skip (`Xunit.SkippableFact`) → Task 5. ✓
- Real-pack browser render assertion → Task 6. ✓
- Verification ritual + CONTINUITY → Task 7. ✓
- Phases independently verifiable (Phase 1 .NET tests; Phase 2 Flutter test; Phase 3 gated E2E). ✓

**Placeholder scan:** The only deferred specifics are explicit verification steps (Task 3 Step 1 Flutter API spellings; Task 5 Step 2 relative-path depth; Task 6 Step 1 renderable-surface contract / pack helper) — each names exactly what to confirm and where, not vague "handle it later." Package versions are concrete (`2.71.0`; `Xunit.SkippableFact 1.5.23` with "latest that restores" guidance).

**Type consistency:** `render(..., semanticsId, semanticsLabel)` matches between Task 3's implementation, its widget test, and the live call site. `DIGITALBRAIN_WEBROOT` / `DIGITALBRAIN_WEB_PORT` / `DIGITALBRAIN_KERNEL_REPLICAS` / `RUN_FLUTTER_E2E` / `DIGITALBRAIN_E2E` are used identically across kernel, Aspire wiring, fixture, and helper. `E2EPrerequisites.RequireRenderE2E()` matches between Task 5 (definition) and Task 6 (call). The `surfaceId` marker (`"pack-surface-e2e"`) is the single value threaded through pack emission → semantics identifier → Playwright selector.
