# Experiences as First-Class, Shell-Rendered, Smallest-Testable Units — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give an Experience a coherent full-screen home (a dedicated `/#/experience` route that advances hops in place) and make authoring each experience's browser E2E ~10 lines via a reusable `ExperienceFlowDriver`.

**Architecture:** Packs tag each hop surface with an `activeExperience` marker injected **into the wire `dataJson`** (the RFW bridge passes `props["dataJson"]` verbatim, so a top-level prop alone never reaches Flutter). A new thin `ExperienceHostScreen` subscribes to `WatchHomeFeed`, keeps the latest envelope whose decoded data matches its target, and renders it full-screen via a shared inline-RFW renderer extracted from the shell. The canvas and shell render paths are untouched (the merged canvas E2E does not regress).

**Tech Stack:** .NET 11 / Orleans / Aspire (`brain/`), Flutter + go_router + RFW (`app/`), xUnit + Reqnroll + Aspire.Hosting.Testing + Playwright (E2E), `flutter_test` (widget tests).

## Global Constraints

- Backend target framework: **net11.0**. Build/test from `brain/`.
- Packs reference **only `DigitalBrain.Core` + BCL** and must use **explicit `using`s** (no implicit usings — the standalone Roslyn/ALC compile has them off; relying on implicit usings causes a silent `PackEmbodimentException` → LLM fallback → pack never runs).
- `TravelPack.cs` is BOTH compiled into the test assembly AND embedded as the pack `Code` string (`TravelPackSource.Read()`). Any API it calls (e.g. `UiSurface.ForExperienceHop`) must exist in `DigitalBrain.Core` so the ALC compile resolves it.
- The browser E2E requires the web bundle built as: `flutter build web --release --no-tree-shake-icons --dart-define=DIGITALBRAIN_E2E=true` (`--no-tree-shake-icons` is mandatory; `main.dart` forces semantics under `DIGITALBRAIN_E2E`). The new route must be present in that built bundle.
- The browser E2E is gated: `RUN_FLUTTER_E2E=true` + a prebuilt bundle at `app/build/web/index.html`. Single replica (`DIGITALBRAIN_KERNEL_REPLICAS=1`) for deterministic fanout.
- `HomeFeedBus` has **no replay** and **dedups identical re-sends** — the `WatchHomeFeed` stream MUST be open before the first hop is emitted (subscribe-before-emit).
- gRPC endpoint split (test fixture): browser nav uses the kernel **"web"** endpoint (`GatewayHttpsUrl`); native-gRPC helpers dial the **"grpc"** endpoint (`GrpcUrl`).
- Self-explanatory names over comments. No vacuous `/// <summary>`. Look up any unfamiliar framework API via Context7 before writing code.
- Per-hop screenshots and failure artifacts go under `AppContext.BaseDirectory/e2e-screenshots` (collectable test artifacts, not the user temp).

---

## Task 1: Baseline — confirm the travel browser E2E is green before any change

**Files:** none (verification gate).

**Interfaces:**
- Consumes: nothing.
- Produces: a confirmed-green baseline so later regressions are attributable.

- [ ] **Step 1: Create the feature branch off master**

```bash
cd brain
git checkout master
git pull --ff-only
git checkout -b experiences-shell-host
```

- [ ] **Step 2: Build the web bundle (required for the E2E)**

```bash
cd ../app
flutter pub get
flutter build web --release --no-tree-shake-icons --dart-define=DIGITALBRAIN_E2E=true
```

Expected: `✓ Built build/web` and `app/build/web/index.html` exists.

- [ ] **Step 3: Run the existing travel browser E2E once (baseline)**

```bash
cd ../brain
RUN_FLUTTER_E2E=true DIGITALBRAIN_KERNEL_REPLICAS=1 \
  dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj \
  --filter "FullyQualifiedName~TravelPlanTripRendersE2ETests"
```

Expected: PASS (1 passed). The hops render on `/#/canvas` today. If it does not pass on a clean master checkout, STOP and report — the premise (green baseline) is false and the plan needs revisiting before changes.

- [ ] **Step 4: Run the fast (non-E2E) suite to record the green count**

```bash
dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "Category!=E2E"
```

Expected: all pass (per CONTINUITY: 163 pass / 1 skip / 0 fail). Record the number; it is the regression baseline for later tasks.

- [ ] **Step 5: Commit a baseline marker (branch checkpoint, no code change)**

```bash
git commit --allow-empty -m "chore: baseline green before experience-host work (E2E + non-E2E suite confirmed)"
```

---

## Task 2: Core — `UiSurface.ForExperienceHop` helper

**Files:**
- Modify: `brain/DigitalBrain.Core/UiSurfaces.cs` (add `using System.Text.Json.Nodes;` at top; add static method to the `UiSurface` record after `ForRfw`)
- Test: `brain/DigitalBrain.Tests/Domains/ForExperienceHopTests.cs` (create)

**Interfaces:**
- Consumes: existing `UiSurface` record, `UiSurface.RfwKind`, `UiSurfaceKeys.SurfaceId/Title/Emitter`.
- Produces:
  `UiSurface UiSurface.ForExperienceHop(string pack, string experienceId, string surfaceId, string libraryName, string rootWidget, string dataJson, string? title = null, string? emitter = null)`
  — returns an `RfwKind` surface whose `Props["dataJson"]` is the input `dataJson` with `activeExperience` (`"<pack>/<experienceId>"`), `experienceId`, and `surfaceId` merged in as top-level JSON keys; `Props` also carries `libraryName`, `rootWidget`, `activeExperience`, `experienceId`, `UiSurfaceKeys.SurfaceId`, and optional `Title`/`Emitter`.

- [ ] **Step 1: Write the failing test**

Create `brain/DigitalBrain.Tests/Domains/ForExperienceHopTests.cs`:

```csharp
using DigitalBrain.Core;
using Xunit;

namespace DigitalBrain.Tests.Domains;

public class ForExperienceHopTests
{
    static string DataJson(UiSurface s) => (string)s.Props["dataJson"]!;

    [Fact]
    public void Injects_marker_into_wire_dataJson()
    {
        var surface = UiSurface.ForExperienceHop(
            pack: "travel", experienceId: "plan-trip", surfaceId: "travel-hotels",
            libraryName: "digitalbrain", rootWidget: "root",
            dataJson: "{\"source\":\"import digitalbrain;\"}");

        Assert.Equal(UiSurface.RfwKind, surface.Kind);
        var json = DataJson(surface);
        Assert.Contains("\"activeExperience\":\"travel/plan-trip\"", json);
        Assert.Contains("\"experienceId\":\"plan-trip\"", json);
        Assert.Contains("\"surfaceId\":\"travel-hotels\"", json);
        Assert.Contains("\"source\":", json); // original payload preserved
    }

    [Fact]
    public void Sets_correlation_and_top_level_props()
    {
        var surface = UiSurface.ForExperienceHop(
            pack: "travel", experienceId: "plan-trip", surfaceId: "travel-intro",
            libraryName: "digitalbrain", rootWidget: "root",
            dataJson: "{}", title: "Plan a trip", emitter: "travel");

        Assert.Equal("travel-intro", surface.Props[UiSurfaceKeys.SurfaceId]);
        Assert.Equal("travel/plan-trip", surface.Props["activeExperience"]);
        Assert.Equal("plan-trip", surface.Props["experienceId"]);
        Assert.Equal("digitalbrain", surface.Props["libraryName"]);
        Assert.Equal("root", surface.Props["rootWidget"]);
        Assert.Equal("Plan a trip", surface.Props[UiSurfaceKeys.Title]);
        Assert.Equal("travel", surface.Props[UiSurfaceKeys.Emitter]);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
cd brain
dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "FullyQualifiedName~ForExperienceHopTests"
```

Expected: FAIL to compile — `UiSurface` does not contain `ForExperienceHop`.

- [ ] **Step 3: Add the `using` and the helper**

At the top of `brain/DigitalBrain.Core/UiSurfaces.cs`, add after `namespace DigitalBrain.Core;`:

```csharp
using System.Text.Json.Nodes;
```

Inside the `UiSurface` record, immediately after the `ForRfw` method (after its closing `}` near line 36), add:

```csharp
    /// Creates an RFW hop surface tagged so an experience host can recognize it and pick the
    /// active hop. The marker is merged INTO dataJson (the RFW bridge forwards Props["dataJson"]
    /// verbatim, so a top-level prop alone would never reach the Flutter client).
    public static UiSurface ForExperienceHop(
        string pack,
        string experienceId,
        string surfaceId,
        string libraryName,
        string rootWidget,
        string dataJson,
        string? title = null,
        string? emitter = null)
    {
        var experienceRef = $"{pack}/{experienceId}";
        var payload = JsonNode.Parse(dataJson) as JsonObject ?? new JsonObject();
        payload["activeExperience"] = experienceRef;
        payload["experienceId"] = experienceId;
        payload["surfaceId"] = surfaceId;

        var props = new Dictionary<string, object?>
        {
            ["libraryName"] = libraryName,
            ["rootWidget"] = rootWidget,
            ["dataJson"] = payload.ToJsonString(),
            ["activeExperience"] = experienceRef,
            ["experienceId"] = experienceId,
            [UiSurfaceKeys.SurfaceId] = surfaceId,
        };
        if (title is not null) props[UiSurfaceKeys.Title] = title;
        if (emitter is not null) props[UiSurfaceKeys.Emitter] = emitter;

        return new UiSurface(RfwKind, props);
    }
```

- [ ] **Step 4: Run the test to verify it passes**

```bash
dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "FullyQualifiedName~ForExperienceHopTests"
```

Expected: PASS (2 passed).

- [ ] **Step 5: Commit**

```bash
git add DigitalBrain.Core/UiSurfaces.cs DigitalBrain.Tests/Domains/ForExperienceHopTests.cs
git commit -m "feat(core): add UiSurface.ForExperienceHop hop marker helper"
```

---

## Task 3: Travel pack emits hops via `ForExperienceHop`

**Files:**
- Modify: `brain/DigitalBrain.Tests/E2E/Packs/TravelPack.cs` (`TravelCards.Surface`, ~lines 172–184)
- Modify: `brain/DigitalBrain.Tests/Domains/TravelPackTests.cs` (extend assertions)

**Interfaces:**
- Consumes: `UiSurface.ForExperienceHop` (Task 2).
- Produces: every travel hop surface now carries the `activeExperience` marker in its `dataJson`; the five `surfaceId`s are unchanged (`travel-intro`, `travel-hotels`, `travel-events`, `travel-activities`, `travel-summary`).

- [ ] **Step 1: Write the failing test (extend `TravelPackTests`)**

In `brain/DigitalBrain.Tests/Domains/TravelPackTests.cs`, add this fact to the class:

```csharp
    [Fact]
    public void Each_hop_carries_active_experience_marker()
    {
        var pack = new TravelPackBehavior();
        var intro = OnlySurface(pack.Handle(Step("start", ("prompt", "plan a trip to Bali next month"))));
        Assert.Contains("\"activeExperience\":\"travel/plan-trip\"", DataJson(intro));
        Assert.Contains("\"surfaceId\":\"travel-intro\"", DataJson(intro));

        var hotels = OnlySurface(pack.Handle(Step("flight.selected", ("flightId", "FL-001"))));
        Assert.Contains("\"activeExperience\":\"travel/plan-trip\"", DataJson(hotels));
        Assert.Contains("\"surfaceId\":\"travel-hotels\"", DataJson(hotels));
    }
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
cd brain
dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "FullyQualifiedName~TravelPackTests.Each_hop_carries_active_experience_marker"
```

Expected: FAIL — `activeExperience` not present in `dataJson` (pack still uses the old `Surface` builder).

- [ ] **Step 3: Refactor `TravelCards.Surface` to delegate to `ForExperienceHop`**

In `brain/DigitalBrain.Tests/E2E/Packs/TravelPack.cs`, replace the entire `Surface` method (lines ~172–184) with:

```csharp
    public static UiSurface Surface(string surfaceId, object data) =>
        UiSurface.ForExperienceHop(
            pack: "travel",
            experienceId: "plan-trip",
            surfaceId: surfaceId,
            libraryName: "digitalbrain",
            rootWidget: "root",
            dataJson: JsonSerializer.Serialize(data, Json),
            title: "Plan a trip",
            emitter: "travel");
```

(No new `using` needed — `DigitalBrain.Core` and `System.Text.Json` are already imported at the top of the file. The marker merge happens inside Core, so the pack stays Core+BCL only.)

- [ ] **Step 4: Run the travel unit tests to verify they pass**

```bash
dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "FullyQualifiedName~TravelPackTests"
```

Expected: PASS (6 passed — the 5 existing + the new marker fact). The existing `Source`/`DataJson`/`SurfaceId` assertions still hold because the original payload is preserved and `surfaceId` is unchanged.

- [ ] **Step 5: Commit**

```bash
git add DigitalBrain.Tests/E2E/Packs/TravelPack.cs DigitalBrain.Tests/Domains/TravelPackTests.cs
git commit -m "feat(travel): emit hops via ForExperienceHop so they carry the experience marker"
```

---

## Task 4: Bridge round-trip test — marker survives to wire `dataJson` + correlationId

**Files:**
- Test: `brain/DigitalBrain.Tests/Kernel/ExperienceHopBridgeTests.cs` (create)

**Interfaces:**
- Consumes: `UiSurface.ForExperienceHop` (Task 2), `UiSurfaceRfwBridge.FromUiSurface` (existing), `RfwCard.DataJson` / `RfwCard.CorrelationId` (existing).
- Produces: a guarantee that `FromUiSurface` forwards the marker into `RfwCard.DataJson` and sets `CorrelationId == surfaceId` (closes spec §7.3).

- [ ] **Step 1: Write the failing test**

Create `brain/DigitalBrain.Tests/Kernel/ExperienceHopBridgeTests.cs`:

```csharp
using DigitalBrain.Core;
using DigitalBrain.Kernel;
using Xunit;

namespace DigitalBrain.Tests.Kernel;

public class ExperienceHopBridgeTests
{
    [Fact]
    public void Hop_marker_and_surface_id_survive_the_rfw_bridge()
    {
        var surface = UiSurface.ForExperienceHop(
            pack: "travel", experienceId: "plan-trip", surfaceId: "travel-hotels",
            libraryName: "digitalbrain", rootWidget: "root",
            dataJson: "{\"source\":\"import digitalbrain;\"}");

        var card = UiSurfaceRfwBridge.FromUiSurface(surface, emitter: "kernel");

        Assert.Equal("travel-hotels", card.CorrelationId);
        Assert.Equal("digitalbrain", card.LibraryName);
        Assert.Equal("root", card.RootWidget);
        Assert.Contains("\"activeExperience\":\"travel/plan-trip\"", card.DataJson);
        Assert.Contains("\"surfaceId\":\"travel-hotels\"", card.DataJson);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails (then confirm the assertion target)**

```bash
cd brain
dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "FullyQualifiedName~ExperienceHopBridgeTests"
```

Expected: this test should actually **PASS immediately** — Task 2/3 already make the marker flow, and the bridge already prefers `props[surfaceId]` for `CorrelationId` and forwards `props["dataJson"]`. This task is a *characterization test* locking that behavior. If it FAILS, the bridge is dropping the marker — investigate `UiSurfaceRfwBridge.FromUiSurface` before proceeding (do NOT weaken the assertions).

- [ ] **Step 3: (No implementation needed)**

The bridge already behaves correctly; this test pins it. If Step 2 failed, the minimal fix is to ensure the RFW early-return branch uses `props["dataJson"]` verbatim and `props[surfaceId]` for the correlation (it does today) — re-read `brain/DigitalBrain.Kernel/Ui/UiSurfaceRfwBridge.cs:43-53`.

- [ ] **Step 4: Run to confirm green**

```bash
dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "FullyQualifiedName~ExperienceHopBridgeTests"
```

Expected: PASS (1 passed).

- [ ] **Step 5: Commit**

```bash
git add DigitalBrain.Tests/Kernel/ExperienceHopBridgeTests.cs
git commit -m "test(kernel): pin that the experience marker survives the RFW bridge"
```

---

## Task 5: App — extract shared inline-RFW renderer; refactor shell to use it

**Files:**
- Create: `app/lib/rfw_host/inline_rfw_surface.dart`
- Modify: `app/lib/shell/forui_app_shell.dart` (`_renderEnvelope` source branch, ~lines 243–260)
- Test: `app/test/rfw_host/inline_rfw_surface_test.dart` (create)

**Interfaces:**
- Consumes: `RfwRuntimeHost` (existing `render`/`ensureLoaded`), `RemoteEventHandler` (from `package:rfw/rfw.dart`).
- Produces:
  `Widget? buildInlineRfwSurface({required RfwRuntimeHost host, required Map<String, Object?> data, required String fallbackKey, required String defaultRootWidget, required RemoteEventHandler onEvent, String? correlationId, String? semanticsId})`
  — returns a `SizedBox.expand` wrapping `host.render(...)` for the inline-`source` case, or `null` when `data['source']` is empty. Loads the source under key `correlationId` (falling back to `fallbackKey`). Passes `semanticsId` straight through to `host.render` (so the shell, which omits it, is behavior-preserved; the experience host supplies `correlationId` for an assertable identifier).

- [ ] **Step 1: Write the failing widget test**

Create `app/test/rfw_host/inline_rfw_surface_test.dart`:

```dart
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:digitalbrain_flutter/rfw_host/rfw_runtime_host.dart';
import 'package:digitalbrain_flutter/rfw_host/inline_rfw_surface.dart';

void main() {
  testWidgets('renders inline RFW source with the supplied semantics id', (tester) async {
    final handle = tester.ensureSemantics();
    final host = RfwRuntimeHost();

    final widget = buildInlineRfwSurface(
      host: host,
      data: const <String, Object?>{
        'source': 'import digitalbrain;\nwidget root = Text(text: "hop-body");',
      },
      fallbackKey: 'fallback',
      defaultRootWidget: 'root',
      onEvent: (_, _) {},
      correlationId: 'travel-hotels',
      semanticsId: 'travel-hotels',
    );

    expect(widget, isNotNull);
    await tester.pumpWidget(MaterialApp(home: Scaffold(body: widget!)));
    await tester.pumpAndSettle();

    expect(find.bySemanticsIdentifier('travel-hotels'), findsOneWidget);
    expect(find.text('hop-body'), findsOneWidget);
    handle.dispose();
  });

  testWidgets('returns null when there is no inline source', (tester) async {
    final host = RfwRuntimeHost();
    final widget = buildInlineRfwSurface(
      host: host,
      data: const <String, Object?>{},
      fallbackKey: 'fallback',
      defaultRootWidget: 'root',
      onEvent: (_, _) {},
    );
    expect(widget, isNull);
  });
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
cd app
flutter test test/rfw_host/inline_rfw_surface_test.dart
```

Expected: FAIL — `inline_rfw_surface.dart` does not exist.

- [ ] **Step 3: Create the shared renderer**

Create `app/lib/rfw_host/inline_rfw_surface.dart`:

```dart
import 'package:flutter/widgets.dart';
import 'package:rfw/rfw.dart' show RemoteEventHandler;

import 'rfw_runtime_host.dart';

/// Renders the inline-RFW (`data['source']`) case of a streamed surface, expanded to fill its
/// parent. Shared by the neuron shell and the experience host so there is one renderer, not two.
/// Returns null when the surface has no inline source (the caller falls back to its own paths).
Widget? buildInlineRfwSurface({
  required RfwRuntimeHost host,
  required Map<String, Object?> data,
  required String fallbackKey,
  required String defaultRootWidget,
  required RemoteEventHandler onEvent,
  String? correlationId,
  String? semanticsId,
}) {
  final source = data['source'] as String?;
  if (source == null || source.isEmpty) return null;

  final root =
      (data['rootWidget'] as String? ?? data['root'] as String? ?? defaultRootWidget);
  final key = (correlationId == null || correlationId.isEmpty) ? fallbackKey : correlationId;
  host.ensureLoaded(key, source);
  return SizedBox.expand(
    child: host.render(
      key,
      data: data,
      onEvent: onEvent,
      rootWidget: root,
      semanticsId: semanticsId,
    ),
  );
}
```

- [ ] **Step 4: Run the widget test to verify it passes**

```bash
flutter test test/rfw_host/inline_rfw_surface_test.dart
```

Expected: PASS (2 tests).

- [ ] **Step 5: Refactor the shell to use the shared renderer (behavior-preserving)**

In `app/lib/shell/forui_app_shell.dart`, add the import near the other `rfw_host` import (after line 9):

```dart
import 'package:digitalbrain_flutter/rfw_host/inline_rfw_surface.dart';
```

Replace the inline-source branch of `_renderEnvelope` (the block from `final source = data['source'] as String?;` through its `return null;`, currently lines ~243–260) with:

```dart
    return buildInlineRfwSurface(
      host: _rfwHost,
      data: data,
      fallbackKey: emptyKey,
      defaultRootWidget: env.rootWidget,
      onEvent: _handleSurfaceEvent,
      correlationId: env.correlationId,
    );
```

(The shell passes no `semanticsId` — identical to today. The earlier `data['tree']` branch of `_renderEnvelope` is unchanged.)

- [ ] **Step 6: Verify the app still analyzes clean and the shell renders unchanged**

```bash
flutter analyze
flutter test
```

Expected: `flutter analyze` reports only pre-existing warnings (no new errors); `flutter test` all pass. The shell's marketplace/installed/login fallbacks are unaffected (they route through the same `_renderEnvelope` which now delegates the source path).

- [ ] **Step 7: Commit**

```bash
git add lib/rfw_host/inline_rfw_surface.dart lib/shell/forui_app_shell.dart test/rfw_host/inline_rfw_surface_test.dart
git commit -m "refactor(app): extract shared buildInlineRfwSurface; shell delegates to it"
```

---

## Task 6: App — `ExperienceHostScreen` + routes

**Files:**
- Create: `app/lib/features/experience/experience_match.dart`
- Create: `app/lib/features/experience/experience_hop_view.dart`
- Create: `app/lib/features/experience/experience_host_screen.dart`
- Modify: `app/lib/router.dart` (add two routes)
- Test: `app/test/features/experience/experience_match_test.dart` (create)
- Test: `app/test/features/experience/experience_hop_view_test.dart` (create)

**Interfaces:**
- Consumes: `buildInlineRfwSurface` (Task 5), `RfwRuntimeHost`, the gRPC helpers `resolveKernelEndpoint`/`createKernelChannel`/`kernelInterceptors` (existing in `lib/grpc/`), `DigitalBrainGatewayClient` + `WatchHomeFeedRequest` + `RfwCardEnvelope` (existing generated gRPC).
- Produces:
  - `bool experienceHopMatches(Map<String, Object?> data, String? target)` — true when `data['activeExperience']` is a non-empty string and (`target` is null/empty OR equals it).
  - `ExperienceHopView` (StatelessWidget) — renders one decoded hop full-screen with `semanticsId == correlationId`.
  - `ExperienceHostScreen({String? pack, String? experienceId})` — the route widget.
  - Routes `/experience` and `/experience/:pack/:experienceId`.

- [ ] **Step 1: Write the failing matcher test**

Create `app/test/features/experience/experience_match_test.dart`:

```dart
import 'package:flutter_test/flutter_test.dart';
import 'package:digitalbrain_flutter/features/experience/experience_match.dart';

void main() {
  test('matches any experience hop when no target is given', () {
    expect(experienceHopMatches({'activeExperience': 'travel/plan-trip'}, null), isTrue);
    expect(experienceHopMatches({'activeExperience': 'travel/plan-trip'}, ''), isTrue);
  });

  test('matches only the targeted experience when a target is given', () {
    expect(experienceHopMatches({'activeExperience': 'travel/plan-trip'}, 'travel/plan-trip'), isTrue);
    expect(experienceHopMatches({'activeExperience': 'food/order'}, 'travel/plan-trip'), isFalse);
  });

  test('rejects non-experience surfaces', () {
    expect(experienceHopMatches(const {}, null), isFalse);
    expect(experienceHopMatches(const {'activeExperience': ''}, null), isFalse);
  });
}
```

- [ ] **Step 2: Run to verify it fails**

```bash
cd app
flutter test test/features/experience/experience_match_test.dart
```

Expected: FAIL — `experience_match.dart` does not exist.

- [ ] **Step 3: Implement the matcher**

Create `app/lib/features/experience/experience_match.dart`:

```dart
/// True when a decoded surface belongs to a live experience. When [target] ("<pack>/<id>") is
/// supplied (the deep-linked route case), only that experience matches; otherwise any hop matches.
bool experienceHopMatches(Map<String, Object?> data, String? target) {
  final ref = data['activeExperience'];
  if (ref is! String || ref.isEmpty) return false;
  if (target == null || target.isEmpty) return true;
  return ref == target;
}
```

- [ ] **Step 4: Run to verify it passes**

```bash
flutter test test/features/experience/experience_match_test.dart
```

Expected: PASS (3 tests).

- [ ] **Step 5: Write the failing hop-view widget test**

Create `app/test/features/experience/experience_hop_view_test.dart`:

```dart
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:digitalbrain_flutter/rfw_host/rfw_runtime_host.dart';
import 'package:digitalbrain_flutter/features/experience/experience_hop_view.dart';

void main() {
  testWidgets('renders the hop body under the surfaceId semantics identifier', (tester) async {
    final handle = tester.ensureSemantics();
    final host = RfwRuntimeHost();

    await tester.pumpWidget(MaterialApp(
      home: Scaffold(
        body: ExperienceHopView(
          host: host,
          data: const <String, Object?>{
            'activeExperience': 'travel/plan-trip',
            'source': 'import digitalbrain;\nwidget root = Text(text: "hotels-hop");',
          },
          correlationId: 'travel-hotels',
          rootWidget: 'root',
          onEvent: (_, _) {},
        ),
      ),
    ));
    await tester.pumpAndSettle();

    expect(find.bySemanticsIdentifier('travel-hotels'), findsOneWidget);
    expect(find.text('hotels-hop'), findsOneWidget);
    handle.dispose();
  });
}
```

- [ ] **Step 6: Run to verify it fails**

```bash
flutter test test/features/experience/experience_hop_view_test.dart
```

Expected: FAIL — `experience_hop_view.dart` does not exist.

- [ ] **Step 7: Implement `ExperienceHopView`**

Create `app/lib/features/experience/experience_hop_view.dart`:

```dart
import 'package:flutter/widgets.dart';
import 'package:rfw/rfw.dart' show RemoteEventHandler;

import 'package:digitalbrain_flutter/rfw_host/inline_rfw_surface.dart';
import 'package:digitalbrain_flutter/rfw_host/rfw_runtime_host.dart';

/// Renders one experience hop full-screen. The semantics identifier is the hop's [correlationId]
/// (== the pack's surfaceId), which the browser E2E asserts via `flt-semantics-identifier`.
class ExperienceHopView extends StatelessWidget {
  const ExperienceHopView({
    super.key,
    required this.host,
    required this.data,
    required this.correlationId,
    required this.onEvent,
    this.rootWidget = 'root',
  });

  final RfwRuntimeHost host;
  final Map<String, Object?> data;
  final String correlationId;
  final RemoteEventHandler onEvent;
  final String rootWidget;

  @override
  Widget build(BuildContext context) {
    final body = buildInlineRfwSurface(
      host: host,
      data: data,
      fallbackKey: correlationId,
      defaultRootWidget: rootWidget,
      onEvent: onEvent,
      correlationId: correlationId,
      semanticsId: correlationId,
    );
    return body ?? const SizedBox.shrink();
  }
}
```

- [ ] **Step 8: Run to verify it passes**

```bash
flutter test test/features/experience/experience_hop_view_test.dart
```

Expected: PASS (1 test).

- [ ] **Step 9: Implement `ExperienceHostScreen` (gRPC glue, E2E-covered)**

Create `app/lib/features/experience/experience_host_screen.dart`:

```dart
import 'dart:async';
import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import 'package:digitalbrain_flutter/grpc/digitalbrain.pbgrpc.dart';
import 'package:digitalbrain_flutter/grpc/endpoint.dart';
import 'package:digitalbrain_flutter/grpc/grpc_channel.dart';
import 'package:digitalbrain_flutter/grpc/digitalbrain.pb.dart' as gw;
import 'package:digitalbrain_flutter/rfw_host/rfw_runtime_host.dart';

import 'experience_hop_view.dart';
import 'experience_match.dart';

/// Full-screen host for a guided experience. Subscribes to WatchHomeFeed and renders the latest
/// hop whose surface carries a matching `activeExperience` marker, replacing the previous hop in
/// place as the journey advances. Chrome-free except a minimal exit affordance.
class ExperienceHostScreen extends StatefulWidget {
  const ExperienceHostScreen({super.key, this.pack, this.experienceId});

  final String? pack;
  final String? experienceId;

  String? get target =>
      (pack != null && experienceId != null) ? '$pack/$experienceId' : null;

  @override
  State<ExperienceHostScreen> createState() => _ExperienceHostScreenState();
}

class _ExperienceHostScreenState extends State<ExperienceHostScreen> {
  final RfwRuntimeHost _rfwHost = RfwRuntimeHost();
  dynamic _channel;
  StreamSubscription<gw.RfwCardEnvelope>? _feedSub;

  Map<String, Object?>? _hopData;
  String? _hopCorrelationId;
  String? _status;

  @override
  void initState() {
    super.initState();
    _connect();
  }

  @override
  void dispose() {
    _feedSub?.cancel();
    _channel?.shutdown();
    super.dispose();
  }

  void _connect() {
    try {
      final (host, port, secure) = resolveKernelEndpoint();
      final channel = createKernelChannel(host: host, port: port, secure: secure);
      final client = DigitalBrainGatewayClient(channel, interceptors: kernelInterceptors());
      final sub = client
          .watchHomeFeed(gw.WatchHomeFeedRequest())
          .listen(_onCard, onError: _onError);
      setState(() {
        _channel = channel;
        _feedSub = sub;
        _status = 'Waiting for the experience to start…';
      });
    } catch (error) {
      setState(() => _status = 'Experience feed connection failed: $error');
    }
  }

  void _onError(Object error, StackTrace _) {
    if (!mounted) return;
    setState(() => _status = 'Experience feed error: $error');
  }

  void _onCard(gw.RfwCardEnvelope envelope) {
    if (!mounted) return;
    final data = _decode(envelope.dataJson);
    if (!experienceHopMatches(data, widget.target)) return;
    setState(() {
      _hopData = data;
      _hopCorrelationId = envelope.correlationId;
      _status = null;
    });
  }

  Map<String, Object?> _decode(String json) {
    try {
      final d = jsonDecode(json);
      if (d is Map) return d.map((k, v) => MapEntry(k.toString(), v));
    } catch (_) {}
    return const {};
  }

  void _onSurfaceEvent(String name, Map<String, Object?> args) {
    // Card taps are forwarded as-is for future gateway→ExperienceStep mapping; the E2E drives
    // hops natively via SendExperienceStepAsync, so this is best-effort and not on the test path.
  }

  @override
  Widget build(BuildContext context) {
    final data = _hopData;
    final correlationId = _hopCorrelationId;
    final body = (data != null && correlationId != null)
        ? ExperienceHopView(
            host: _rfwHost,
            data: data,
            correlationId: correlationId,
            onEvent: _onSurfaceEvent,
          )
        : Center(child: Text(_status ?? 'Loading experience…'));

    return Scaffold(
      body: SafeArea(
        child: Stack(
          children: [
            Positioned.fill(child: body),
            Positioned(
              left: 8,
              top: 8,
              child: BackButton(
                onPressed: () =>
                    context.canPop() ? context.pop() : context.go('/'),
              ),
            ),
          ],
        ),
      ),
    );
  }
}
```

- [ ] **Step 10: Register the routes**

In `app/lib/router.dart`, add the import after the existing feature imports (after line 5):

```dart
import 'features/experience/experience_host_screen.dart';
```

Add these two routes to the top-level `routes:` list (e.g. after the `/canvas` route, before `/spike`):

```dart
    GoRoute(
      path: '/experience',
      name: 'experience',
      builder: (context, state) => const ExperienceHostScreen(),
    ),
    GoRoute(
      path: '/experience/:pack/:experienceId',
      name: 'experience-targeted',
      builder: (context, state) => ExperienceHostScreen(
        pack: state.pathParameters['pack'],
        experienceId: state.pathParameters['experienceId'],
      ),
    ),
```

- [ ] **Step 11: Verify analyze + full app test suite**

```bash
flutter analyze
flutter test
```

Expected: `flutter analyze` only pre-existing warnings; `flutter test` all pass (includes the new matcher + hop-view tests).

- [ ] **Step 12: Commit**

```bash
git add lib/features/experience/ lib/router.dart test/features/experience/
git commit -m "feat(app): experience-host route renders a guided journey full-screen, advancing in place"
```

---

## Task 7: `ExperienceFlowDriver` + fixture diagnostics

**Files:**
- Create: `brain/DigitalBrain.Tests/E2E/ExperienceFlowDriver.cs`
- Modify: `brain/DigitalBrain.Tests/E2E/DigitalBrainBrowserFixture.cs` (headed/headless/slowMo toggle)

**Interfaces:**
- Consumes: `DigitalBrainBrowserFixture` (`Page`, `GatewayHttpsUrl`, `PublishPackAsync`, `InstallPackAsync`, `SendExperienceStepAsync`), Playwright `IPage`.
- Produces an `ExperienceFlowDriver` with:
  - ctor `(DigitalBrainBrowserFixture fixture, string pack, string experienceId)`
  - `Task PublishAndInstallAsync(string code, string description, string version = "1.0", string buyer = "e2e", double commissionRate = 0.0)`
  - `Task OpenAsync()` — navigates to `/#/experience/<pack>/<experienceId>`, attaches console/error capture, and waits for the first `WatchHomeFeed` response
  - `Task TriggerExperienceAsync(params (string, string)[] args)` — fires the `start` step
  - `Task TapAsync(string eventName, params (string, string)[] args)` — fires a step
  - `Task AssertHopRendersAsync(string surfaceId)` — waits for `[flt-semantics-identifier="<surfaceId>"]`, asserts count == 1, screenshots; on failure dumps console/page-errors/DOM to the artifacts dir then rethrows

- [ ] **Step 1: Add the headed/headless/slowMo toggle to the browser fixture**

In `brain/DigitalBrain.Tests/E2E/DigitalBrainBrowserFixture.cs`, replace the `isCi`/`LaunchAsync` block (lines ~22–27) with:

```csharp
        bool isCi = string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase);
        bool forceHeaded = string.Equals(Environment.GetEnvironmentVariable("DIGITALBRAIN_E2E_HEADED"), "true", StringComparison.OrdinalIgnoreCase);
        bool forceHeadless = string.Equals(Environment.GetEnvironmentVariable("DIGITALBRAIN_E2E_HEADLESS"), "true", StringComparison.OrdinalIgnoreCase);
        bool headless = forceHeaded ? false : (forceHeadless || isCi);
        float? slowMo = int.TryParse(Environment.GetEnvironmentVariable("DIGITALBRAIN_E2E_SLOWMO"), out var ms) ? ms : null;

        Browser = await Playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = headless,
            SlowMo = slowMo,
        });
```

- [ ] **Step 2: Create the driver**

Create `brain/DigitalBrain.Tests/E2E/ExperienceFlowDriver.cs`:

```csharp
using DigitalBrain.Core;
using Microsoft.Playwright;
using Xunit;

namespace DigitalBrain.Tests.E2E;

/// Reusable harness for browser E2Es of an experience: publish+install a domain pack, open its
/// full-screen host, then trigger/tap hops and assert each renders in Flutter. Generalizes the
/// boilerplate so a new experience E2E is ~10 lines. On a failed hop it dumps browser console
/// logs, page errors, and the DOM to the artifacts dir for diagnosis without re-deriving the
/// pipeline. For deeper debugging see the dart-MCP recipe in the class remarks below.
///
/// dart-MCP manual recipe (NOT wired here — the release web bundle has no Dart Tooling Daemon):
///   1) cd app && flutter run -d chrome --dart-define=DIGITALBRAIN_E2E=true   (debug, exposes DTD)
///   2) point it at the same kernel endpoint, drive the experience route
///   3) use the dart MCP tools get_widget_tree / get_runtime_errors against that running app.
public sealed class ExperienceFlowDriver
{
    readonly DigitalBrainBrowserFixture _fx;
    readonly string _pack;
    readonly string _experienceId;
    readonly List<string> _consoleLog = new();
    readonly string _artifactDir;

    public ExperienceFlowDriver(DigitalBrainBrowserFixture fixture, string pack, string experienceId)
    {
        _fx = fixture;
        _pack = pack;
        _experienceId = experienceId;
        _artifactDir = Path.Combine(AppContext.BaseDirectory, "e2e-screenshots");
        Directory.CreateDirectory(_artifactDir);
    }

    public async Task PublishAndInstallAsync(string code, string description,
        string version = "1.0", string buyer = "e2e", double commissionRate = 0.0)
    {
        await _fx.PublishPackAsync(_pack, version, code: code, commissionRate: commissionRate, description: description);
        await _fx.InstallPackAsync(_pack, version, buyer: buyer);
    }

    public async Task OpenAsync()
    {
        _fx.Page.Console += (_, msg) => _consoleLog.Add($"[{msg.Type}] {msg.Text}");
        _fx.Page.PageError += (_, err) => _consoleLog.Add($"[pageerror] {err}");

        // Web hash strategy: deep-link straight to the experience host. Wait for the WatchHomeFeed
        // response before any hop is emitted (HomeFeedBus has no replay; subscribe-before-emit).
        var url = _fx.GatewayHttpsUrl.TrimEnd('/') + $"/#/experience/{_pack}/{_experienceId}";
        await _fx.Page.RunAndWaitForResponseAsync(
            () => _fx.Page.GotoAsync(url, new() { WaitUntil = WaitUntilState.Load }),
            r => r.Url.Contains("WatchHomeFeed"),
            new() { Timeout = 60_000 });
    }

    public Task TriggerExperienceAsync(params (string, string)[] args) => StepAsync("start", args);

    public Task TapAsync(string eventName, params (string, string)[] args) => StepAsync(eventName, args);

    async Task StepAsync(string eventName, (string, string)[] args)
    {
        await _fx.SendExperienceStepAsync(_pack, _experienceId, eventName,
            args.ToDictionary(a => a.Item1, a => a.Item2));
    }

    public async Task AssertHopRendersAsync(string surfaceId)
    {
        var node = _fx.Page.Locator($"[flt-semantics-identifier=\"{surfaceId}\"]");
        try
        {
            await node.WaitForAsync(new() { Timeout = 30_000 });
            Assert.Equal(1, await node.CountAsync());
            await _fx.Page.ScreenshotAsync(new() { Path = Path.Combine(_artifactDir, $"e2e-{_pack}-{surfaceId}.png") });
        }
        catch
        {
            await DumpFailureAsync(surfaceId);
            throw;
        }
    }

    async Task DumpFailureAsync(string surfaceId)
    {
        try
        {
            await File.WriteAllLinesAsync(Path.Combine(_artifactDir, $"console-{_pack}-{surfaceId}.log"), _consoleLog);
            var dom = await _fx.Page.ContentAsync();
            await File.WriteAllTextAsync(Path.Combine(_artifactDir, $"dom-{_pack}-{surfaceId}.html"), dom);
            await _fx.Page.ScreenshotAsync(new() { Path = Path.Combine(_artifactDir, $"FAILED-{_pack}-{surfaceId}.png") });
        }
        catch { /* diagnostics are best-effort; never mask the original assertion failure */ }
    }
}
```

- [ ] **Step 3: Build the test project to verify the driver compiles**

```bash
cd brain
dotnet build DigitalBrain.Tests/DigitalBrain.Tests.csproj
```

Expected: Build succeeded (the driver and fixture change compile; no behavior asserted yet — Task 8 exercises it).

- [ ] **Step 4: Commit**

```bash
git add DigitalBrain.Tests/E2E/ExperienceFlowDriver.cs DigitalBrain.Tests/E2E/DigitalBrainBrowserFixture.cs
git commit -m "test(e2e): add ExperienceFlowDriver + fixture headed/slowMo/diagnostics toggles"
```

---

## Task 8: Rewrite the travel E2E onto the driver, targeting the experience route

**Files:**
- Modify: `brain/DigitalBrain.Tests/E2E/TravelPlanTripRendersE2ETests.cs` (full rewrite)

**Interfaces:**
- Consumes: `ExperienceFlowDriver` (Task 7), `TravelPackSource.Read()`, `E2EPrerequisites.RequireRenderE2E()`.
- Produces: the regression anchor proving the experience renders full-screen on `/#/experience/...` and advances through all five hops.

- [ ] **Step 1: Rewrite the test to use the driver**

Replace the entire contents of `brain/DigitalBrain.Tests/E2E/TravelPlanTripRendersE2ETests.cs` with:

```csharp
using DigitalBrain.Tests.E2E.Packs;
using Xunit;

namespace DigitalBrain.Tests.E2E;

[Trait("Category", "E2E")]
[Collection(nameof(DigitalBrainE2ECollection))]
public sealed class TravelPlanTripRendersE2ETests(DigitalBrainBrowserFixture fixture)
{
    readonly DigitalBrainBrowserFixture _fx = fixture;

    [SkippableFact]
    public async Task PlanTrip_walks_hops_and_each_renders_full_screen_in_flutter()
    {
        E2EPrerequisites.RequireRenderE2E();

        var driver = new ExperienceFlowDriver(_fx, pack: "travel", experienceId: "plan-trip");
        await driver.PublishAndInstallAsync(TravelPackSource.Read(),
            description: "Travel domain — Plan a trip experience");
        await driver.OpenAsync();

        await driver.TriggerExperienceAsync(("prompt", "plan a trip to Bali next month"));
        await driver.AssertHopRendersAsync("travel-intro");

        await driver.TapAsync("flight.selected", ("flightId", "FL-001"));
        await driver.AssertHopRendersAsync("travel-hotels");

        await driver.TapAsync("hotel.selected", ("hotelId", "H-001"));
        await driver.AssertHopRendersAsync("travel-events");

        await driver.TapAsync("event.selected", ("eventId", "EV-001"));
        await driver.AssertHopRendersAsync("travel-activities");

        await driver.TapAsync("activity.selected", ("activityId", "AC-001"));
        await driver.AssertHopRendersAsync("travel-summary");
    }
}
```

- [ ] **Step 2: Rebuild the web bundle with the new route**

```bash
cd ../app
flutter build web --release --no-tree-shake-icons --dart-define=DIGITALBRAIN_E2E=true
```

Expected: `✓ Built build/web`. (The bundle now contains `/experience` routes — required, the E2E navigates there.)

- [ ] **Step 3: Run the rewritten E2E (the integration deliverable)**

```bash
cd ../brain
RUN_FLUTTER_E2E=true DIGITALBRAIN_KERNEL_REPLICAS=1 \
  dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj \
  --filter "FullyQualifiedName~TravelPlanTripRendersE2ETests"
```

Expected: PASS (1 passed). All five hops render full-screen on the experience route, each replacing the prior in place. Per-hop screenshots land in the test output `e2e-screenshots/`. If a hop fails, inspect the `console-*.log` / `dom-*.html` / `FAILED-*.png` artifacts in the same dir.

- [ ] **Step 4: (If green) optionally watch it headed**

```bash
RUN_FLUTTER_E2E=true DIGITALBRAIN_KERNEL_REPLICAS=1 DIGITALBRAIN_E2E_HEADED=true DIGITALBRAIN_E2E_SLOWMO=300 \
  dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj \
  --filter "FullyQualifiedName~TravelPlanTripRendersE2ETests"
```

Expected: PASS, with a visible Chromium window stepping through the journey (diagnosability, goal 3).

- [ ] **Step 5: Commit**

```bash
git add DigitalBrain.Tests/E2E/TravelPlanTripRendersE2ETests.cs
git commit -m "test(e2e): drive the travel experience through the full-screen host via ExperienceFlowDriver"
```

---

## Task 9: Full verification ritual + code review

**Files:** none (verification).

**Interfaces:**
- Consumes: all prior tasks.
- Produces: a verified, review-clean branch ready to integrate.

- [ ] **Step 1: Backend build + full fast suite (regression check)**

```bash
cd brain
dotnet build Brain.slnx
dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "Category!=E2E"
```

Expected: Build succeeded; the fast suite passes with the same count as Task 1 Step 4 **plus** the new tests (ForExperienceHop ×2, TravelPack marker ×1, bridge ×1) — none skipped except the gated E2E, 0 failed.

- [ ] **Step 2: App analyze + tests**

```bash
cd ../app
flutter analyze
flutter test
```

Expected: only pre-existing warnings; all widget tests pass.

- [ ] **Step 3: Aspire doctor (hosting health)**

Use the aspire MCP (`mcp__aspire__doctor`) or:

```bash
cd ../brain
aspire doctor
```

Expected: all checks pass (per CONTINUITY ritual, 4/4).

- [ ] **Step 4: Code review**

Run the code-review skill (`/code-review`, opus, high) over the branch diff vs master. Address Critical/Important findings; record skipped findings with rationale.

- [ ] **Step 5: Update CONTINUITY**

Append a dated entry to `brain/CONTINUITY.md` summarizing: the `activeExperience` marker (injected into dataJson, not a top-level prop), the `/#/experience` host route advancing hops in place, the shared `buildInlineRfwSurface` extraction, the `ExperienceFlowDriver`, and the deferred follow-ups (shell "Plan a trip" entry button; gateway card-tap → `ExperienceStep` mapping; per-user flow state; `NeuroPack→Domain` rename).

- [ ] **Step 6: Commit and finish the branch**

```bash
git add CONTINUITY.md
git commit -m "docs: log experience-host slice in CONTINUITY"
```

Then use the `superpowers:finishing-a-development-branch` skill to decide merge/PR.

---

## Self-Review (against the spec)

**Spec coverage:**
- §3.1 `activeExperience` marker → Tasks 2, 3, 4 (Core helper, travel emission, bridge round-trip).
- §3.2 dedicated experience-host route + shared renderer → Tasks 5, 6.
- §3.3 data flow (one hop) → exercised end-to-end by Task 8.
- §3.4 `ExperienceFlowDriver` (standalone over the fixture) → Task 7; ~10-line E2E proof → Task 8.
- Diagnostics (headed toggle, screenshots, on-failure console/DOM dump, dart-MCP recipe) → Task 7.
- §4 web bundle invariant + new route present → Task 1 Step 2, Task 8 Step 2.
- §6 baseline-first + ritual + Context7 + code review → Tasks 1, 9.
- §7 open risks: §7.1 (no card-tap→ExperienceStep mapping this phase) recorded as deferred in Task 9 Step 5 and Task 6 `_onSurfaceEvent`; §7.2 (extraction behavior-preserving) verified Task 5 Step 6; §7.3 (marker survives bridge) Task 4; §7.4 (match-on-target vs render-any) Task 6 `experienceHopMatches`.

**Placeholder scan:** no TBD/TODO; every code step shows complete code; commands have expected output.

**Type consistency:** `ForExperienceHop` signature identical across Tasks 2/3/4. `buildInlineRfwSurface` signature identical across Tasks 5/6. `experienceHopMatches`, `ExperienceHopView`, `ExperienceHostScreen` names consistent across Task 6 and its tests. `ExperienceFlowDriver` method names (`PublishAndInstallAsync`/`OpenAsync`/`TriggerExperienceAsync`/`TapAsync`/`AssertHopRendersAsync`) identical in Tasks 7 and 8.
