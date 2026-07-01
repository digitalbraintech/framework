# Authoring Loop Slice 2 — Collapse the Render Env-Var Ceremony Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A developer can right-click an `E2E`-tagged test in Visual Studio's Test Explorer and hit "Run" without first opening a terminal to set three environment variables (`RUN_FLUTTER_E2E`, `DIGITALBRAIN_E2E_HEADED`, `FAST_UI_E2E`).

**Architecture:** `DigitalBrain.Tests` already reads these as plain process environment variables (`E2EPrerequisites.cs`, `DigitalBrainBrowserFixture.cs`, `ExperienceFlowDriver.cs`). VSTest (the adapter `xunit.runner.visualstudio` runs under) supports a classic `.runsettings` file with a `<RunConfiguration><EnvironmentVariables>` block that sets environment variables on the test host process — this is the standard, documented mechanism (confirmed via Context7 against the Microsoft Testing Platform docs, which describe it as the legacy behavior their newer `testconfig.json` feature intentionally mirrors). Adding one `e2e.runsettings` file at the repo root, and pointing VS at it once via *Test > Configure Run Settings > Select Solution Wide runsettings File*, means every subsequent Test Explorer run of an `E2E`-tagged test already has the render loop opted in. CI is unaffected: it invokes `dotnet test` directly with `--filter "FullyQualifiedName!~E2E"` and never references this file.

**Tech Stack:** .NET 11 (net11.0), VSTest `.runsettings` (`RunConfiguration/EnvironmentVariables`), `System.Xml.Linq` (for the regression-guard test).

## Global Constraints

- Target framework **net11.0**; never pin `Version="*"`.
- **No vacuous `/// <summary>`**. Self-explanatory names; small inline comments only where genuinely non-obvious.
- Tests are executable specs. **Run the relevant tests and confirm they pass before claiming a task done.**
- Look up unfamiliar library/framework APIs via **Context7** before writing code against them. (Already done for this slice: the `.runsettings` `RunConfiguration/EnvironmentVariables` schema was verified against the Microsoft Testing Platform docs before this plan was written.)
- Work in the `brain` repo on branch `spec/authoring-loop-slice2-render-runsettings` (created off `master` at the start).
- Relative paths; never leak user-profile paths.
- Commit messages end with: `Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>`.
- **CI must not change behavior.** `.github/workflows/ci.yml:23` runs `dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release --filter "FullyQualifiedName!~E2E"` with no `--settings` flag — do not add one. This slice must not require any CI file edit.
- This slice does **not** touch the AppHost resource graph — `aspire doctor` is not required.

---

## File Structure

- **Create** `brain/e2e.runsettings` — the solution-wide run settings file declaring `RUN_FLUTTER_E2E=true` and `FAST_UI_E2E=1` as test-host environment variables. (`DIGITALBRAIN_E2E_HEADED` is deliberately **not** set here — `DigitalBrainBrowserFixture.cs:38-41` already defaults to headed whenever neither `CI=true` nor `DIGITALBRAIN_E2E_HEADLESS=true` is present, which is exactly the local-VS case; setting it explicitly would be redundant.)
- **Create** `DigitalBrain.Tests/E2E/RenderRunSettingsTests.cs` — a fast regression-guard test that parses `e2e.runsettings` and asserts it declares the two keys the render loop actually reads, so a future accidental edit/typo fails fast instead of silently breaking the one-click loop.
- **Modify** `docs/authoring-a-bundle.md` — replace the "Useful env flags" instructions with the VS Test Explorer one-time setup plus the `dotnet test --settings` CLI equivalent.

---

## Task 1: `e2e.runsettings` + a regression-guard test

**Files:**
- Create: `brain/e2e.runsettings`
- Test: `DigitalBrain.Tests/E2E/RenderRunSettingsTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: nothing other tasks depend on — Task 2 only edits documentation.

- [ ] **Step 1: Write the failing test**

Create `DigitalBrain.Tests/E2E/RenderRunSettingsTests.cs`:

```csharp
using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace DigitalBrain.Tests.E2E;

public class RenderRunSettingsTests
{
    private static string RunSettingsPath =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "e2e.runsettings"));

    [Fact]
    public void Runsettings_file_exists_at_the_repo_root()
    {
        Assert.True(File.Exists(RunSettingsPath), $"Expected {RunSettingsPath} to exist.");
    }

    [Fact]
    public void Runsettings_declares_the_render_loop_opt_in_and_fast_timeouts()
    {
        var doc = XDocument.Load(RunSettingsPath);
        var envVars = doc.Root?.Element("RunConfiguration")?.Element("EnvironmentVariables");

        Assert.NotNull(envVars);
        Assert.Equal("true", envVars!.Elements().FirstOrDefault(e => e.Name == "RUN_FLUTTER_E2E")?.Value);
        Assert.Equal("1", envVars.Elements().FirstOrDefault(e => e.Name == "FAST_UI_E2E")?.Value);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd /e/digitalbraintech/brain && dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~RenderRunSettingsTests"`
Expected: FAIL — `e2e.runsettings` does not exist yet.

- [ ] **Step 3: Create the run settings file**

Create `brain/e2e.runsettings`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<!--
  Solution-wide render-loop defaults for Visual Studio Test Explorer.
  Wire this up once: Test > Configure Run Settings > Select Solution Wide runsettings File > e2e.runsettings.
  After that, running any [Trait("Category", "E2E")] test from Test Explorer opts into the render loop
  automatically — no env vars to remember. CI does not reference this file (see .github/workflows/ci.yml).
-->
<RunSettings>
  <RunConfiguration>
    <EnvironmentVariables>
      <RUN_FLUTTER_E2E>true</RUN_FLUTTER_E2E>
      <FAST_UI_E2E>1</FAST_UI_E2E>
    </EnvironmentVariables>
  </RunConfiguration>
</RunSettings>
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd /e/digitalbraintech/brain && dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~RenderRunSettingsTests"`
Expected: PASS (2 passed).

- [ ] **Step 5: Confirm CI's filter is untouched**

Run: `grep -n "filter" /e/digitalbraintech/brain/.github/workflows/ci.yml`
Expected: still exactly `--filter "FullyQualifiedName!~E2E"`, no `--settings` flag added.

- [ ] **Step 6: Commit**

```bash
cd /e/digitalbraintech/brain
git add e2e.runsettings DigitalBrain.Tests/E2E/RenderRunSettingsTests.cs
git commit -m "$(cat <<'MSG'
feat(tests): e2e.runsettings collapses the render-loop env-var ceremony

One solution-wide runsettings file sets RUN_FLUTTER_E2E + FAST_UI_E2E
for the VS Test Explorer test host, so running an E2E-tagged test from
the IDE opts into the render loop without a terminal. CI is unaffected
-- it invokes dotnet test directly with its own filter, no --settings.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
MSG
)"
```

---

## Task 2: Document the one-time VS setup and the CLI equivalent

**Files:**
- Modify: `docs/authoring-a-bundle.md`

**Interfaces:**
- Consumes: `brain/e2e.runsettings` (Task 1).
- Produces: nothing — this is a documentation-only task.

- [ ] **Step 1: Replace the "Useful env flags" section**

In `docs/authoring-a-bundle.md`, replace lines 53-59 (from `Useful env flags:` through the `DIGITALBRAIN_E2E_REPLICAS=1` line) with:

```markdown
**One-time Visual Studio setup (recommended):** Test > Configure Run Settings > Select Solution
Wide runsettings File > `e2e.runsettings`. After this, running any `E2E`-tagged test from Test
Explorer already has `RUN_FLUTTER_E2E=true` and `FAST_UI_E2E=1` set — no terminal needed.

**CLI equivalent**, if you'd rather not touch VS settings:

```sh
cd brain
dotnet test DigitalBrain.Tests --settings e2e.runsettings --filter "FullyQualifiedName~MyBundleRendersE2ETests"
```

Other useful env flags (set manually, either way, when you want them):

- `DIGITALBRAIN_E2E_HEADED=true` — force a visible browser (already the default outside CI).
- `DIGITALBRAIN_E2E_SLOWMO=500` — slow Playwright actions (ms) so you can see each step.
- `DIGITALBRAIN_E2E_REPLICAS=1` — kernel replicas for the test stack (default 1).
```

- [ ] **Step 2: Verify the doc renders sensibly**

Run: `cd /e/digitalbraintech/brain && sed -n '1,75p' docs/authoring-a-bundle.md`
Expected: the "Render loop" section reads cleanly — prerequisites, then the VS setup, then the CLI equivalent, then the remaining manual flags.

- [ ] **Step 3: Commit**

```bash
cd /e/digitalbraintech/brain
git add docs/authoring-a-bundle.md
git commit -m "$(cat <<'MSG'
docs: document the e2e.runsettings one-click render loop

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
Expected: all pass, including the two new `RenderRunSettingsTests`.

- [ ] **Manual spot-check in Visual Studio (requires the render E2E prerequisites)**

Open `Brain.slnx` in Visual Studio, set the solution-wide runsettings file to `e2e.runsettings`,
then right-click `HelloWorldRendersE2ETests` in Test Explorer and choose Run. Confirm it does not
report "Skipped" (which would mean `RUN_FLUTTER_E2E` wasn't picked up).

> `aspire doctor` is **not** required for this slice — a runsettings file and documentation only.

## Out of scope (later slice)

- The warm-cluster attach-with-fallback that delivers the actual 30-120s → few-seconds cycle-time win (Slice 3).
