# Authoring Loop Slice 3 — Warm-Cluster Attach-With-Fallback Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A render E2E test attaches to an already-running dev kernel in a few seconds instead of always booting a brand-new Aspire stack (30-120s) — this is the slice that delivers the actual cycle-time win.

**Architecture:** `Program.cs:17-53` already has a working non-Aspire-hosted "fast path": when none of the Aspire connection-string env vars are present, the kernel binds fixed Kestrel ports — `8080` (gRPC, HTTP/2-only, cleartext) and `8081` (web + gRPC-Web + MCP, HTTP/1.1+HTTP/2) — with in-memory Orleans clustering. A developer runs `dotnet run --project DigitalBrain.Kernel` once, with `DIGITALBRAIN_WEBROOT` pointed at the built Flutter bundle, and leaves it running. `DigitalBrainAppHostFixture.InitializeAsync()` gains a probe step: before booting a fresh `DistributedApplicationTestingBuilder` app, it makes a short-timeout HTTP request to `http://localhost:8081`. If something answers, it treats that as the warm cluster — sets `GatewayHttpsUrl`/`GrpcUrl` to the fixed ports and returns with `App` left `null` — skipping the AppHost boot entirely. If nothing answers, it falls through to exactly today's fresh-boot behavior. `DisposeAsync()` already no-ops when `App` is `null` (existing code, verified below — no change needed), so the fixture never stops a process it didn't start.

**Depends on:** Slices 1 and 2 of this authoring-loop-acceleration work are assumed already merged to `master` (recommended order — see `docs/specs/2026-07-01-authoring-loop-acceleration-design.md` §7). The `InitializeAsync` line references below reflect the post-Slice-1 file (the `EnsureWebBundleFresh()` / `WebBundlePresent` reordering). If Slice 1 has not landed, re-read the current file before editing — do not blindly apply line numbers.

**Tech Stack:** .NET 11 (net11.0), `System.Net.Http.HttpClient`, `Grpc.Net.Client`, `System.Net.HttpListener` (test only).

## Global Constraints

- Target framework **net11.0**; never pin `Version="*"`.
- **No vacuous `/// <summary>`**. Self-explanatory names; small inline comments only where genuinely non-obvious.
- Tests are executable specs. **Run the relevant tests and confirm they pass before claiming a task done.**
- Look up unfamiliar library/framework APIs via **Context7** before writing code against them. (Already done for this slice: `Grpc.Net.Client`'s h2c/cleartext support was verified via Context7 before this plan was written — see the note in Task 1.)
- Work in the `brain` repo on branch `spec/authoring-loop-slice3-warm-cluster` (created off `master` at the start, after Slices 1-2 have merged).
- Relative paths; never leak user-profile paths.
- Commit messages end with: `Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>`.
- **CI must be unaffected.** No warm cluster is ever listening on port 8081 in CI, so the probe always fails there and CI takes the unchanged fresh-boot path. `aspire doctor` is **not** required — this slice does not touch the AppHost resource graph, only the test fixture that boots it.
- **Correction to the design doc:** `docs/specs/2026-07-01-authoring-loop-acceleration-design.md` §2.2 says `Http2UnencryptedSupport` should be "scoped to that gRPC channel's `SocketsHttpHandler`, not process-wide." That is not achievable — it is an `AppContext` switch, which is inherently process-wide, not a per-handler property. This plan sets it process-wide (once, only on the warm-cluster-attach path) and notes why that is safe: the switch only *permits* cleartext HTTP/2 when a channel explicitly targets an `http://` address; it does not weaken or affect any of this test process's other (HTTPS) gRPC channels.

---

## File Structure

- **Modify** `DigitalBrain.Tests/E2E/DigitalBrainAppHostFixture.cs` — add `WarmClusterWebUrl`/`WarmClusterGrpcUrl` constants, an `internal static ProbeAsync(string url, TimeSpan timeout)` helper, and the probe-and-attach branch in `InitializeAsync()`.
- **Create** `DigitalBrain.Tests/E2E/DigitalBrainAppHostFixtureProbeTests.cs` — fast tests for `ProbeAsync` against a real local listener (no Aspire, no Kernel process needed).
- **Modify** `docs/authoring-a-bundle.md` — add the "Warm dev cluster" subsection with the `dotnet run --project DigitalBrain.Kernel` startup instructions.

---

## Task 1: `ProbeAsync` + the attach branch in `InitializeAsync`

**Files:**
- Modify: `DigitalBrain.Tests/E2E/DigitalBrainAppHostFixture.cs`
- Test: `DigitalBrain.Tests/E2E/DigitalBrainAppHostFixtureProbeTests.cs`

**Interfaces:**
- Consumes: nothing new from other tasks in this slice (Task 2 is documentation-only).
- Produces: `DigitalBrainAppHostFixture.ProbeAsync(string url, TimeSpan timeout) : Task<bool>` (internal static), `DigitalBrainAppHostFixture.WarmClusterWebUrl`/`WarmClusterGrpcUrl : string` (internal const).

- [ ] **Step 1: Write the failing `ProbeAsync` tests**

Create `DigitalBrain.Tests/E2E/DigitalBrainAppHostFixtureProbeTests.cs`:

```csharp
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Xunit;

namespace DigitalBrain.Tests.E2E;

public class DigitalBrainAppHostFixtureProbeTests
{
    [Fact]
    public async Task ProbeAsync_returns_true_when_something_is_listening()
    {
        var port = GetFreeTcpPort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();

        try
        {
            var acceptTask = listener.GetContextAsync();
            var probeTask = DigitalBrainAppHostFixture.ProbeAsync($"http://localhost:{port}/", TimeSpan.FromSeconds(2));

            var context = await acceptTask;
            context.Response.StatusCode = 200;
            context.Response.Close();

            Assert.True(await probeTask);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task ProbeAsync_returns_false_when_nothing_is_listening()
    {
        var port = GetFreeTcpPort();

        var result = await DigitalBrainAppHostFixture.ProbeAsync($"http://localhost:{port}/", TimeSpan.FromMilliseconds(500));

        Assert.False(result);
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd /e/digitalbraintech/brain && dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~DigitalBrainAppHostFixtureProbeTests"`
Expected: FAIL — compile error, `DigitalBrainAppHostFixture.ProbeAsync` does not exist.

- [ ] **Step 3: Add `ProbeAsync` and the warm-cluster constants**

In `DigitalBrain.Tests/E2E/DigitalBrainAppHostFixture.cs`, add `using System.Net.Http;` to the top of the file (it is not implicitly available — this project's implicit usings do not include `System.Net.Http`, only the `Microsoft.NET.Sdk.Web` SDK gets that one for free), then add these members to the `DigitalBrainAppHostFixture` class, immediately after the `GrpcUrl` property (line 25):

```csharp
    // The bare-Kernel non-Aspire-hosted fast path (Program.cs's isAspireHosted=false branch): fixed
    // Kestrel ports, in-memory Orleans clustering. A developer runs
    // `dotnet run --project DigitalBrain.Kernel` (DIGITALBRAIN_WEBROOT set) and leaves it running;
    // InitializeAsync attaches to it instead of booting a fresh ~30-120s Aspire stack.
    internal const string WarmClusterWebUrl = "http://localhost:8081";
    internal const string WarmClusterGrpcUrl = "http://localhost:8080";

    internal static async Task<bool> ProbeAsync(string url, TimeSpan timeout)
    {
        using var client = new HttpClient { Timeout = timeout };
        try
        {
            using var response = await client.GetAsync(url);
            return true;
        }
        catch
        {
            return false;
        }
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd /e/digitalbraintech/brain && dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~DigitalBrainAppHostFixtureProbeTests"`
Expected: PASS (2 passed).

- [ ] **Step 5: Wire the attach branch into `InitializeAsync`**

In `DigitalBrain.Tests/E2E/DigitalBrainAppHostFixture.cs`, find the guard block (post-Slice-1):

```csharp
        if (!E2EPrerequisites.WebBundlePresent)
            return; // Still absent after the best-effort auto-build (e.g. Flutter not installed); the [SkippableFact] will skip.
```

Insert this immediately after it, before the `var testId = ...` line that starts the fresh-boot path:

```csharp

        if (await ProbeAsync(WarmClusterWebUrl, TimeSpan.FromSeconds(2)))
        {
            // Port 8080 is HTTP/2-only cleartext (h2c) -- the .NET gRPC client needs this switch to call
            // it without TLS. This is a process-wide AppContext switch (there is no per-handler
            // equivalent); it only permits cleartext HTTP/2 for channels that explicitly target an
            // http:// address, so it does not affect this process's other (HTTPS) gRPC channels.
            AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
            GatewayHttpsUrl = WarmClusterWebUrl;
            GrpcUrl = WarmClusterGrpcUrl;
            return; // App stays null: attached to a warm cluster we don't own, nothing to boot or dispose.
        }
```

- [ ] **Step 6: Verify `DisposeAsync` is already safe (no edit needed)**

Read `DigitalBrain.Tests/E2E/DigitalBrainAppHostFixture.cs`'s `DisposeAsync` method and confirm it already reads:

```csharp
    public virtual async Task DisposeAsync()
    {
        if (App is not null)
        {
            await App.StopAsync();
            await App.DisposeAsync();
        }
    }
```

This is already correct for the attach path (`App` stays `null`, so `DisposeAsync` is a no-op) — do not change it. If it does **not** already look like this, add the `if (App is not null)` guard before making any further changes.

- [ ] **Step 7: Run the full fast suite to confirm no regression**

Run: `cd /e/digitalbraintech/brain && dotnet test DigitalBrain.Tests --filter "FullyQualifiedName!~E2E"`
Expected: all pass, same count as before this change.

- [ ] **Step 8: Commit**

```bash
cd /e/digitalbraintech/brain
git add DigitalBrain.Tests/E2E/DigitalBrainAppHostFixture.cs DigitalBrain.Tests/E2E/DigitalBrainAppHostFixtureProbeTests.cs
git commit -m "$(cat <<'MSG'
feat(tests): attach render E2E tests to a warm dev cluster when present

DigitalBrainAppHostFixture.InitializeAsync probes the bare-Kernel fast
path's fixed ports (8080 grpc, 8081 web) before booting a fresh Aspire
stack. A long-lived `dotnet run --project DigitalBrain.Kernel` process
is picked up automatically -- render tests attach in seconds instead
of the usual 30-120s boot. Falls back to today's fresh-boot behavior
whenever nothing is listening there, so CI (and anyone not running a
warm cluster) is unaffected.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
MSG
)"
```

---

## Task 2: Document how to start the warm cluster

**Files:**
- Modify: `docs/authoring-a-bundle.md`

**Interfaces:**
- Consumes: nothing new (documents Task 1's behavior).
- Produces: nothing.

- [ ] **Step 1: Add the "Warm dev cluster" subsection**

In `docs/authoring-a-bundle.md`, immediately after the "Render loop" section's prerequisites block and VS/CLI instructions (the content Slice 2 added), append:

```markdown
### Warm dev cluster (fastest — skips the 30-120s Aspire boot)

Start a long-lived kernel once, outside Aspire, and every render test attaches to it instead of
booting a fresh cluster:

```sh
cd brain
DIGITALBRAIN_WEBROOT=$(pwd)/../app/build/web dotnet run --project DigitalBrain.Kernel
```

(PowerShell: `$env:DIGITALBRAIN_WEBROOT = (Resolve-Path ../app/build/web); dotnet run --project DigitalBrain.Kernel`)

Leave it running. Render tests probe `http://localhost:8081` at startup; if it responds, they
attach directly (a few seconds) instead of booting a fresh Aspire stack. If nothing is listening
there, tests fall back to today's behavior automatically — there is nothing to configure to opt
out.

State is in-memory. If a dev session's state ever gets confusing, just restart the process —
there is no persisted store to clean up.
```

- [ ] **Step 2: Commit**

```bash
cd /e/digitalbraintech/brain
git add docs/authoring-a-bundle.md
git commit -m "$(cat <<'MSG'
docs: document the warm dev cluster startup command

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
MSG
)"
```

---

## Final verification

- [ ] **Build the whole solution**

Run: `cd /e/digitalbraintech/brain && dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Run the fast suite (CI's exact filter)**

Run: `cd /e/digitalbraintech/brain && dotnet test DigitalBrain.Tests --filter "FullyQualifiedName!~E2E"`
Expected: all pass, including the two new `DigitalBrainAppHostFixtureProbeTests`.

- [ ] **Confirm the fallback path still works (no warm cluster running)**

Run: `cd /e/digitalbraintech/brain && RUN_FLUTTER_E2E=true dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~HelloWorldRendersE2ETests"`
Expected: PASS, same as before this slice (a fresh Aspire stack boots, since nothing is listening on port 8081).

- [ ] **Manual verification of the actual cycle-time win (the point of this slice)**

In one terminal: `cd brain && DIGITALBRAIN_WEBROOT=<absolute path to app/build/web> dotnet run --project DigitalBrain.Kernel` — leave it running.
In another terminal: `RUN_FLUTTER_E2E=true dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~HelloWorldRendersE2ETests"` twice in a row.
Expected: both runs complete in low single-digit seconds (not 30-120s), and the second run's timing confirms the attach path was taken both times (no Aspire/Ollama/Azurite startup logs in the test output).

> `aspire doctor` is **not** required for this slice — no AppHost resource-graph change; test-project code and docs only.

## Out of scope (tracked follow-ups, not this slice)

- Flutter-side auto-refresh for a human manually browsing the app (`SurfaceDemoScreen`) — separate interactive-exploration gap, not part of the automated render-test loop.
- AI-generation-from-spec wired into the authoring loop (`CodeFoundryClosedLoopNeuron` / MCP tools) — next roadmap item after this ships, per the design doc.
- The `FilterMarketplace` missing-from-`JournalJsonContext` bug found during research — unrelated, should be its own small fix.
