# Authoring Loop Slice 1 — Auto-Build-If-Stale Flutter Bundle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A developer running the render E2E loop never has to remember to manually run `flutter build web ...` — the bundle is built automatically when missing or stale, and left alone (no rebuild cost) when already fresh.

**Architecture:** `E2EPrerequisites` gains a content fingerprint (file path + last-write-time + length over every `app/lib/**/*.dart` file plus `app/pubspec.lock`, hashed with SHA-256) stored alongside the built bundle. `DigitalBrainAppHostFixture.InitializeAsync()` calls a new `EnsureWebBundleFresh()` *before* checking whether the bundle is present — today it checks presence first, which would skip the test before an auto-build ever got a chance to run for a first-time developer. If the fingerprint is missing or doesn't match, it best-effort shells out to `flutter build web`; a build failure or missing Flutter SDK falls through to the existing `Skip.IfNot(WebBundlePresent, ...)` messaging unchanged — this is purely additive convenience, never a new hard failure mode.

**Tech Stack:** .NET 11 (net11.0), `System.Diagnostics.Process`, `System.Security.Cryptography.SHA256`, xUnit + `Xunit.SkippableFact` (existing).

## Global Constraints

- Target framework **net11.0**; never pin `Version="*"`; package versions are central in `Directory.Packages.props`.
- **No vacuous `/// <summary>`** that restates a signature. Self-explanatory names; small inline comments only where genuinely non-obvious.
- Tests are executable specs. **Run the relevant tests and confirm they pass before claiming a task done** — evidence before assertions.
- Look up unfamiliar library/framework APIs via **Context7** before writing code against them.
- Work in the `brain` repo on branch `spec/authoring-loop-slice1-flutter-freshness` (created off `master` at the start; `master` already has the design doc at `docs/specs/2026-07-01-authoring-loop-acceleration-design.md`).
- Relative paths; never leak user-profile paths.
- Commit messages end with: `Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>`.
- This slice does **not** touch the AppHost resource graph — `aspire doctor` is not required for verification, only `dotnet build` + the relevant `dotnet test` filters.

---

## File Structure

- **Modify** `DigitalBrain.Tests/E2E/E2EPrerequisites.cs` — add `AppDir`, `ComputeSourceFingerprint(string appDir)`, `IsWebBundleStale(string appDir, string webBundleDir)`, `EnsureWebBundleFresh()`, and the private `TryRunFlutterBuild(string appDir, out string output)` process-launch helper. `WebBundleDir`, `OptedIn`, `WebBundlePresent`, `RequireRenderE2E()` stay as they are today.
- **Modify** `DigitalBrain.Tests/E2E/DigitalBrainAppHostFixture.cs` — in `InitializeAsync()`, call `E2EPrerequisites.EnsureWebBundleFresh()` between the `OptedIn` check and the `WebBundlePresent` check (today both are checked together in one `if`, before any build could happen).
- **Create** `DigitalBrain.Tests/E2E/E2EPrerequisitesFreshnessTests.cs` — fast, pure-function tests for the fingerprint/staleness logic against a temp directory. No Flutter SDK or network required.

---

## Task 1: Fingerprint, staleness check, and auto-build wiring

**Files:**
- Modify: `DigitalBrain.Tests/E2E/E2EPrerequisites.cs`
- Modify: `DigitalBrain.Tests/E2E/DigitalBrainAppHostFixture.cs:27-30`
- Test: `DigitalBrain.Tests/E2E/E2EPrerequisitesFreshnessTests.cs`

**Interfaces:**
- Consumes: nothing new from other tasks (this is the only task in this slice).
- Produces: `E2EPrerequisites.AppDir : string`, `E2EPrerequisites.ComputeSourceFingerprint(string appDir) : string` (internal), `E2EPrerequisites.IsWebBundleStale(string appDir, string webBundleDir) : bool` (internal), `E2EPrerequisites.EnsureWebBundleFresh() : void` (public — called by the fixture).

- [ ] **Step 1: Write the failing fingerprint/staleness tests**

Create `DigitalBrain.Tests/E2E/E2EPrerequisitesFreshnessTests.cs`:

```csharp
using System.IO;
using Xunit;

namespace DigitalBrain.Tests.E2E;

public class E2EPrerequisitesFreshnessTests
{
    [Fact]
    public void Fingerprint_is_stable_for_unchanged_files()
    {
        var appDir = CreateTempAppDir();
        try
        {
            var first = E2EPrerequisites.ComputeSourceFingerprint(appDir);
            var second = E2EPrerequisites.ComputeSourceFingerprint(appDir);

            Assert.Equal(first, second);
        }
        finally
        {
            Directory.Delete(appDir, recursive: true);
        }
    }

    [Fact]
    public void Fingerprint_changes_when_a_dart_file_is_touched()
    {
        var appDir = CreateTempAppDir();
        try
        {
            var before = E2EPrerequisites.ComputeSourceFingerprint(appDir);

            var mainDart = Path.Combine(appDir, "lib", "main.dart");
            File.WriteAllText(mainDart, "// changed");
            File.SetLastWriteTimeUtc(mainDart, DateTime.UtcNow.AddMinutes(1));

            var after = E2EPrerequisites.ComputeSourceFingerprint(appDir);

            Assert.NotEqual(before, after);
        }
        finally
        {
            Directory.Delete(appDir, recursive: true);
        }
    }

    [Fact]
    public void Bundle_is_stale_when_no_fingerprint_file_exists()
    {
        var appDir = CreateTempAppDir();
        var webBundleDir = Path.Combine(appDir, "build", "web");
        Directory.CreateDirectory(webBundleDir);
        try
        {
            Assert.True(E2EPrerequisites.IsWebBundleStale(appDir, webBundleDir));
        }
        finally
        {
            Directory.Delete(appDir, recursive: true);
        }
    }

    [Fact]
    public void Bundle_is_not_stale_when_stored_fingerprint_matches_current_source()
    {
        var appDir = CreateTempAppDir();
        var webBundleDir = Path.Combine(appDir, "build", "web");
        Directory.CreateDirectory(webBundleDir);
        File.WriteAllText(
            Path.Combine(webBundleDir, ".source-fingerprint"),
            E2EPrerequisites.ComputeSourceFingerprint(appDir));
        try
        {
            Assert.False(E2EPrerequisites.IsWebBundleStale(appDir, webBundleDir));
        }
        finally
        {
            Directory.Delete(appDir, recursive: true);
        }
    }

    private static string CreateTempAppDir()
    {
        var appDir = Path.Combine(Path.GetTempPath(), "dbt-freshness-" + Path.GetRandomFileName());
        var libDir = Path.Combine(appDir, "lib");
        Directory.CreateDirectory(libDir);
        File.WriteAllText(Path.Combine(libDir, "main.dart"), "void main() {}");
        File.WriteAllText(Path.Combine(appDir, "pubspec.lock"), "packages: {}");
        return appDir;
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd /e/digitalbraintech/brain && dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~E2EPrerequisitesFreshnessTests"`
Expected: FAIL — compile error, `ComputeSourceFingerprint`/`IsWebBundleStale` do not exist on `E2EPrerequisites`.

- [ ] **Step 3: Implement the fingerprint and staleness check**

Replace the full contents of `DigitalBrain.Tests/E2E/E2EPrerequisites.cs` with:

```csharp
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace DigitalBrain.Tests.E2E;

// Locates the prebuilt Flutter web bundle and gates the render E2E so it only runs deliberately.
public static class E2EPrerequisites
{
    public static string WebBundleDir =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "app", "build", "web"));

    public static string AppDir => Path.GetFullPath(Path.Combine(WebBundleDir, "..", ".."));

    public static bool OptedIn =>
        string.Equals(Environment.GetEnvironmentVariable("RUN_FLUTTER_E2E"), "true", StringComparison.OrdinalIgnoreCase);

    public static bool WebBundlePresent => File.Exists(Path.Combine(WebBundleDir, "index.html"));

    private static string FingerprintFile(string webBundleDir) => Path.Combine(webBundleDir, ".source-fingerprint");

    // Metadata fingerprint (path + last-write-time + length), not a content hash — fast enough to run on
    // every test invocation and sufficient because editors update mtime on save.
    internal static string ComputeSourceFingerprint(string appDir)
    {
        var sb = new StringBuilder();
        var libDir = Path.Combine(appDir, "lib");
        var dartFiles = Directory.Exists(libDir)
            ? Directory.EnumerateFiles(libDir, "*.dart", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal)
            : Enumerable.Empty<string>();

        foreach (var file in dartFiles)
        {
            var info = new FileInfo(file);
            sb.Append(Path.GetRelativePath(appDir, file)).Append('|')
              .Append(info.LastWriteTimeUtc.Ticks).Append('|')
              .Append(info.Length).Append(';');
        }

        var pubspecLock = Path.Combine(appDir, "pubspec.lock");
        if (File.Exists(pubspecLock))
        {
            var info = new FileInfo(pubspecLock);
            sb.Append("pubspec.lock|").Append(info.LastWriteTimeUtc.Ticks).Append('|').Append(info.Length);
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    internal static bool IsWebBundleStale(string appDir, string webBundleDir)
    {
        var fingerprintPath = FingerprintFile(webBundleDir);
        if (!File.Exists(fingerprintPath)) return true;

        var stored = File.ReadAllText(fingerprintPath).Trim();
        return !string.Equals(stored, ComputeSourceFingerprint(appDir), StringComparison.Ordinal);
    }

    // Best-effort: builds the Flutter web bundle when missing or stale. Never throws — a missing Flutter
    // SDK or a build failure just leaves the existing (possibly absent) bundle in place, and
    // RequireRenderE2E()'s existing Skip.IfNot(WebBundlePresent, ...) reports the actionable message.
    public static void EnsureWebBundleFresh()
    {
        if (WebBundlePresent && !IsWebBundleStale(AppDir, WebBundleDir))
            return;

        if (!TryRunFlutterBuild(AppDir, out var output))
        {
            Console.WriteLine($"[E2EPrerequisites] Auto-build of the Flutter web bundle did not complete; " +
                               $"falling back to the existing bundle if present.\n{output}");
            return;
        }

        Directory.CreateDirectory(WebBundleDir);
        File.WriteAllText(FingerprintFile(WebBundleDir), ComputeSourceFingerprint(AppDir));
    }

    private static bool TryRunFlutterBuild(string appDir, out string output)
    {
        var psi = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "flutter.bat" : "flutter",
            Arguments = "build web --release --no-tree-shake-icons --dart-define=DIGITALBRAIN_E2E=true",
            WorkingDirectory = appDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        Process? process;
        try
        {
            process = Process.Start(psi);
        }
        catch (Exception ex)
        {
            output = $"Could not start flutter: {ex.Message}";
            return false;
        }

        if (process is null)
        {
            output = "Could not start the flutter process.";
            return false;
        }

        // Start draining both streams before blocking on exit so a chatty build can't deadlock on a full pipe buffer.
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(TimeSpan.FromMinutes(5)))
        {
            process.Kill(entireProcessTree: true);
            output = "flutter build web timed out after 5 minutes.";
            return false;
        }

        output = stdoutTask.GetAwaiter().GetResult() + stderrTask.GetAwaiter().GetResult();
        return process.ExitCode == 0;
    }

    public static void RequireRenderE2E()
    {
        Skip.IfNot(OptedIn, "Set RUN_FLUTTER_E2E=true to run the Flutter render E2E.");
        Skip.IfNot(WebBundlePresent,
            $"Flutter web bundle not found at {WebBundleDir}. Build it first: " +
            "cd app && flutter build web --release --no-tree-shake-icons --dart-define=DIGITALBRAIN_E2E=true " +
            "(--no-tree-shake-icons is required: the app uses non-constant IconData).");
    }
}
```

- [ ] **Step 4: Run the fingerprint/staleness tests to verify they pass**

Run: `cd /e/digitalbraintech/brain && dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~E2EPrerequisitesFreshnessTests"`
Expected: PASS (4 passed).

- [ ] **Step 5: Wire the auto-build into the fixture, before the presence check**

In `DigitalBrain.Tests/E2E/DigitalBrainAppHostFixture.cs`, replace lines 27-30:

```csharp
    public virtual async Task InitializeAsync()
    {
        if (!(E2EPrerequisites.OptedIn && E2EPrerequisites.WebBundlePresent))
            return; // Prereqs absent: the [SkippableFact] will skip; don't boot the AppHost.
```

with:

```csharp
    public virtual async Task InitializeAsync()
    {
        if (!E2EPrerequisites.OptedIn)
            return; // Not opted into the render E2E; the [SkippableFact] will skip.

        E2EPrerequisites.EnsureWebBundleFresh();

        if (!E2EPrerequisites.WebBundlePresent)
            return; // Still absent after the best-effort auto-build (e.g. Flutter not installed); the [SkippableFact] will skip.
```

This ordering matters: checking `WebBundlePresent` before `EnsureWebBundleFresh()` (the old code) would skip a first-time developer's run before an auto-build ever got the chance to create the bundle.

- [ ] **Step 6: Run the full fast suite to confirm no regression**

Run: `cd /e/digitalbraintech/brain && dotnet test DigitalBrain.Tests --filter "FullyQualifiedName!~E2E"`
Expected: all pass, same count as before this change (this slice touches no non-E2E code paths).

- [ ] **Step 7: Commit**

```bash
cd /e/digitalbraintech/brain
git add DigitalBrain.Tests/E2E/E2EPrerequisites.cs DigitalBrain.Tests/E2E/DigitalBrainAppHostFixture.cs DigitalBrain.Tests/E2E/E2EPrerequisitesFreshnessTests.cs
git commit -m "$(cat <<'MSG'
feat(tests): auto-build the Flutter web bundle when stale

E2EPrerequisites fingerprints app/lib/**/*.dart + pubspec.lock and
rebuilds the web bundle only when it's missing or the source has moved
on. DigitalBrainAppHostFixture now checks freshness before presence, so
a first-time developer's render E2E run builds the bundle instead of
skipping. Best-effort: a missing Flutter SDK or a failed build falls
through to the existing skip message unchanged.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
MSG
)"
```

---

## Final verification

- [ ] **Build the whole solution**

Run: `cd /e/digitalbraintech/brain && dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Run the fast suite (CI's exact filter) to confirm nothing outside E2E moved**

Run: `cd /e/digitalbraintech/brain && dotnet test DigitalBrain.Tests --filter "FullyQualifiedName!~E2E"`
Expected: all pass.

- [ ] **Manual spot-check (requires Flutter SDK installed locally)**

Delete `app/build/web` if it exists, then run:
`cd /e/digitalbraintech/brain && RUN_FLUTTER_E2E=true dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~HelloWorldRendersE2ETests"`
Expected: the console shows Flutter building the web bundle automatically (no manual `flutter build web` step), then the test proceeds as it does today. Re-running the same command immediately after should skip the rebuild (fingerprint unchanged) and start faster.

> `aspire doctor` is **not** required for this slice — no AppHost resource-graph change; test-project code only.

## Out of scope (later slices)

- Collapsing the env-var ceremony into one VS-runnable category (Slice 2).
- The warm-cluster attach-with-fallback that delivers the actual 30-120s → few-seconds cycle-time win (Slice 3).
