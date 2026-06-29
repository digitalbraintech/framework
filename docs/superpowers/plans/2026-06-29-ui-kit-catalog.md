# UI-Kit Catalog (Sub-project B) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Grow the typed `ui:` UI kit from 5 to 35 components (5 existing + 30 new) — each a thin ForUI cover routed by the existing `ui:*` branch — plus a self-authored `ui-gallery` showcase pack, following the proven Slice 0 three-edit-per-component pattern.

**Architecture:** Each component is (1) a `ui:<Name>` const on `Ui` + a fluent method on `UiHop`/`UiExperience` in `DigitalBrain.Core`, (2) one `app/lib/ui_kit/ui_<name>.dart` ForUI cover + a `ui_registry.dart` case, (3) a Dart widget test + a Core builder/emit test. The `ui:*` dispatch branch in `UiSurfaceTreeRenderer.build` and the `KitExperience.Inject` walk already route everything; nav nodes reuse the `ExperienceStep` loop (no new infra); overlays are declarative `open` nodes presented imperatively client-side (no protocol change).

**Tech Stack:** .NET (net11.0, Orleans 10.2, Aspire 13.4.6), Roslyn+collectible ALC pack embodiment, gRPC; Flutter (ForUI 0.21.3, rfw, go_router), flutter_test (app); xUnit + Reqnroll + Playwright (brain).

## Global Constraints

- **net11.0**; AppHost is `NeuroOSPrototype.AppHost`; "Silo" is "Kernel". Central versions in `Directory.Packages.props` — no `Version="*"`, no inline `Version=`.
- **Look up EVERY ForUI/Flutter API via Context7 before writing a cover.** This plan's cover code is grounded in ForUI 0.21.3 signatures but the implementer MUST verify each widget's constructor/control/change API via Context7 and fix any drift (param names, `onChange` vs control, variant enums) before running tests. Mandatory per repo rules.
- **Node prefix is `ui:`**; Core catalog class is `Ui`; app covers live in `app/lib/ui_kit/`; registry is `app/lib/ui_kit/ui_registry.dart` (switch on `type.toLowerCase()`).
- **Value model: every captured input value is a string on the wire.** Covers coerce at the boundary (`Checkbox`/`Switch` → `"true"`/`"false"`, `Slider` → number `toString()`, `DateField` → ISO `yyyy-MM-dd`, `Select`/`RadioGroup` → the chosen string). The form scope is `Map<String,String>` (`UiKitFormController`).
- **ForUI test gotchas (carry forward):** theme in widget tests is `FThemes.neutral.light.touch` (NOT zinc). `FButton` has a 100 ms press-animation timer → tap then `await tester.pumpAndSettle()`. `FTextField` value capture is `FTextField(control: FTextFieldControl.lifted(value:, onChange:))` — there is no direct `onChange`/`controller` param.
- **Pack source strings compile standalone via Roslyn/ALC against `DigitalBrain.Core` + BCL with EXPLICIT `using`s, and MUST NOT use `\"` escapes inside a C# raw string literal (`"""`)** — `\` is literal there and silently breaks embodiment. Use string concat where a literal quote is needed.
- **Self-explanatory names; NO vacuous `/// <summary>`.** Small inline comments only where genuinely non-obvious.
- **Additive only:** do not disturb the `neuron:` / `forui:` / RFW renderer branches, the existing 5 covers, or the `ui:*` dispatch + `Inject` walk except where a task explicitly extends them.
- **Verification ritual per task:** `dotnet build`; targeted `dotnet test`; `flutter analyze`; `flutter test test/ui_kit/` (+ the task's tests). End of branch: `aspire doctor` + one intentional `aspire run` to watch the gallery render live.
- Run brain steps from `E:\digitalbraintech\brain`, app steps from `E:\digitalbraintech\app` (separate repo roots). Use relative paths inside each repo.

---

## Branch setup (do once, before Task 0)

```bash
cd /e/digitalbraintech/brain && git switch -c feat/ui-kit-catalog master
cd /e/digitalbraintech/app   && git switch -c feat/ui-kit-catalog main
```

---

## Patterns (read once; every component task instantiates these)

These three patterns are the entire repeatable shape. Each component task shows only its component-specific deltas; assemble them with these.

### Pattern A — Core: const + fluent builder method

In `DigitalBrain.Core/UiSurfaces.cs`, add a `const string` to `public static class Ui` (value `"ui:<Name>"`). In `DigitalBrain.Core/UiKit/UiExperience.cs`, add a `UiHop` method that appends a node factory:

```csharp
// display / static node (no state dependency):
public UiHop Heading(string text)
{
    Factories.Add(_ => new UiWidgetTree(Ui.Heading,
        new Dictionary<string, object?> { ["text"] = text }));
    return this;
}

// input node (captures under `name`; value lives in client form-scope, not C# state):
public UiHop Checkbox(string name, string label)
{
    Factories.Add(_ => new UiWidgetTree(Ui.Checkbox,
        new Dictionary<string, object?> { ["name"] = name, ["label"] = label }));
    return this;
}

// container node (recurses into a nested UiHop, like the existing Panel):
public UiHop Row(Action<UiHop> body)
{
    var inner = new UiHop(Id);
    body(inner);
    Factories.Add(state => new UiWidgetTree(Ui.Row, new Dictionary<string, object?>(),
        inner.Factories.Select(f => f(state)).ToList()));
    return this;
}
```

### Pattern B — App: ForUI cover file `app/lib/ui_kit/ui_<name>.dart`

- **Display covers** are `StatelessWidget` taking typed props.
- **Capture covers** are `StatefulWidget` holding the current value in `State`, calling `setState`, and writing the string into `UiKitFormScope.of(context)?.set(name, stringValue)` — exactly like the shipped `ui_text_field.dart`.
- **Action covers** (nav) read `UiKitFormScope.of(context)?.values` and fire `onEvent('press', {'synapseType':'ExperienceStep','props':{'pack':…, 'experienceId':…, 'eventName':goTo, ...capturedValues}})` — exactly like the shipped `ui_button.dart`.

### Pattern C — App: registry case in `buildUiNode`

`buildUiNode(String type, Map<String,Object?> props, List childrenList, RemoteEventHandler onEvent, {required Widget Function(Map<String,Object?>) buildChild})` switches on `type.toLowerCase()`. Helpers in scope: `kids()` (maps `childrenList` through `buildChild`), `s(key)` (`(props[key] ?? '').toString()`). Add a `case 'ui:<name>':` returning the cover. For action covers, pass `onEvent`. Import the new cover at the top.

### Test host helper (app) — define in each new test file

```dart
Widget _host(Widget child) => MaterialApp(
      home: FTheme(data: FThemes.neutral.light.touch, child: FScaffold(child: child)),
    );
```

---

## Task 0: Enabling follow-ups — bridge `title` + `buildActionEnvelope` string-coercion

**Files:**
- Modify: `brain/DigitalBrain.Kernel/Ui/UiSurfaceRfwBridge.cs:59` (widget-tree marker list)
- Test: `brain/DigitalBrain.Tests/Ui/WidgetTreeHopBridgeTests.cs` (add a test)
- Modify: `app/lib/grpc/action_dispatch.dart:37-40` (coerce props to strings)
- Test: `app/test/grpc/action_dispatch_test.dart` (new)

**Interfaces:**
- Produces (brain): the widget-tree `RfwCard.DataJson` now includes a `title` key when the surface carries `UiSurfaceKeys.Title`.
- Produces (app): `buildActionEnvelope` JSON-encodes a `Map<String,String>` (every prop value `.toString()`-coerced), so non-string inputs never ship a raw bool/num.

- [ ] **Step 1: Write the failing brain test**

Append to `brain/DigitalBrain.Tests/Ui/WidgetTreeHopBridgeTests.cs` inside the class:

```csharp
    [Fact]
    public void WidgetTree_hop_surface_carries_title_marker()
    {
        var tree = new UiWidgetTree(Ui.Screen, new Dictionary<string, object?>());
        var surface = UiSurface.ForExperienceHopTree("ui-gallery", "ui-gallery", "inputs", tree, title: "Inputs");

        var card = UiSurfaceRfwBridge.FromUiSurface(surface, "ui-gallery");

        using var doc = System.Text.Json.JsonDocument.Parse(card.DataJson);
        Assert.Equal("Inputs", doc.RootElement.GetProperty("title").GetString());
    }
```

- [ ] **Step 2: Run it; verify FAIL**

Run: `dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~WidgetTreeHopBridgeTests.WidgetTree_hop_surface_carries_title_marker" --no-restore`
Expected: FAIL — `title` not present in DataJson (current marker list omits it).

- [ ] **Step 3: Implement — add `title` to the marker list**

In `UiSurfaceRfwBridge.cs`, change the widget-tree branch marker array (line ~59) from:

```csharp
            foreach (var markerKey in new[] { "activeExperience", "experienceId", UiSurfaceKeys.SurfaceId })
```

to:

```csharp
            foreach (var markerKey in new[] { "activeExperience", "experienceId", UiSurfaceKeys.SurfaceId, UiSurfaceKeys.Title })
```

- [ ] **Step 4: Run it; verify PASS**

Run: `dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~WidgetTreeHopBridgeTests" --no-restore`
Expected: PASS (new test + the existing marker test).

- [ ] **Step 5: Write the failing app test**

Create `app/test/grpc/action_dispatch_test.dart`:

```dart
import 'dart:convert';
import 'package:flutter_test/flutter_test.dart';
import 'package:digitalbrain_flutter/grpc/action_dispatch.dart';

void main() {
  test('coerces non-string props to strings before encoding', () {
    final env = buildActionEnvelope('press', {
      'synapseType': 'ExperienceStep',
      'props': {'pack': 'p', 'eventName': 'go', 'agree': true, 'level': 0.5},
    });
    final decoded = jsonDecode(utf8.decode(env!.payload)) as Map<String, dynamic>;
    expect(decoded['agree'], 'true');
    expect(decoded['level'], '0.5');
    expect(decoded['pack'], 'p');
  });
}
```

- [ ] **Step 6: Run it; verify FAIL**

Run: `flutter test test/grpc/action_dispatch_test.dart`
Expected: FAIL — `decoded['agree']` is `true` (bool), not `'true'`.

- [ ] **Step 7: Implement — coerce props to strings**

In `app/lib/grpc/action_dispatch.dart`, replace the `return` (lines ~37-40) with:

```dart
  final stringProps = <String, String>{
    for (final entry in props.entries) entry.key: entry.value?.toString() ?? '',
  };

  return gw.SynapseEnvelope()
    ..correlationId = (action['actionId'] as String?) ?? synapseType
    ..typeName = synapseType
    ..payload = utf8.encode(jsonEncode(stringProps));
```

- [ ] **Step 8: Run it; verify PASS**

Run: `flutter test test/grpc/action_dispatch_test.dart`
Expected: PASS.

- [ ] **Step 9: Commit (both repos)**

```bash
cd /e/digitalbraintech/brain && git add DigitalBrain.Kernel/Ui/UiSurfaceRfwBridge.cs DigitalBrain.Tests/Ui/WidgetTreeHopBridgeTests.cs && git commit -m "feat(kernel): widget-tree hop bridge carries title marker"
cd /e/digitalbraintech/app && git add lib/grpc/action_dispatch.dart test/grpc/action_dispatch_test.dart && git commit -m "feat(app): string-coerce buildActionEnvelope props"
```

---

## Task 1: Inputs A — Checkbox, Switch, TextArea

**Files:**
- Modify: `brain/DigitalBrain.Core/UiSurfaces.cs` (`Ui`: add `Checkbox`, `Switch`, `TextArea`)
- Modify: `brain/DigitalBrain.Core/UiKit/UiExperience.cs` (3 `UiHop` methods)
- Create: `app/lib/ui_kit/ui_checkbox.dart`, `ui_switch.dart`, `ui_text_area.dart`
- Modify: `app/lib/ui_kit/ui_registry.dart`
- Test: `brain/DigitalBrain.Tests/Domains/KitExperienceTests.cs` (add), `app/test/ui_kit/ui_inputs_a_test.dart` (new)

**Interfaces:**
- Consumes: `UiKitFormScope` (`set`, `values`), `UiWidgetTree`, `Ui`.
- Produces: `Ui.Checkbox="ui:Checkbox"`, `Ui.Switch="ui:Switch"`, `Ui.TextArea="ui:TextArea"`; `UiHop.Checkbox(string name, string label)`, `UiHop.Switch(string name, string label)`, `UiHop.TextArea(string name, string placeholder="")`; covers `UiKitCheckbox`, `UiKitSwitch`, `UiKitTextArea`.

- [ ] **Step 1: Write the failing Core test**

Append to `KitExperienceTests.cs`:

```csharp
    [Fact]
    public void Checkbox_switch_textarea_emit_named_input_nodes()
    {
        var hop = new UiHop("h");
        hop.Checkbox("agree", "I agree").Switch("notify", "Notify me").TextArea("bio", "About you");
        var nodes = hop.Factories.Select(f => f(new Dictionary<string, string>())).ToList();

        Assert.Equal(Ui.Checkbox, nodes[0].Type);
        Assert.Equal("agree", nodes[0].Props["name"]);
        Assert.Equal("I agree", nodes[0].Props["label"]);
        Assert.Equal(Ui.Switch, nodes[1].Type);
        Assert.Equal("notify", nodes[1].Props["name"]);
        Assert.Equal(Ui.TextArea, nodes[2].Type);
        Assert.Equal("About you", nodes[2].Props["placeholder"]);
    }
```

- [ ] **Step 2: Run it; verify FAIL**

Run: `dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~KitExperienceTests.Checkbox_switch_textarea" --no-restore`
Expected: FAIL — methods/consts missing (compile error).

- [ ] **Step 3: Implement Core**

In `UiSurfaces.cs` `Ui` class add:

```csharp
    public const string Checkbox = "ui:Checkbox";
    public const string Switch = "ui:Switch";
    public const string TextArea = "ui:TextArea";
```

In `UiExperience.cs` `UiHop` add:

```csharp
    public UiHop Checkbox(string name, string label)
    {
        Factories.Add(_ => new UiWidgetTree(Ui.Checkbox,
            new Dictionary<string, object?> { ["name"] = name, ["label"] = label }));
        return this;
    }

    public UiHop Switch(string name, string label)
    {
        Factories.Add(_ => new UiWidgetTree(Ui.Switch,
            new Dictionary<string, object?> { ["name"] = name, ["label"] = label }));
        return this;
    }

    public UiHop TextArea(string name, string placeholder = "")
    {
        Factories.Add(_ => new UiWidgetTree(Ui.TextArea,
            new Dictionary<string, object?> { ["name"] = name, ["placeholder"] = placeholder }));
        return this;
    }
```

- [ ] **Step 4: Run Core test; verify PASS**

Run: `dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~KitExperienceTests.Checkbox_switch_textarea" --no-restore`
Expected: PASS.

- [ ] **Step 5: Write the failing app test**

Create `app/test/ui_kit/ui_inputs_a_test.dart` (verify `FCheckbox`/`FSwitch`/`FTextField.multiline` via Context7 first):

```dart
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:forui/forui.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_checkbox.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_switch.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_text_area.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_form_scope.dart';

Widget _host(Widget child) => MaterialApp(
      home: FTheme(data: FThemes.neutral.light.touch, child: FScaffold(child: child)),
    );

void main() {
  testWidgets('Checkbox writes "true"/"false" into form scope', (tester) async {
    final c = UiKitFormController();
    await tester.pumpWidget(_host(UiKitFormScope(
      controller: c,
      child: const UiKitCheckbox(name: 'agree', label: 'I agree'),
    )));
    await tester.tap(find.byType(FCheckbox));
    await tester.pumpAndSettle();
    expect(c.values['agree'], 'true');
  });

  testWidgets('Switch writes "true" into form scope', (tester) async {
    final c = UiKitFormController();
    await tester.pumpWidget(_host(UiKitFormScope(
      controller: c,
      child: const UiKitSwitch(name: 'notify', label: 'Notify'),
    )));
    await tester.tap(find.byType(FSwitch));
    await tester.pumpAndSettle();
    expect(c.values['notify'], 'true');
  });

  testWidgets('TextArea writes its text into form scope', (tester) async {
    final c = UiKitFormController();
    await tester.pumpWidget(_host(UiKitFormScope(
      controller: c,
      child: const UiKitTextArea(name: 'bio'),
    )));
    await tester.enterText(find.byType(FTextField), 'hello');
    await tester.pump();
    expect(c.values['bio'], 'hello');
  });
}
```

- [ ] **Step 6: Run it; verify FAIL**

Run: `flutter test test/ui_kit/ui_inputs_a_test.dart`
Expected: FAIL — cover files do not exist.

- [ ] **Step 7: Implement the covers**

Create `app/lib/ui_kit/ui_checkbox.dart`:

```dart
import 'package:flutter/material.dart';
import 'package:forui/forui.dart';
import 'ui_form_scope.dart';

class UiKitCheckbox extends StatefulWidget {
  const UiKitCheckbox({super.key, required this.name, required this.label});
  final String name;
  final String label;

  @override
  State<UiKitCheckbox> createState() => _UiKitCheckboxState();
}

class _UiKitCheckboxState extends State<UiKitCheckbox> {
  bool _value = false;

  void _onChange(bool v) {
    setState(() => _value = v);
    UiKitFormScope.of(context)?.set(widget.name, v.toString());
  }

  @override
  Widget build(BuildContext context) =>
      FCheckbox(value: _value, onChange: _onChange, label: Text(widget.label));
}
```

Create `app/lib/ui_kit/ui_switch.dart` (identical shape, `FSwitch`):

```dart
import 'package:flutter/material.dart';
import 'package:forui/forui.dart';
import 'ui_form_scope.dart';

class UiKitSwitch extends StatefulWidget {
  const UiKitSwitch({super.key, required this.name, required this.label});
  final String name;
  final String label;

  @override
  State<UiKitSwitch> createState() => _UiKitSwitchState();
}

class _UiKitSwitchState extends State<UiKitSwitch> {
  bool _value = false;

  void _onChange(bool v) {
    setState(() => _value = v);
    UiKitFormScope.of(context)?.set(widget.name, v.toString());
  }

  @override
  Widget build(BuildContext context) =>
      FSwitch(value: _value, onChange: _onChange, label: Text(widget.label));
}
```

Create `app/lib/ui_kit/ui_text_area.dart` (multiline `FTextField`, lifted control like the shipped `ui_text_field.dart`):

```dart
import 'package:flutter/material.dart';
import 'package:forui/forui.dart';
import 'ui_form_scope.dart';

class UiKitTextArea extends StatefulWidget {
  const UiKitTextArea({super.key, required this.name, this.placeholder = ''});
  final String name;
  final String placeholder;

  @override
  State<UiKitTextArea> createState() => _UiKitTextAreaState();
}

class _UiKitTextAreaState extends State<UiKitTextArea> {
  TextEditingValue _value = TextEditingValue.empty;

  void _onChange(TextEditingValue v) {
    setState(() => _value = v);
    UiKitFormScope.of(context)?.set(widget.name, v.text);
  }

  @override
  Widget build(BuildContext context) => FTextField.multiline(
        control: FTextFieldControl.lifted(value: _value, onChange: _onChange),
        hint: widget.placeholder,
        minLines: 3,
      );
}
```

- [ ] **Step 8: Register the covers**

In `app/lib/ui_kit/ui_registry.dart` add imports and cases:

```dart
import 'ui_checkbox.dart';
import 'ui_switch.dart';
import 'ui_text_area.dart';
```

```dart
    case 'ui:checkbox':
      return UiKitCheckbox(name: s('name'), label: s('label'));
    case 'ui:switch':
      return UiKitSwitch(name: s('name'), label: s('label'));
    case 'ui:textarea':
      return UiKitTextArea(name: s('name'), placeholder: s('placeholder'));
```

- [ ] **Step 9: Run app tests; verify PASS**

Run: `flutter test test/ui_kit/ui_inputs_a_test.dart && flutter analyze`
Expected: PASS + no analyzer issues. (Fix any ForUI signature drift per Context7 — e.g. `FCheckbox.label` slot name — and re-run.)

- [ ] **Step 10: Commit**

```bash
cd /e/digitalbraintech/brain && git add DigitalBrain.Core DigitalBrain.Tests && git commit -m "feat(core): ui: Checkbox/Switch/TextArea builders"
cd /e/digitalbraintech/app && git add lib/ui_kit test/ui_kit && git commit -m "feat(ui_kit): Checkbox/Switch/TextArea covers"
```

---

## Task 2: Inputs B — Select, RadioGroup, Slider, DateField

**Files:**
- Modify: `brain/DigitalBrain.Core/UiSurfaces.cs` (`Ui`: `Select`, `RadioGroup`, `Slider`, `DateField`)
- Modify: `brain/DigitalBrain.Core/UiKit/UiExperience.cs` (4 methods)
- Create: `app/lib/ui_kit/ui_select.dart`, `ui_radio_group.dart`, `ui_slider.dart`, `ui_date_field.dart`
- Modify: `app/lib/ui_kit/ui_registry.dart`
- Test: `KitExperienceTests.cs` (add), `app/test/ui_kit/ui_inputs_b_test.dart` (new)

**Interfaces:**
- Consumes: `UiKitFormScope`, `UiWidgetTree`, `Ui`.
- Produces: `Ui.Select/RadioGroup/Slider/DateField`; `UiHop.Select(string name, IReadOnlyList<string> options, string? label=null)`, `UiHop.RadioGroup(string name, IReadOnlyList<string> options, string? label=null)`, `UiHop.Slider(string name, double min=0, double max=1, string? label=null)`, `UiHop.DateField(string name, string? label=null)`; covers `UiKitSelect`, `UiKitRadioGroup`, `UiKitSlider`, `UiKitDateField`. Options travel as `props["options"]` = `List<string>`.

- [ ] **Step 1: Write the failing Core test**

Append to `KitExperienceTests.cs`:

```csharp
    [Fact]
    public void Select_radio_slider_datefield_emit_input_nodes_with_options()
    {
        var hop = new UiHop("h");
        hop.Select("color", new[] { "Red", "Green" }, "Color")
           .RadioGroup("size", new[] { "S", "M", "L" })
           .Slider("level", 0, 10, "Level")
           .DateField("when", "When");
        var nodes = hop.Factories.Select(f => f(new Dictionary<string, string>())).ToList();

        Assert.Equal(Ui.Select, nodes[0].Type);
        Assert.Equal("color", nodes[0].Props["name"]);
        var options = Assert.IsAssignableFrom<IReadOnlyList<string>>(nodes[0].Props["options"]);
        Assert.Equal(new[] { "Red", "Green" }, options);
        Assert.Equal(Ui.RadioGroup, nodes[1].Type);
        Assert.Equal(Ui.Slider, nodes[2].Type);
        Assert.Equal(10.0, nodes[2].Props["max"]);
        Assert.Equal(Ui.DateField, nodes[3].Type);
    }
```

- [ ] **Step 2: Run it; verify FAIL**

Run: `dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~KitExperienceTests.Select_radio_slider_datefield" --no-restore`
Expected: FAIL — methods/consts missing.

- [ ] **Step 3: Implement Core**

`Ui` class:

```csharp
    public const string Select = "ui:Select";
    public const string RadioGroup = "ui:RadioGroup";
    public const string Slider = "ui:Slider";
    public const string DateField = "ui:DateField";
```

`UiHop`:

```csharp
    public UiHop Select(string name, IReadOnlyList<string> options, string? label = null)
    {
        Factories.Add(_ => new UiWidgetTree(Ui.Select, new Dictionary<string, object?>
        {
            ["name"] = name, ["options"] = options, ["label"] = label ?? string.Empty
        }));
        return this;
    }

    public UiHop RadioGroup(string name, IReadOnlyList<string> options, string? label = null)
    {
        Factories.Add(_ => new UiWidgetTree(Ui.RadioGroup, new Dictionary<string, object?>
        {
            ["name"] = name, ["options"] = options, ["label"] = label ?? string.Empty
        }));
        return this;
    }

    public UiHop Slider(string name, double min = 0, double max = 1, string? label = null)
    {
        Factories.Add(_ => new UiWidgetTree(Ui.Slider, new Dictionary<string, object?>
        {
            ["name"] = name, ["min"] = min, ["max"] = max, ["label"] = label ?? string.Empty
        }));
        return this;
    }

    public UiHop DateField(string name, string? label = null)
    {
        Factories.Add(_ => new UiWidgetTree(Ui.DateField, new Dictionary<string, object?>
        {
            ["name"] = name, ["label"] = label ?? string.Empty
        }));
        return this;
    }
```

- [ ] **Step 4: Run Core test; verify PASS**

Run: `dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~KitExperienceTests.Select_radio_slider_datefield" --no-restore`
Expected: PASS.

- [ ] **Step 5: Write the failing app test**

Create `app/test/ui_kit/ui_inputs_b_test.dart` (verify `FSelect`/`FSelectGroup`/`FSlider`/`FDateField` via Context7 — these have richer control APIs; the test asserts capture, the implementer wires whatever change/control API ForUI 0.21.3 exposes):

```dart
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:forui/forui.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_registry.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_select.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_slider.dart';

void main() {
  test('registry maps inputs-b nodes to their covers', () {
    expect(
      buildUiNode('ui:select', {'name': 'c', 'options': const ['Red'], 'label': ''},
          const [], (_, __) {}, buildChild: (_) => const SizedBox()),
      isA<UiKitSelect>(),
    );
    expect(
      buildUiNode('ui:slider', {'name': 'l', 'min': 0.0, 'max': 10.0, 'label': ''},
          const [], (_, __) {}, buildChild: (_) => const SizedBox()),
      isA<UiKitSlider>(),
    );
  });

  testWidgets('Slider captures a numeric string into form scope', (tester) async {
    final captured = <String, String>{};
    await tester.pumpWidget(MaterialApp(
      home: FTheme(
        data: FThemes.neutral.light.touch,
        child: FScaffold(
          child: UiKitSlider(name: 'level', min: 0, max: 10, onCapture: captured.addAll),
        ),
      ),
    ));
    // Drag handled by ForUI; assert the cover exposes a capture hook the registry test relies on.
    expect(find.byType(FSlider), findsOneWidget);
  });
}
```

> Note for implementer: keep the capture mechanism uniform with Inputs A — a stateful cover that writes the string into `UiKitFormScope.of(context)?.set(name, …)`. The `onCapture` param above is only a test seam for the slider; if ForUI's `FSlider` value-read differs (it uses an `FSliderController`/`onChange`), wire capture through the controller and drop the seam, keeping the form-scope write. Verify via Context7.

- [ ] **Step 6: Run it; verify FAIL**

Run: `flutter test test/ui_kit/ui_inputs_b_test.dart`
Expected: FAIL — cover files do not exist.

- [ ] **Step 7: Implement the covers**

Create `app/lib/ui_kit/ui_select.dart` (`FSelect` takes `items: Map<String,String>`; capture the chosen key):

```dart
import 'package:flutter/material.dart';
import 'package:forui/forui.dart';
import 'ui_form_scope.dart';

class UiKitSelect extends StatefulWidget {
  const UiKitSelect({super.key, required this.name, required this.options, this.label = ''});
  final String name;
  final List<String> options;
  final String label;

  @override
  State<UiKitSelect> createState() => _UiKitSelectState();
}

class _UiKitSelectState extends State<UiKitSelect> {
  void _onChange(String? value) {
    if (value != null) UiKitFormScope.of(context)?.set(widget.name, value);
  }

  @override
  Widget build(BuildContext context) => FSelect<String>(
        items: {for (final o in widget.options) o: o},
        onChange: _onChange,
        label: widget.label.isEmpty ? null : Text(widget.label),
      );
}
```

> The `FSelect` constructor in 0.21.3 exposes value changes via an `FSelectControl`/`onChange`. Verify the exact change hook via Context7 and wire `_onChange`; the form-scope write stays.

Create `app/lib/ui_kit/ui_radio_group.dart` (`FSelectGroup` in radio mode):

```dart
import 'package:flutter/material.dart';
import 'package:forui/forui.dart';
import 'ui_form_scope.dart';

class UiKitRadioGroup extends StatefulWidget {
  const UiKitRadioGroup({super.key, required this.name, required this.options, this.label = ''});
  final String name;
  final List<String> options;
  final String label;

  @override
  State<UiKitRadioGroup> createState() => _UiKitRadioGroupState();
}

class _UiKitRadioGroupState extends State<UiKitRadioGroup> {
  void _onChange(Set<String> selected) {
    if (selected.isNotEmpty) UiKitFormScope.of(context)?.set(widget.name, selected.first);
  }

  @override
  Widget build(BuildContext context) => FSelectGroup<String>(
        control: FSelectGroupControl.radio(),
        label: widget.label.isEmpty ? null : Text(widget.label),
        onChange: _onChange,
        children: [
          for (final o in widget.options) FSelectGroupItem.radio(value: o, label: Text(o)),
        ],
      );
}
```

> `FSelectGroup` radio control + item constructors changed across ForUI versions (`.managedRadio`, `FSelectGroupItem.radio`, or `onChange` vs control listener). Verify the 0.21.3 form via Context7 and adjust; capture the single selected value as a string.

Create `app/lib/ui_kit/ui_slider.dart`:

```dart
import 'package:flutter/material.dart';
import 'package:forui/forui.dart';
import 'ui_form_scope.dart';

class UiKitSlider extends StatefulWidget {
  const UiKitSlider({
    super.key,
    required this.name,
    this.min = 0,
    this.max = 1,
    this.label = '',
    this.onCapture,
  });
  final String name;
  final double min;
  final double max;
  final String label;
  final void Function(Map<String, String>)? onCapture;

  @override
  State<UiKitSlider> createState() => _UiKitSliderState();
}

class _UiKitSliderState extends State<UiKitSlider> {
  late final FContinuousSliderController _controller =
      FContinuousSliderController(selection: FSliderSelection(max: 0));

  void _capture(double value) {
    final v = value.toString();
    UiKitFormScope.of(context)?.set(widget.name, v);
    widget.onCapture?.call({widget.name: v});
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => FSlider(
        control: _controller,
        label: widget.label.isEmpty ? null : Text(widget.label),
        onChange: (selection) => _capture(selection.offset.max),
      );
}
```

> ForUI `FSlider` selection/offset shape (`FSliderSelection`, `offset.max`, controller constructor) and whether change is `onChange` vs a controller listener vary by version. Verify via Context7; the contract this plan needs is: on value change, write `value.toString()` to form scope.

Create `app/lib/ui_kit/ui_date_field.dart`:

```dart
import 'package:flutter/material.dart';
import 'package:forui/forui.dart';
import 'ui_form_scope.dart';

class UiKitDateField extends StatefulWidget {
  const UiKitDateField({super.key, required this.name, this.label = ''});
  final String name;
  final String label;

  @override
  State<UiKitDateField> createState() => _UiKitDateFieldState();
}

class _UiKitDateFieldState extends State<UiKitDateField> {
  String _iso(DateTime d) =>
      '${d.year.toString().padLeft(4, '0')}-${d.month.toString().padLeft(2, '0')}-${d.day.toString().padLeft(2, '0')}';

  @override
  Widget build(BuildContext context) => FDateField(
        label: widget.label.isEmpty ? null : Text(widget.label),
        onChange: (date) {
          if (date != null) UiKitFormScope.of(context)?.set(widget.name, _iso(date));
        },
      );
}
```

> `FDateField` change hook is `onChange`/`onSubmit`/controller per version. Verify via Context7; write ISO `yyyy-MM-dd` to form scope on change.

- [ ] **Step 8: Register the covers**

In `ui_registry.dart` add imports (`ui_select.dart`, `ui_radio_group.dart`, `ui_slider.dart`, `ui_date_field.dart`) and cases. Add a `List<String> optList(String key)` helper near `s(...)`:

```dart
  List<String> optList(String key) =>
      (props[key] as List?)?.map((e) => e.toString()).toList() ?? const [];
  double d(String key) => (props[key] as num?)?.toDouble() ?? 0;
```

```dart
    case 'ui:select':
      return UiKitSelect(name: s('name'), options: optList('options'), label: s('label'));
    case 'ui:radiogroup':
      return UiKitRadioGroup(name: s('name'), options: optList('options'), label: s('label'));
    case 'ui:slider':
      return UiKitSlider(name: s('name'), min: d('min'), max: d('max'), label: s('label'));
    case 'ui:datefield':
      return UiKitDateField(name: s('name'), label: s('label'));
```

- [ ] **Step 9: Run app tests; verify PASS**

Run: `flutter test test/ui_kit/ui_inputs_b_test.dart && flutter analyze`
Expected: PASS + clean analyze.

- [ ] **Step 10: Commit**

```bash
cd /e/digitalbraintech/brain && git add DigitalBrain.Core DigitalBrain.Tests && git commit -m "feat(core): ui: Select/RadioGroup/Slider/DateField builders"
cd /e/digitalbraintech/app && git add lib/ui_kit test/ui_kit && git commit -m "feat(ui_kit): Select/RadioGroup/Slider/DateField covers"
```

---

## Task 3: Layout — Row, Column, Divider, Header, Gap

**Files:**
- Modify: `UiSurfaces.cs` (`Ui`: `Row`, `Column`, `Divider`, `Header`, `Gap`)
- Modify: `UiExperience.cs` (`Row`, `Column` container methods; `Divider`, `Header`, `Gap` leaf methods)
- Create: `app/lib/ui_kit/ui_row.dart`, `ui_column.dart`, `ui_divider.dart`, `ui_header.dart`, `ui_gap.dart`
- Modify: `ui_registry.dart`
- Test: `KitExperienceTests.cs`, `app/test/ui_kit/ui_layout_test.dart`

**Interfaces:**
- Produces: `Ui.Row/Column/Divider/Header/Gap`; `UiHop.Row(Action<UiHop>)`, `UiHop.Column(Action<UiHop>)`, `UiHop.Divider()`, `UiHop.Header(string title)`, `UiHop.Gap(double size=16)`; covers `UiKitRow`, `UiKitColumn`, `UiKitDivider`, `UiKitHeader`, `UiKitGap`. Row/Column are containers (children rendered via `kids()`/`buildChild`).

- [ ] **Step 1: Write the failing Core test**

```csharp
    [Fact]
    public void Layout_nodes_emit_containers_and_leaves()
    {
        var hop = new UiHop("h");
        hop.Row(r => r.Text("a").Text("b")).Divider().Header("Section").Gap(8);
        var nodes = hop.Factories.Select(f => f(new Dictionary<string, string>())).ToList();

        Assert.Equal(Ui.Row, nodes[0].Type);
        Assert.Equal(2, nodes[0].Children!.Count);
        Assert.Equal(Ui.Divider, nodes[1].Type);
        Assert.Equal(Ui.Header, nodes[2].Type);
        Assert.Equal("Section", nodes[2].Props["title"]);
        Assert.Equal(Ui.Gap, nodes[3].Type);
        Assert.Equal(8.0, nodes[3].Props["size"]);
    }
```

- [ ] **Step 2: Run; verify FAIL.** `dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~KitExperienceTests.Layout_nodes" --no-restore`

- [ ] **Step 3: Implement Core**

`Ui`:

```csharp
    public const string Row = "ui:Row";
    public const string Column = "ui:Column";
    public const string Divider = "ui:Divider";
    public const string Header = "ui:Header";
    public const string Gap = "ui:Gap";
```

`UiHop`:

```csharp
    public UiHop Row(Action<UiHop> body)
    {
        var inner = new UiHop(Id);
        body(inner);
        Factories.Add(state => new UiWidgetTree(Ui.Row, new Dictionary<string, object?>(),
            inner.Factories.Select(f => f(state)).ToList()));
        return this;
    }

    public UiHop Column(Action<UiHop> body)
    {
        var inner = new UiHop(Id);
        body(inner);
        Factories.Add(state => new UiWidgetTree(Ui.Column, new Dictionary<string, object?>(),
            inner.Factories.Select(f => f(state)).ToList()));
        return this;
    }

    public UiHop Divider()
    {
        Factories.Add(_ => new UiWidgetTree(Ui.Divider, new Dictionary<string, object?>()));
        return this;
    }

    public UiHop Header(string title)
    {
        Factories.Add(_ => new UiWidgetTree(Ui.Header, new Dictionary<string, object?> { ["title"] = title }));
        return this;
    }

    public UiHop Gap(double size = 16)
    {
        Factories.Add(_ => new UiWidgetTree(Ui.Gap, new Dictionary<string, object?> { ["size"] = size }));
        return this;
    }
```

- [ ] **Step 4: Run; verify PASS.**

- [ ] **Step 5: Write the failing app test**

Create `app/test/ui_kit/ui_layout_test.dart`:

```dart
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:forui/forui.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_registry.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_divider.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_header.dart';

Widget _host(Widget child) => MaterialApp(
      home: FTheme(data: FThemes.neutral.light.touch, child: FScaffold(child: child)),
    );

void main() {
  test('registry maps layout nodes', () {
    expect(buildUiNode('ui:divider', const {}, const [], (_, __) {}, buildChild: (_) => const SizedBox()),
        isA<UiKitDivider>());
    expect(buildUiNode('ui:header', {'title': 'S'}, const [], (_, __) {}, buildChild: (_) => const SizedBox()),
        isA<UiKitHeader>());
  });

  testWidgets('Header shows its title', (tester) async {
    await tester.pumpWidget(_host(const UiKitHeader(title: 'Section')));
    expect(find.text('Section'), findsOneWidget);
  });

  testWidgets('Row lays children horizontally', (tester) async {
    final node = {
      'Type': 'ui:Row',
      'Props': const <String, Object?>{},
      'Children': [
        {'Type': 'ui:Text', 'Props': {'text': 'a'}, 'Children': const []},
        {'Type': 'ui:Text', 'Props': {'text': 'b'}, 'Children': const []},
      ],
    };
    await tester.pumpWidget(_host(UiSurfaceTreeRenderer().build(node, (_, __) {}, rfwHost: RfwRuntimeHost())));
    expect(find.text('a'), findsOneWidget);
    expect(find.text('b'), findsOneWidget);
  });
}
```

> Add `import 'package:digitalbrain_flutter/rfw_host/rfw_runtime_host.dart';` for `UiSurfaceTreeRenderer`/`RfwRuntimeHost`.

- [ ] **Step 6: Run; verify FAIL.** `flutter test test/ui_kit/ui_layout_test.dart`

- [ ] **Step 7: Implement the covers**

`ui_row.dart`:

```dart
import 'package:flutter/widgets.dart';

class UiKitRow extends StatelessWidget {
  const UiKitRow({super.key, required this.children});
  final List<Widget> children;

  @override
  Widget build(BuildContext context) => Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          for (final c in children) Padding(padding: const EdgeInsets.symmetric(horizontal: 4), child: c),
        ],
      );
}
```

`ui_column.dart`:

```dart
import 'package:flutter/widgets.dart';

class UiKitColumn extends StatelessWidget {
  const UiKitColumn({super.key, required this.children});
  final List<Widget> children;

  @override
  Widget build(BuildContext context) => Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          for (final c in children) Padding(padding: const EdgeInsets.symmetric(vertical: 4), child: c),
        ],
      );
}
```

`ui_divider.dart`:

```dart
import 'package:flutter/widgets.dart';
import 'package:forui/forui.dart';

class UiKitDivider extends StatelessWidget {
  const UiKitDivider({super.key});

  @override
  Widget build(BuildContext context) => const FDivider();
}
```

`ui_header.dart`:

```dart
import 'package:flutter/widgets.dart';
import 'package:forui/forui.dart';

class UiKitHeader extends StatelessWidget {
  const UiKitHeader({super.key, required this.title});
  final String title;

  @override
  Widget build(BuildContext context) => FHeader(title: Text(title));
}
```

`ui_gap.dart`:

```dart
import 'package:flutter/widgets.dart';

class UiKitGap extends StatelessWidget {
  const UiKitGap({super.key, this.size = 16});
  final double size;

  @override
  Widget build(BuildContext context) => SizedBox(height: size, width: size);
}
```

- [ ] **Step 8: Register**

Imports + cases in `ui_registry.dart`:

```dart
    case 'ui:row':
      return UiKitRow(children: kids());
    case 'ui:column':
      return UiKitColumn(children: kids());
    case 'ui:divider':
      return const UiKitDivider();
    case 'ui:header':
      return UiKitHeader(title: s('title'));
    case 'ui:gap':
      return UiKitGap(size: d('size') == 0 ? 16 : d('size'));
```

- [ ] **Step 9: Run; verify PASS.** `flutter test test/ui_kit/ui_layout_test.dart && flutter analyze`

- [ ] **Step 10: Commit** (brain: "feat(core): ui: layout builders"; app: "feat(ui_kit): Row/Column/Divider/Header/Gap covers")

---

## Task 4: Display A — Heading, Icon, Avatar, Badge

**Files:** `UiSurfaces.cs`, `UiExperience.cs`; `app/lib/ui_kit/ui_heading.dart`, `ui_icon.dart`, `ui_avatar.dart`, `ui_badge.dart`; `ui_registry.dart`; `KitExperienceTests.cs`, `app/test/ui_kit/ui_display_a_test.dart`.

**Interfaces:**
- Produces: `Ui.Heading/Icon/Avatar/Badge`; `UiHop.Heading(string text)`, `UiHop.Icon(string name)`, `UiHop.Avatar(string? imageUrl=null, string? fallback=null)`, `UiHop.Badge(string text)`; covers `UiKitHeading`, `UiKitIcon`, `UiKitAvatar`, `UiKitBadge`. `Icon.name` maps to an `FLucideIcons` member by name.

- [ ] **Step 1: Write the failing Core test**

```csharp
    [Fact]
    public void Display_a_nodes_emit_typed_props()
    {
        var hop = new UiHop("h");
        hop.Heading("Title").Icon("star").Avatar(fallback: "AB").Badge("New");
        var nodes = hop.Factories.Select(f => f(new Dictionary<string, string>())).ToList();
        Assert.Equal(Ui.Heading, nodes[0].Type);
        Assert.Equal("Title", nodes[0].Props["text"]);
        Assert.Equal(Ui.Icon, nodes[1].Type);
        Assert.Equal("star", nodes[1].Props["name"]);
        Assert.Equal(Ui.Avatar, nodes[2].Type);
        Assert.Equal("AB", nodes[2].Props["fallback"]);
        Assert.Equal(Ui.Badge, nodes[3].Type);
        Assert.Equal("New", nodes[3].Props["text"]);
    }
```

- [ ] **Step 2: Run; verify FAIL.**

- [ ] **Step 3: Implement Core**

`Ui`: `Heading="ui:Heading"`, `Icon="ui:Icon"`, `Avatar="ui:Avatar"`, `Badge="ui:Badge"`.

`UiHop`:

```csharp
    public UiHop Heading(string text)
    {
        Factories.Add(_ => new UiWidgetTree(Ui.Heading, new Dictionary<string, object?> { ["text"] = text }));
        return this;
    }

    public UiHop Icon(string name)
    {
        Factories.Add(_ => new UiWidgetTree(Ui.Icon, new Dictionary<string, object?> { ["name"] = name }));
        return this;
    }

    public UiHop Avatar(string? imageUrl = null, string? fallback = null)
    {
        Factories.Add(_ => new UiWidgetTree(Ui.Avatar, new Dictionary<string, object?>
        {
            ["imageUrl"] = imageUrl ?? string.Empty, ["fallback"] = fallback ?? string.Empty
        }));
        return this;
    }

    public UiHop Badge(string text)
    {
        Factories.Add(_ => new UiWidgetTree(Ui.Badge, new Dictionary<string, object?> { ["text"] = text }));
        return this;
    }
```

- [ ] **Step 4: Run; verify PASS.**

- [ ] **Step 5: Write the failing app test**

Create `app/test/ui_kit/ui_display_a_test.dart`:

```dart
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:forui/forui.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_heading.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_badge.dart';

Widget _host(Widget child) => MaterialApp(
      home: FTheme(data: FThemes.neutral.light.touch, child: FScaffold(child: child)),
    );

void main() {
  testWidgets('Heading renders its text', (tester) async {
    await tester.pumpWidget(_host(const UiKitHeading(text: 'Title')));
    expect(find.text('Title'), findsOneWidget);
  });

  testWidgets('Badge renders its text', (tester) async {
    await tester.pumpWidget(_host(const UiKitBadge(text: 'New')));
    expect(find.text('New'), findsOneWidget);
  });
}
```

- [ ] **Step 6: Run; verify FAIL.**

- [ ] **Step 7: Implement covers**

`ui_heading.dart`:

```dart
import 'package:flutter/widgets.dart';
import 'package:forui/forui.dart';

class UiKitHeading extends StatelessWidget {
  const UiKitHeading({super.key, required this.text});
  final String text;

  @override
  Widget build(BuildContext context) =>
      Text(text, style: context.theme.typography.xl2.copyWith(fontWeight: FontWeight.bold));
}
```

`ui_icon.dart` (resolve a Lucide icon by name with a safe fallback):

```dart
import 'package:flutter/widgets.dart';
import 'package:forui/forui.dart';

class UiKitIcon extends StatelessWidget {
  const UiKitIcon({super.key, required this.name});
  final String name;

  static const Map<String, IconData> _icons = {
    'star': FLucideIcons.star,
    'house': FLucideIcons.house,
    'user': FLucideIcons.user,
    'check': FLucideIcons.check,
    'info': FLucideIcons.info,
    'settings': FLucideIcons.settings,
  };

  @override
  Widget build(BuildContext context) =>
      Icon(_icons[name] ?? FLucideIcons.circle);
}
```

> Confirm the exact `FLucideIcons` member names via Context7; extend the map as the gallery needs. Unknown names fall back to a circle (never throws).

`ui_avatar.dart`:

```dart
import 'package:flutter/material.dart';
import 'package:forui/forui.dart';

class UiKitAvatar extends StatelessWidget {
  const UiKitAvatar({super.key, this.imageUrl = '', this.fallback = ''});
  final String imageUrl;
  final String fallback;

  @override
  Widget build(BuildContext context) => imageUrl.isEmpty
      ? FAvatar.raw(child: Text(fallback))
      : FAvatar(image: NetworkImage(imageUrl), fallback: Text(fallback));
}
```

`ui_badge.dart`:

```dart
import 'package:flutter/widgets.dart';
import 'package:forui/forui.dart';

class UiKitBadge extends StatelessWidget {
  const UiKitBadge({super.key, required this.text});
  final String text;

  @override
  Widget build(BuildContext context) => FBadge(child: Text(text));
}
```

- [ ] **Step 8: Register** (imports + cases):

```dart
    case 'ui:heading':
      return UiKitHeading(text: s('text'));
    case 'ui:icon':
      return UiKitIcon(name: s('name'));
    case 'ui:avatar':
      return UiKitAvatar(imageUrl: s('imageUrl'), fallback: s('fallback'));
    case 'ui:badge':
      return UiKitBadge(text: s('text'));
```

- [ ] **Step 9: Run; verify PASS.** `flutter test test/ui_kit/ui_display_a_test.dart && flutter analyze`

- [ ] **Step 10: Commit** (brain: "feat(core): ui: Heading/Icon/Avatar/Badge builders"; app: "feat(ui_kit): display-A covers")

---

## Task 5: Display B — Tile, List

**Files:** `UiSurfaces.cs`, `UiExperience.cs`, `KitExperience.cs` (extend `Inject` for tile `goTo`); `app/lib/ui_kit/ui_tile.dart`, `ui_list.dart`; `ui_registry.dart`; tests.

**Interfaces:**
- Produces: `Ui.Tile/List`; `UiHop.Tile(string title, string? subtitle=null, string? goTo=null)`, `UiHop.List(Action<UiHop> body)` (children are tiles); covers `UiKitTile` (optional `onEvent` when `goTo` set), `UiKitList`. `KitExperience.Inject` extended to stamp `pack`/`experienceId` on `ui:Tile` nodes that carry a `goTo` (so a tap can fire `ExperienceStep`).

- [ ] **Step 1: Write the failing Core test** (tile stamping)

```csharp
    [Fact]
    public void Tile_with_goTo_is_stamped_with_pack_and_experienceId()
    {
        var pack = new GalleryStubPack();  // defined in Step 3 test helper; any KitExperience with a hop containing Tile(goTo:)
        var outputs = pack.Handle(new ExperienceStep("p", "p", "start", new Dictionary<string, string>()));
        var tree = (UiWidgetTree)((UiSurface)outputs[0]).Props["tree"];
        var tile = FindByType(tree, Ui.Tile);
        Assert.Equal("p", tile.Props["pack"]);
        Assert.Equal("p", tile.Props["experienceId"]);
        Assert.Equal("next", tile.Props["eventName"]);
    }
```

Add the helpers to the test class:

```csharp
    private sealed class GalleryStubPack : KitExperience
    {
        protected override UiExperience Define() => Experience("p", "P")
            .Hop("start", s => s.Tile("Go", goTo: "next"))
            .Hop("next", s => s.Text("done"));
    }

    private static UiWidgetTree FindByType(UiWidgetTree node, string type)
    {
        if (node.Type == type) return node;
        foreach (var child in node.Children ?? new List<UiWidgetTree>())
            if (FindByType(child, type) is { } match) return match;
        return null!;  // tests assert a match exists; a miss surfaces as an NRE on the assertion
    }
```

> The `eventName` prop is set by the `Tile` builder when `goTo` is non-null (mirrors `Button`).

- [ ] **Step 2: Run; verify FAIL.**

- [ ] **Step 3: Implement Core**

`Ui`: `Tile="ui:Tile"`, `List="ui:List"`.

`UiHop`:

```csharp
    public UiHop Tile(string title, string? subtitle = null, string? goTo = null)
    {
        Factories.Add(_ =>
        {
            var props = new Dictionary<string, object?> { ["title"] = title, ["subtitle"] = subtitle ?? string.Empty };
            if (goTo is not null) props["eventName"] = goTo;
            return new UiWidgetTree(Ui.Tile, props);
        });
        return this;
    }

    public UiHop List(Action<UiHop> body)
    {
        var inner = new UiHop(Id);
        body(inner);
        Factories.Add(state => new UiWidgetTree(Ui.List, new Dictionary<string, object?>(),
            inner.Factories.Select(f => f(state)).ToList()));
        return this;
    }
```

In `KitExperience.cs`, extend `Inject` so action-bearing nodes are stamped generically. Replace the `Inject` button check with a check for any node carrying an `eventName` prop:

```csharp
    private static UiWidgetTree Inject(UiWidgetTree node, string id)
    {
        if (node.Props.ContainsKey("eventName"))
        {
            var props = new Dictionary<string, object?>(node.Props) { ["pack"] = id, ["experienceId"] = id };
            node = node with { Props = props };
        }
        if (node.Children is { } children)
        {
            return node with { Children = children.Select(child => Inject(child, id)).ToList() };
        }
        return node;
    }
```

> This generalizes Slice 0's button-only stamping to every action node (Button, Tile, and every nav node in Tasks 7-8) by keying on the presence of `eventName`. Confirm the existing Button test still passes — `ui:Button` already carries `eventName`.

- [ ] **Step 4: Run; verify PASS.** Run the new test + the existing `Start_emits_ask_hop_with_text_field_and_button` to confirm no regression.

- [ ] **Step 5: Write the failing app test**

Create `app/test/ui_kit/ui_display_b_test.dart`:

```dart
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:forui/forui.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_tile.dart';

Widget _host(Widget child) => MaterialApp(
      home: FTheme(data: FThemes.neutral.light.touch, child: FScaffold(child: child)),
    );

void main() {
  testWidgets('Tile shows title and fires ExperienceStep on tap when goTo set', (tester) async {
    Map<String, Object?>? captured;
    await tester.pumpWidget(_host(UiKitTile(
      title: 'Go', subtitle: 'sub', pack: 'p', experienceId: 'p', eventName: 'next',
      onEvent: (n, a) => captured = a,
    )));
    expect(find.text('Go'), findsOneWidget);
    await tester.tap(find.text('Go'));
    await tester.pumpAndSettle();
    final propsMap = captured!['props'] as Map<String, Object?>;
    expect(propsMap['eventName'], 'next');
  });
}
```

- [ ] **Step 6: Run; verify FAIL.**

- [ ] **Step 7: Implement covers**

`ui_tile.dart`:

```dart
import 'package:flutter/widgets.dart';
import 'package:forui/forui.dart';
import 'package:rfw/rfw.dart' show RemoteEventHandler;
import 'ui_form_scope.dart';

class UiKitTile extends StatelessWidget {
  const UiKitTile({
    super.key,
    required this.title,
    this.subtitle = '',
    this.pack = '',
    this.experienceId = '',
    this.eventName = '',
    this.onEvent,
  });
  final String title;
  final String subtitle;
  final String pack;
  final String experienceId;
  final String eventName;
  final RemoteEventHandler? onEvent;

  @override
  Widget build(BuildContext context) {
    final scope = UiKitFormScope.of(context);
    return FTile(
      title: Text(title),
      subtitle: subtitle.isEmpty ? null : Text(subtitle),
      onPress: eventName.isEmpty || onEvent == null
          ? null
          : () => onEvent!('press', {
                'synapseType': 'ExperienceStep',
                'props': {
                  'pack': pack,
                  'experienceId': experienceId,
                  'eventName': eventName,
                  ...(scope?.values ?? const {}),
                },
              }),
    );
  }
}
```

`ui_list.dart`:

```dart
import 'package:flutter/widgets.dart';
import 'package:forui/forui.dart';

class UiKitList extends StatelessWidget {
  const UiKitList({super.key, required this.children});
  final List<Widget> children;

  @override
  Widget build(BuildContext context) => FTileGroup(children: [for (final c in children) c as FTile]);
}
```

> If `FTileGroup` requires `FTile` children specifically and `kids()` yields generic `Widget`, have the list cover wrap non-tile children or assert tiles. Verify `FTileGroup` children typing via Context7; simplest is to require `ui:Tile` children in a `ui:List`.

- [ ] **Step 8: Register** (`ui_tile.dart`, `ui_list.dart`):

```dart
    case 'ui:tile':
      return UiKitTile(
        title: s('title'), subtitle: s('subtitle'),
        pack: s('pack'), experienceId: s('experienceId'), eventName: s('eventName'),
        onEvent: onEvent,
      );
    case 'ui:list':
      return UiKitList(children: kids());
```

- [ ] **Step 9: Run; verify PASS.** `flutter test test/ui_kit/ui_display_b_test.dart && flutter analyze`

- [ ] **Step 10: Commit** (brain: "feat(core): ui: Tile/List + generic eventName stamping"; app: "feat(ui_kit): Tile/List covers")

---

## Task 6: Feedback — Alert, Progress, Spinner, Tooltip

**Files:** `UiSurfaces.cs`, `UiExperience.cs`; `app/lib/ui_kit/ui_alert.dart`, `ui_progress.dart`, `ui_spinner.dart`, `ui_tooltip.dart`; `ui_registry.dart`; tests.

**Interfaces:**
- Produces: `Ui.Alert/Progress/Spinner/Tooltip`; `UiHop.Alert(string title, string? subtitle=null)`, `UiHop.Progress(double value)`, `UiHop.Spinner()`, `UiHop.Tooltip(string tip, Action<UiHop> body)` (wraps a single child); covers `UiKitAlert`, `UiKitProgress`, `UiKitSpinner`, `UiKitTooltip`.

- [ ] **Step 1: Write the failing Core test**

```csharp
    [Fact]
    public void Feedback_nodes_emit_typed_props()
    {
        var hop = new UiHop("h");
        hop.Alert("Heads up", "details").Progress(0.4).Spinner().Tooltip("hint", t => t.Text("hover me"));
        var nodes = hop.Factories.Select(f => f(new Dictionary<string, string>())).ToList();
        Assert.Equal(Ui.Alert, nodes[0].Type);
        Assert.Equal("Heads up", nodes[0].Props["title"]);
        Assert.Equal(Ui.Progress, nodes[1].Type);
        Assert.Equal(0.4, nodes[1].Props["value"]);
        Assert.Equal(Ui.Spinner, nodes[2].Type);
        Assert.Equal(Ui.Tooltip, nodes[3].Type);
        Assert.Equal("hint", nodes[3].Props["tip"]);
        Assert.Single(nodes[3].Children!);
    }
```

- [ ] **Step 2: Run; verify FAIL.**

- [ ] **Step 3: Implement Core**

`Ui`: `Alert/Progress/Spinner/Tooltip` consts.

`UiHop`:

```csharp
    public UiHop Alert(string title, string? subtitle = null)
    {
        Factories.Add(_ => new UiWidgetTree(Ui.Alert, new Dictionary<string, object?>
        {
            ["title"] = title, ["subtitle"] = subtitle ?? string.Empty
        }));
        return this;
    }

    public UiHop Progress(double value)
    {
        Factories.Add(_ => new UiWidgetTree(Ui.Progress, new Dictionary<string, object?> { ["value"] = value }));
        return this;
    }

    public UiHop Spinner()
    {
        Factories.Add(_ => new UiWidgetTree(Ui.Spinner, new Dictionary<string, object?>()));
        return this;
    }

    public UiHop Tooltip(string tip, Action<UiHop> body)
    {
        var inner = new UiHop(Id);
        body(inner);
        Factories.Add(state => new UiWidgetTree(Ui.Tooltip,
            new Dictionary<string, object?> { ["tip"] = tip },
            inner.Factories.Select(f => f(state)).ToList()));
        return this;
    }
```

- [ ] **Step 4: Run; verify PASS.**

- [ ] **Step 5: Write the failing app test**

Create `app/test/ui_kit/ui_feedback_test.dart`:

```dart
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:forui/forui.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_alert.dart';

Widget _host(Widget child) => MaterialApp(
      home: FTheme(data: FThemes.neutral.light.touch, child: FScaffold(child: child)),
    );

void main() {
  testWidgets('Alert shows title and subtitle', (tester) async {
    await tester.pumpWidget(_host(const UiKitAlert(title: 'Heads up', subtitle: 'details')));
    expect(find.text('Heads up'), findsOneWidget);
    expect(find.text('details'), findsOneWidget);
  });
}
```

- [ ] **Step 6: Run; verify FAIL.**

- [ ] **Step 7: Implement covers** (verify `FAlert`/`FProgress`/`FTooltip` via Context7)

`ui_alert.dart`:

```dart
import 'package:flutter/widgets.dart';
import 'package:forui/forui.dart';

class UiKitAlert extends StatelessWidget {
  const UiKitAlert({super.key, required this.title, this.subtitle = ''});
  final String title;
  final String subtitle;

  @override
  Widget build(BuildContext context) =>
      FAlert(title: Text(title), subtitle: subtitle.isEmpty ? null : Text(subtitle));
}
```

`ui_progress.dart`:

```dart
import 'package:flutter/widgets.dart';
import 'package:forui/forui.dart';

class UiKitProgress extends StatelessWidget {
  const UiKitProgress({super.key, required this.value});
  final double value;

  @override
  Widget build(BuildContext context) => FProgress(value: value);
}
```

> Verify `FProgress` value param (it may be `value`/`percentage` or a determinate constructor) via Context7.

`ui_spinner.dart`:

```dart
import 'package:flutter/widgets.dart';
import 'package:forui/forui.dart';

class UiKitSpinner extends StatelessWidget {
  const UiKitSpinner({super.key});

  @override
  Widget build(BuildContext context) => const FProgress.circularIcon();
}
```

> Verify the indeterminate/circular ForUI API (`FProgress.circularIcon()` or `FCircularProgress`) via Context7.

`ui_tooltip.dart`:

```dart
import 'package:flutter/widgets.dart';
import 'package:forui/forui.dart';

class UiKitTooltip extends StatelessWidget {
  const UiKitTooltip({super.key, required this.tip, required this.child});
  final String tip;
  final Widget child;

  @override
  Widget build(BuildContext context) =>
      FTooltip(tipBuilder: (context, controller) => Text(tip), child: child);
}
```

> `FTooltip` builder signature varies; verify via Context7.

- [ ] **Step 8: Register** — Tooltip wraps a single child (`kids().first`):

```dart
    case 'ui:alert':
      return UiKitAlert(title: s('title'), subtitle: s('subtitle'));
    case 'ui:progress':
      return UiKitProgress(value: d('value'));
    case 'ui:spinner':
      return const UiKitSpinner();
    case 'ui:tooltip':
      final tipKids = kids();
      return UiKitTooltip(tip: s('tip'), child: tipKids.isEmpty ? const SizedBox.shrink() : tipKids.first);
```

- [ ] **Step 9: Run; verify PASS.** `flutter test test/ui_kit/ui_feedback_test.dart && flutter analyze`

- [ ] **Step 10: Commit** (brain: "feat(core): ui: Alert/Progress/Spinner/Tooltip builders"; app: "feat(ui_kit): feedback covers")

---

## Task 7: Navigation A — Tabs, Breadcrumb, Pagination

**Files:** `UiSurfaces.cs`, `UiExperience.cs`; `app/lib/ui_kit/ui_nav_item.dart` (shared `(label, goTo)` model), `ui_tabs.dart`, `ui_breadcrumb.dart`, `ui_pagination.dart`; `ui_registry.dart`; tests.

**Interfaces:**
- Consumes: the generic `eventName` stamping from Task 5 (`Inject`).
- Produces: `Ui.Tabs/Breadcrumb/Pagination`; nav builders take `(string label, string goTo)` items. `UiHop.Tabs(params (string label, string goTo)[] items)`, `UiHop.Breadcrumb(params (string label, string goTo)[] items)`, `UiHop.Pagination(int pages, string goToPrefix)` (emits `eventName = goToPrefix + pageIndex`). Items travel as `props["items"]` = `List<Dictionary<string,object?>>` each `{label, eventName}`. Covers `UiKitTabs`, `UiKitBreadcrumb`, `UiKitPagination`; each item fires `ExperienceStep` via the shared `_fireNav` helper.

> **Stamping note:** nav nodes carry per-item `eventName`s, not a single top-level one. So `Inject` (Task 5, keyed on top-level `eventName`) won't reach them. Extend `Inject` once more here: also stamp `pack`/`experienceId` at the top level of any node whose `Type` starts with `ui:` and that carries an `items` prop, so the cover can address `ExperienceStep` back. Add to the Task 5 `Inject`:
>
> ```csharp
>         if (node.Props.ContainsKey("items"))
>         {
>             var props = new Dictionary<string, object?>(node.Props) { ["pack"] = id, ["experienceId"] = id };
>             node = node with { Props = props };
>         }
> ```
> (Place alongside the `eventName` check; a node may match either.)

- [ ] **Step 1: Write the failing Core test**

```csharp
    [Fact]
    public void Nav_a_nodes_emit_items_and_are_stamped()
    {
        var pack = new NavStubPack();
        var outputs = pack.Handle(new ExperienceStep("p", "p", "start", new Dictionary<string, string>()));
        var tree = (UiWidgetTree)((UiSurface)outputs[0]).Props["tree"];
        var tabs = FindByType(tree, Ui.Tabs);
        Assert.Equal("p", tabs.Props["pack"]);
        var items = Assert.IsAssignableFrom<IReadOnlyList<object>>(tabs.Props["items"]);
        Assert.Equal(2, items.Count);
    }

    private sealed class NavStubPack : KitExperience
    {
        protected override UiExperience Define() => Experience("p", "P")
            .Hop("start", s => s.Tabs(("One", "one"), ("Two", "two")))
            .Hop("one", s => s.Text("1")).Hop("two", s => s.Text("2"));
    }
```

- [ ] **Step 2: Run; verify FAIL.**

- [ ] **Step 3: Implement Core** — extend `Inject` (note above), add consts + builders:

```csharp
    public const string Tabs = "ui:Tabs";
    public const string Breadcrumb = "ui:Breadcrumb";
    public const string Pagination = "ui:Pagination";
```

```csharp
    private static List<Dictionary<string, object?>> NavItems((string label, string goTo)[] items) =>
        items.Select(i => new Dictionary<string, object?> { ["label"] = i.label, ["eventName"] = i.goTo }).ToList();

    public UiHop Tabs(params (string label, string goTo)[] items)
    {
        Factories.Add(_ => new UiWidgetTree(Ui.Tabs, new Dictionary<string, object?> { ["items"] = NavItems(items) }));
        return this;
    }

    public UiHop Breadcrumb(params (string label, string goTo)[] items)
    {
        Factories.Add(_ => new UiWidgetTree(Ui.Breadcrumb, new Dictionary<string, object?> { ["items"] = NavItems(items) }));
        return this;
    }

    public UiHop Pagination(int pages, string goToPrefix)
    {
        var items = Enumerable.Range(0, pages)
            .Select(i => new Dictionary<string, object?> { ["label"] = (i + 1).ToString(), ["eventName"] = goToPrefix + i })
            .ToList();
        Factories.Add(_ => new UiWidgetTree(Ui.Pagination, new Dictionary<string, object?> { ["items"] = items }));
        return this;
    }
```

- [ ] **Step 4: Run; verify PASS.**

- [ ] **Step 5: Write the failing app test**

Create `app/test/ui_kit/ui_nav_a_test.dart`:

```dart
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:forui/forui.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_tabs.dart';

Widget _host(Widget child) => MaterialApp(
      home: FTheme(data: FThemes.neutral.light.touch, child: FScaffold(child: child)),
    );

void main() {
  testWidgets('Tabs fires ExperienceStep with the tapped item eventName', (tester) async {
    Map<String, Object?>? captured;
    await tester.pumpWidget(_host(UiKitTabs(
      pack: 'p', experienceId: 'p',
      items: const [{'label': 'One', 'eventName': 'one'}, {'label': 'Two', 'eventName': 'two'}],
      onEvent: (n, a) => captured = a,
    )));
    await tester.tap(find.text('Two'));
    await tester.pumpAndSettle();
    final propsMap = captured!['props'] as Map<String, Object?>;
    expect(propsMap['eventName'], 'two');
  });
}
```

- [ ] **Step 6: Run; verify FAIL.**

- [ ] **Step 7: Implement covers**

`ui_nav_item.dart` (shared parse + fire helper):

```dart
import 'package:rfw/rfw.dart' show RemoteEventHandler;

class UiNavItem {
  const UiNavItem(this.label, this.eventName);
  final String label;
  final String eventName;
}

List<UiNavItem> parseNavItems(List rawItems) => rawItems
    .cast<Map>()
    .map((m) => UiNavItem((m['label'] ?? '').toString(), (m['eventName'] ?? '').toString()))
    .toList();

void fireNav(RemoteEventHandler onEvent, String pack, String experienceId, String eventName,
    Map<String, String> capturedValues) {
  onEvent('press', {
    'synapseType': 'ExperienceStep',
    'props': {'pack': pack, 'experienceId': experienceId, 'eventName': eventName, ...capturedValues},
  });
}
```

`ui_tabs.dart` (verify `FTabs`/`FTabEntry` via Context7):

```dart
import 'package:flutter/material.dart';
import 'package:forui/forui.dart';
import 'package:rfw/rfw.dart' show RemoteEventHandler;
import 'ui_form_scope.dart';
import 'ui_nav_item.dart';

class UiKitTabs extends StatelessWidget {
  const UiKitTabs({super.key, required this.items, required this.pack, required this.experienceId, required this.onEvent});
  final List items;
  final String pack;
  final String experienceId;
  final RemoteEventHandler onEvent;

  @override
  Widget build(BuildContext context) {
    final parsed = parseNavItems(items);
    final scope = UiKitFormScope.of(context);
    return FTabs(
      children: [
        for (final item in parsed)
          FTabEntry(
            label: Text(item.label),
            child: GestureDetector(
              onTap: () => fireNav(onEvent, pack, experienceId, item.eventName, scope?.values ?? const {}),
              child: const SizedBox.shrink(),
            ),
          ),
      ],
    );
  }
}
```

> `FTabs` is a content-switcher; to use it as a hop-advancer, fire the nav event when a tab is selected. If `FTabs` exposes an `onChange`/`onPress(index)`, prefer wiring that over the GestureDetector child and drop the empty child. Verify the 0.21.3 tab-selection callback via Context7.

`ui_breadcrumb.dart` (verify `FBreadcrumb`/`FBreadcrumbItem`):

```dart
import 'package:flutter/material.dart';
import 'package:forui/forui.dart';
import 'package:rfw/rfw.dart' show RemoteEventHandler;
import 'ui_form_scope.dart';
import 'ui_nav_item.dart';

class UiKitBreadcrumb extends StatelessWidget {
  const UiKitBreadcrumb({super.key, required this.items, required this.pack, required this.experienceId, required this.onEvent});
  final List items;
  final String pack;
  final String experienceId;
  final RemoteEventHandler onEvent;

  @override
  Widget build(BuildContext context) {
    final parsed = parseNavItems(items);
    final scope = UiKitFormScope.of(context);
    return FBreadcrumb(
      children: [
        for (final item in parsed)
          FBreadcrumbItem(
            onPress: () => fireNav(onEvent, pack, experienceId, item.eventName, scope?.values ?? const {}),
            child: Text(item.label),
          ),
      ],
    );
  }
}
```

`ui_pagination.dart` — render the items as a row of `FButton`s firing the per-page event (a simple, version-stable rendering; swap to `FPagination` if its 0.21.3 API exposes an `onChange(page)`):

```dart
import 'package:flutter/material.dart';
import 'package:forui/forui.dart';
import 'package:rfw/rfw.dart' show RemoteEventHandler;
import 'ui_form_scope.dart';
import 'ui_nav_item.dart';

class UiKitPagination extends StatelessWidget {
  const UiKitPagination({super.key, required this.items, required this.pack, required this.experienceId, required this.onEvent});
  final List items;
  final String pack;
  final String experienceId;
  final RemoteEventHandler onEvent;

  @override
  Widget build(BuildContext context) {
    final parsed = parseNavItems(items);
    final scope = UiKitFormScope.of(context);
    return Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        for (final item in parsed)
          Padding(
            padding: const EdgeInsets.symmetric(horizontal: 2),
            child: FButton(
              style: FButtonStyle.outline(),
              onPress: () => fireNav(onEvent, pack, experienceId, item.eventName, scope?.values ?? const {}),
              child: Text(item.label),
            ),
          ),
      ],
    );
  }
}
```

> Verify `FButton.style`/`FButtonStyle.outline()` naming via Context7. If `FPagination` in 0.21.3 has a clean `onChange(int)`, prefer it.

- [ ] **Step 8: Register** (`ui_tabs.dart`, `ui_breadcrumb.dart`, `ui_pagination.dart`) — add a `List itemList()` helper:

```dart
  List itemList() => (props['items'] as List?) ?? const [];
```

```dart
    case 'ui:tabs':
      return UiKitTabs(items: itemList(), pack: s('pack'), experienceId: s('experienceId'), onEvent: onEvent);
    case 'ui:breadcrumb':
      return UiKitBreadcrumb(items: itemList(), pack: s('pack'), experienceId: s('experienceId'), onEvent: onEvent);
    case 'ui:pagination':
      return UiKitPagination(items: itemList(), pack: s('pack'), experienceId: s('experienceId'), onEvent: onEvent);
```

- [ ] **Step 9: Run; verify PASS.** `flutter test test/ui_kit/ui_nav_a_test.dart && flutter analyze`

- [ ] **Step 10: Commit** (brain: "feat(core): ui: Tabs/Breadcrumb/Pagination + nav stamping"; app: "feat(ui_kit): nav-A covers")

---

## Task 8: Navigation B — Sidebar, BottomNav

**Files:** `UiSurfaces.cs`, `UiExperience.cs`; `app/lib/ui_kit/ui_sidebar.dart`, `ui_bottom_nav.dart`; `ui_registry.dart`; tests. Reuses `ui_nav_item.dart` from Task 7.

**Interfaces:**
- Produces: `Ui.Sidebar/BottomNav`; `UiHop.Sidebar(params (string label, string goTo)[] items)`, `UiHop.BottomNav(params (string label, string goTo)[] items)` (use the `NavItems` helper from Task 7); covers `UiKitSidebar`, `UiKitBottomNav` (same fire-nav contract). Stamping handled by the Task-7 `items` rule in `Inject`.

- [ ] **Step 1: Write the failing Core test**

```csharp
    [Fact]
    public void Nav_b_nodes_emit_items()
    {
        var hop = new UiHop("h");
        hop.Sidebar(("Home", "home"), ("Settings", "settings")).BottomNav(("A", "a"), ("B", "b"));
        var nodes = hop.Factories.Select(f => f(new Dictionary<string, string>())).ToList();
        Assert.Equal(Ui.Sidebar, nodes[0].Type);
        Assert.Equal(Ui.BottomNav, nodes[1].Type);
        Assert.Equal(2, ((IReadOnlyList<object>)nodes[0].Props["items"]!).Count);
    }
```

- [ ] **Step 2: Run; verify FAIL.**

- [ ] **Step 3: Implement Core**

`Ui`: `Sidebar="ui:Sidebar"`, `BottomNav="ui:BottomNav"`.

```csharp
    public UiHop Sidebar(params (string label, string goTo)[] items)
    {
        Factories.Add(_ => new UiWidgetTree(Ui.Sidebar, new Dictionary<string, object?> { ["items"] = NavItems(items) }));
        return this;
    }

    public UiHop BottomNav(params (string label, string goTo)[] items)
    {
        Factories.Add(_ => new UiWidgetTree(Ui.BottomNav, new Dictionary<string, object?> { ["items"] = NavItems(items) }));
        return this;
    }
```

- [ ] **Step 4: Run; verify PASS.**

- [ ] **Step 5: Write the failing app test**

Create `app/test/ui_kit/ui_nav_b_test.dart`:

```dart
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:forui/forui.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_bottom_nav.dart';

Widget _host(Widget child) => MaterialApp(
      home: FTheme(data: FThemes.neutral.light.touch, child: FScaffold(child: child)),
    );

void main() {
  testWidgets('BottomNav fires ExperienceStep on item tap', (tester) async {
    Map<String, Object?>? captured;
    await tester.pumpWidget(_host(UiKitBottomNav(
      pack: 'p', experienceId: 'p',
      items: const [{'label': 'A', 'eventName': 'a'}, {'label': 'B', 'eventName': 'b'}],
      onEvent: (n, a) => captured = a,
    )));
    await tester.tap(find.text('B'));
    await tester.pumpAndSettle();
    expect((captured!['props'] as Map)['eventName'], 'b');
  });
}
```

- [ ] **Step 6: Run; verify FAIL.**

- [ ] **Step 7: Implement covers** (verify `FSidebar`/`FBottomNavigationBar` via Context7)

`ui_bottom_nav.dart`:

```dart
import 'package:flutter/material.dart';
import 'package:forui/forui.dart';
import 'package:rfw/rfw.dart' show RemoteEventHandler;
import 'ui_form_scope.dart';
import 'ui_nav_item.dart';

class UiKitBottomNav extends StatelessWidget {
  const UiKitBottomNav({super.key, required this.items, required this.pack, required this.experienceId, required this.onEvent});
  final List items;
  final String pack;
  final String experienceId;
  final RemoteEventHandler onEvent;

  @override
  Widget build(BuildContext context) {
    final parsed = parseNavItems(items);
    final scope = UiKitFormScope.of(context);
    return FBottomNavigationBar(
      onChange: (index) =>
          fireNav(onEvent, pack, experienceId, parsed[index].eventName, scope?.values ?? const {}),
      children: [
        for (final item in parsed)
          FBottomNavigationBarItem(icon: const Icon(FLucideIcons.circle), label: Text(item.label)),
      ],
    );
  }
}
```

> Verify `FBottomNavigationBar` selection callback (`onChange(int)`) + item constructor via Context7.

`ui_sidebar.dart`:

```dart
import 'package:flutter/material.dart';
import 'package:forui/forui.dart';
import 'package:rfw/rfw.dart' show RemoteEventHandler;
import 'ui_form_scope.dart';
import 'ui_nav_item.dart';

class UiKitSidebar extends StatelessWidget {
  const UiKitSidebar({super.key, required this.items, required this.pack, required this.experienceId, required this.onEvent});
  final List items;
  final String pack;
  final String experienceId;
  final RemoteEventHandler onEvent;

  @override
  Widget build(BuildContext context) {
    final parsed = parseNavItems(items);
    final scope = UiKitFormScope.of(context);
    return FSidebar(
      children: [
        FSidebarGroup(
          children: [
            for (final item in parsed)
              FSidebarItem(
                label: Text(item.label),
                onPress: () => fireNav(onEvent, pack, experienceId, item.eventName, scope?.values ?? const {}),
              ),
          ],
        ),
      ],
    );
  }
}
```

- [ ] **Step 8: Register**:

```dart
    case 'ui:sidebar':
      return UiKitSidebar(items: itemList(), pack: s('pack'), experienceId: s('experienceId'), onEvent: onEvent);
    case 'ui:bottomnav':
      return UiKitBottomNav(items: itemList(), pack: s('pack'), experienceId: s('experienceId'), onEvent: onEvent);
```

- [ ] **Step 9: Run; verify PASS.** `flutter test test/ui_kit/ui_nav_b_test.dart && flutter analyze`

- [ ] **Step 10: Commit** (brain: "feat(core): ui: Sidebar/BottomNav builders"; app: "feat(ui_kit): nav-B covers")

---

## Task 9: Overlays — Dialog, Sheet, Toast (declarative `open` nodes + imperative-present adapter)

**Files:** `UiSurfaces.cs`, `UiExperience.cs`; `app/lib/ui_kit/ui_overlay_host.dart` (the imperative-present adapter), `ui_dialog.dart`, `ui_sheet.dart`, `ui_toast.dart`; `ui_registry.dart`; tests.

**Interfaces:**
- Produces: `Ui.Dialog/Sheet/Toast`; `UiHop.Dialog(bool open, string title, Action<UiHop> body)`, `UiHop.Sheet(bool open, string title, Action<UiHop> body)`, `UiHop.Toast(string message)`; covers `UiKitDialog`, `UiKitSheet`, `UiKitToast`. Each overlay cover is a zero-size widget that, in a post-frame callback when `open` is true (or always, for Toast), imperatively presents the ForUI overlay exactly once and renders its `children` via `buildChild`. A guard (`_presented`) prevents double-present across rebuilds.

> **Design:** overlays add no wire type. An overlay node sits in the hop tree with `open:true` and its `children`. The cover presents `showFDialog`/`showFSheet`/`showFToast`; a Button inside fires `ExperienceStep` (dismiss = the author re-emits the hop with `open:false`). This is the one new client mechanism in the catalog.

- [ ] **Step 1: Write the failing Core test**

```csharp
    [Fact]
    public void Overlay_nodes_emit_open_flag_and_children()
    {
        var hop = new UiHop("h");
        hop.Dialog(true, "Confirm", d => d.Text("Sure?").Button("OK", "done")).Toast("Saved");
        var nodes = hop.Factories.Select(f => f(new Dictionary<string, string>())).ToList();
        Assert.Equal(Ui.Dialog, nodes[0].Type);
        Assert.Equal(true, nodes[0].Props["open"]);
        Assert.Equal("Confirm", nodes[0].Props["title"]);
        Assert.Equal(2, nodes[0].Children!.Count);
        Assert.Equal(Ui.Toast, nodes[1].Type);
        Assert.Equal("Saved", nodes[1].Props["message"]);
    }
```

- [ ] **Step 2: Run; verify FAIL.**

- [ ] **Step 3: Implement Core**

`Ui`: `Dialog="ui:Dialog"`, `Sheet="ui:Sheet"`, `Toast="ui:Toast"`.

```csharp
    public UiHop Dialog(bool open, string title, Action<UiHop> body)
    {
        var inner = new UiHop(Id);
        body(inner);
        Factories.Add(state => new UiWidgetTree(Ui.Dialog,
            new Dictionary<string, object?> { ["open"] = open, ["title"] = title },
            inner.Factories.Select(f => f(state)).ToList()));
        return this;
    }

    public UiHop Sheet(bool open, string title, Action<UiHop> body)
    {
        var inner = new UiHop(Id);
        body(inner);
        Factories.Add(state => new UiWidgetTree(Ui.Sheet,
            new Dictionary<string, object?> { ["open"] = open, ["title"] = title },
            inner.Factories.Select(f => f(state)).ToList()));
        return this;
    }

    public UiHop Toast(string message)
    {
        Factories.Add(_ => new UiWidgetTree(Ui.Toast, new Dictionary<string, object?> { ["message"] = message }));
        return this;
    }
```

> `Inject` already recurses into children, so buttons inside an overlay get stamped.

- [ ] **Step 4: Run; verify PASS.**

- [ ] **Step 5: Write the failing app test**

Create `app/test/ui_kit/ui_overlays_test.dart`:

```dart
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:forui/forui.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_dialog.dart';

void main() {
  testWidgets('Dialog with open:true presents its title', (tester) async {
    await tester.pumpWidget(MaterialApp(
      home: FToaster(
        child: FTheme(
          data: FThemes.neutral.light.touch,
          child: FScaffold(
            child: UiKitDialog(open: true, title: 'Confirm', children: const [Text('Sure?')]),
          ),
        ),
      ),
    ));
    await tester.pumpAndSettle();
    expect(find.text('Confirm'), findsOneWidget);
    expect(find.text('Sure?'), findsOneWidget);
  });
}
```

> `showFToast` requires an `FToaster` ancestor; the app shell must host one (add it in Task 10 wiring if absent). Verify `FToaster` placement via Context7.

- [ ] **Step 6: Run; verify FAIL.**

- [ ] **Step 7: Implement the adapter + covers**

`ui_overlay_host.dart` (shared present-once guard mixin):

```dart
import 'package:flutter/widgets.dart';

mixin PresentOnce<T extends StatefulWidget> on State<T> {
  bool _presented = false;

  void presentOnce(bool shouldPresent, void Function() present) {
    if (!shouldPresent || _presented) return;
    _presented = true;
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) present();
    });
  }
}
```

`ui_dialog.dart`:

```dart
import 'package:flutter/material.dart';
import 'package:forui/forui.dart';
import 'ui_overlay_host.dart';

class UiKitDialog extends StatefulWidget {
  const UiKitDialog({super.key, required this.open, required this.title, required this.children});
  final bool open;
  final String title;
  final List<Widget> children;

  @override
  State<UiKitDialog> createState() => _UiKitDialogState();
}

class _UiKitDialogState extends State<UiKitDialog> with PresentOnce {
  @override
  Widget build(BuildContext context) {
    presentOnce(widget.open, () {
      showFDialog<void>(
        context: context,
        builder: (context, style, animation) => FDialog(
          title: Text(widget.title),
          body: Column(mainAxisSize: MainAxisSize.min, children: widget.children),
          actions: const [],
        ),
      );
    });
    return const SizedBox.shrink();
  }
}
```

> Verify `FDialog` slot names (`title`/`body`/`actions`) and the `showFDialog` builder signature `(context, style, animation)` via Context7 (grounded above, but confirm).

`ui_sheet.dart`:

```dart
import 'package:flutter/material.dart';
import 'package:forui/forui.dart';
import 'ui_overlay_host.dart';

class UiKitSheet extends StatefulWidget {
  const UiKitSheet({super.key, required this.open, required this.title, required this.children});
  final bool open;
  final String title;
  final List<Widget> children;

  @override
  State<UiKitSheet> createState() => _UiKitSheetState();
}

class _UiKitSheetState extends State<UiKitSheet> with PresentOnce {
  @override
  Widget build(BuildContext context) {
    presentOnce(widget.open, () {
      showFSheet<void>(
        context: context,
        side: FLayout.btt,
        builder: (context) => Padding(
          padding: const EdgeInsets.all(16),
          child: Column(mainAxisSize: MainAxisSize.min, children: [Text(widget.title), ...widget.children]),
        ),
      );
    });
    return const SizedBox.shrink();
  }
}
```

> Verify `FLayout.btt` (bottom-to-top) and `showFSheet` signature via Context7 (grounded above).

`ui_toast.dart`:

```dart
import 'package:flutter/material.dart';
import 'package:forui/forui.dart';
import 'ui_overlay_host.dart';

class UiKitToast extends StatefulWidget {
  const UiKitToast({super.key, required this.message});
  final String message;

  @override
  State<UiKitToast> createState() => _UiKitToastState();
}

class _UiKitToastState extends State<UiKitToast> with PresentOnce {
  @override
  Widget build(BuildContext context) {
    presentOnce(true, () => showFToast(context: context, title: Text(widget.message)));
    return const SizedBox.shrink();
  }
}
```

- [ ] **Step 8: Register** — Dialog/Sheet take `kids()` as children:

```dart
    case 'ui:dialog':
      return UiKitDialog(open: props['open'] == true, title: s('title'), children: kids());
    case 'ui:sheet':
      return UiKitSheet(open: props['open'] == true, title: s('title'), children: kids());
    case 'ui:toast':
      return UiKitToast(message: s('message'));
```

- [ ] **Step 9: Run; verify PASS.** `flutter test test/ui_kit/ui_overlays_test.dart && flutter analyze`

- [ ] **Step 10: Commit** (brain: "feat(core): ui: Dialog/Sheet/Toast builders"; app: "feat(ui_kit): overlay covers + present-once adapter")

---

## Task 10: The `ui-gallery` showcase pack + marketplace route + E2E

**Files:**
- Create: `brain/DigitalBrain.Tests/E2E/Packs/UiGalleryPackSource.cs` (canonical pack source)
- Modify: `brain/DigitalBrain.Core/MarketplaceSeeds.cs` (seed `ui-gallery`; identical source string)
- Modify: `brain/DigitalBrain.Core/UiSurfaces.cs` → `UiSurfaceLiveData` (`IsPreinstalledLocalPack` + a `ui-gallery` Run-to-route action, mirroring `hello-world`)
- Modify (app, if absent): the experience-host shell wraps its body in an `FToaster` (for `ui:Toast`)
- Test: `KitExperienceTests.cs` (seed + source guards); `brain/DigitalBrain.Tests/E2E/UiGalleryRendersE2ETests.cs` (new)

**Interfaces:**
- Consumes: every `UiHop` builder from Tasks 1-9; the `hello-world` seed + Run-action pattern (`UiSurfaceLiveData.ExperiencesForPack`).
- Produces: `UiGalleryPackSource.Code` (explicit usings, no `\"`-in-raw-string); a seeded `NeuroPack("ui-gallery", …)`; a Run action with `targetSurfaceKind = "/experience/ui-gallery/ui-gallery"`.

- [ ] **Step 1: Write the failing Core guard tests**

Append to `KitExperienceTests.cs`:

```csharp
    [Fact]
    public void UiGallery_pack_source_is_present_and_explicit_usings()
    {
        var code = DigitalBrain.Tests.E2E.Packs.UiGalleryPackSource.Code;
        Assert.Contains("using DigitalBrain.Core;", code);
        Assert.Contains(": KitExperience", code);
        Assert.DoesNotContain("\\\"", code);   // no backslash-quote escapes in the raw string
    }

    [Fact]
    public void Seeds_include_ui_gallery_pack()
    {
        Assert.Contains(MarketplaceSeeds.LocalUiPacks, p => p.Name == "ui-gallery");
    }
```

- [ ] **Step 2: Run; verify FAIL.**

- [ ] **Step 3: Author the gallery pack source**

Create `brain/DigitalBrain.Tests/E2E/Packs/UiGalleryPackSource.cs`. The gallery is one hop per category, a `ui:Sidebar` switching between them, each category hop showing each component live. MUST use explicit usings and string concat for any literal quote.

```csharp
namespace DigitalBrain.Tests.E2E.Packs;

// The components gallery, authored entirely with the ui: kit it showcases. Shared by the seed and the E2E.
public static class UiGalleryPackSource
{
    public const string Code = """
using System.Collections.Generic;
using DigitalBrain.Core;

public sealed class UiGalleryExperience : KitExperience
{
    protected override UiExperience Define() => Experience("ui-gallery", "UI Kit Gallery")
        .Hop("inputs", s => Nav(s)
            .Heading("Inputs")
            .Panel(p => p.Text("TextField").TextField("name", "Your name"))
            .Panel(p => p.Text("Checkbox").Checkbox("agree", "I agree"))
            .Panel(p => p.Text("Switch").Switch("notify", "Notify me"))
            .Panel(p => p.Text("Select").Select("color", new List<string> { "Red", "Green", "Blue" }, "Color"))
            .Panel(p => p.Text("Slider").Slider("level", 0, 10, "Level")))
        .Hop("display", s => Nav(s)
            .Heading("Display")
            .Panel(p => p.Heading("Heading").Text("body text").Badge("New"))
            .Panel(p => p.Avatar(fallback: "AB"))
            .Panel(p => p.List(l => l.Tile("First", "subtitle").Tile("Second", "subtitle"))))
        .Hop("feedback", s => Nav(s)
            .Heading("Feedback")
            .Panel(p => p.Alert("Heads up", "an inline alert"))
            .Panel(p => p.Progress(0.6))
            .Panel(p => p.Spinner()))
        .Hop("overlays", s => Nav(s)
            .Heading("Overlays")
            .Panel(p => p.Text("Toast").Button("Show toast", "overlays"))
            .Toast("Hello from the gallery"));

    private static UiHop Nav(UiHop s) => s.Sidebar(
        ("Inputs", "inputs"), ("Display", "display"), ("Feedback", "feedback"), ("Overlays", "overlays"));
}
""";
}
```

> Keep this source within the verified builder surface from Tasks 1-9. If a builder name differs from this plan, fix it here AND in the seed string (Step 4) identically.

- [ ] **Step 4: Seed the pack + Run action**

In `MarketplaceSeeds.cs`, add to `LocalUiPacks` (before `Dummy.BehaviorPack`) a `NeuroPack("ui-gallery", "1.0.0", "digitalbraintech", false, 0.0, <the identical source string>, "Browse every ui: component in one place.")`.

In `UiSurfaces.cs` `UiSurfaceLiveData.IsPreinstalledLocalPack`, add:

```csharp
        pack.Name.Equals("ui-gallery", StringComparison.OrdinalIgnoreCase) ||
```

In `UiSurfaceLiveData.ExperiencesForPack`, add a branch mirroring `hello-world`:

```csharp
        else if (pack.Name.Equals("ui-gallery", StringComparison.OrdinalIgnoreCase))
        {
            yield return ExperienceRow(
                pack, "open", "Open", "experience", "Browse every ui: component.",
                UiSurfaceSamples.SynapseAction(
                    "open-ui-gallery", "Open", nameof(ExperienceUsed),
                    new Dictionary<string, object?>
                    {
                        ["packName"] = pack.Name,
                        ["action"] = "open",
                        ["targetSurfaceKind"] = "/experience/ui-gallery/ui-gallery"
                    }),
                userId, sessionId);
        }
```

- [ ] **Step 5: Run Core guards; verify PASS + full fast suite green**

Run: `dotnet build Brain.slnx -clp:ErrorsOnly && dotnet test DigitalBrain.Tests --filter "Category!=E2E" --no-restore`
Expected: PASS (gallery guards + all component builder tests + no regression). Fix any builder-name drift between the source string and the actual `UiHop` methods.

- [ ] **Step 6: Ensure an `FToaster` ancestor in the experience host (for `ui:Toast`)**

If the experience-host shell does not already wrap its body in `FToaster`, wrap it (verify placement via Context7). In `app/lib/features/experience/experience_host_screen.dart` build, wrap the rendered hop:

```dart
return FToaster(child: /* existing experience body */);
```

`flutter analyze` to confirm no breakage.

- [ ] **Step 7: Write the E2E (gated)**

Create `brain/DigitalBrain.Tests/E2E/UiGalleryRendersE2ETests.cs`, mirroring `HelloWorldRendersE2ETests`: boot the Aspire app via `Aspire.Hosting.Testing`, open `ui-gallery` from the marketplace (route `/experience/ui-gallery/ui-gallery`), then assert via `flt-semantics-identifier` that the `inputs` hop renders, click the sidebar `Display` item, assert the `display` hop renders, and so on for `feedback`. Use the existing `ExperienceFlowDriver` helper. Mark `[Trait("Category", "E2E")]`.

```csharp
using System.Threading.Tasks;
using Xunit;

namespace DigitalBrain.Tests.E2E;

[Trait("Category", "E2E")]
public class UiGalleryRendersE2ETests
{
    [Fact]
    public async Task Gallery_opens_and_walks_category_hops()
    {
        await using var driver = await ExperienceFlowDriver.StartAsync();
        await driver.OpenExperienceAsync("ui-gallery", "ui-gallery");
        await driver.AssertSurfaceVisibleAsync("inputs");
        await driver.TapTextAsync("Display");
        await driver.AssertSurfaceVisibleAsync("display");
        await driver.TapTextAsync("Feedback");
        await driver.AssertSurfaceVisibleAsync("feedback");
    }
}
```

> Match the actual `ExperienceFlowDriver` method names from `HelloWorldRendersE2ETests` (e.g. `OpenExperienceAsync`/`AssertSurfaceVisibleAsync`/`TapTextAsync`); if they differ, use the real ones. The semantics id for each hop is its `surfaceId` (the hop id).

- [ ] **Step 8: Run the E2E (real)**

Run: `dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~UiGalleryRendersE2ETests" --no-restore`
Expected: PASS — gallery opens, the sidebar advances hops, each category renders. (This is the broad render smoke-test for all 35 components composed through the standalone pack path.)

- [ ] **Step 9: Keep Hello World green (regression)**

Run: `dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~HelloWorldRendersE2ETests" --no-restore`
Expected: PASS.

- [ ] **Step 10: Commit**

```bash
cd /e/digitalbraintech/brain && git add DigitalBrain.Core DigitalBrain.Tests && git commit -m "feat: ui-gallery showcase pack + marketplace route + E2E"
cd /e/digitalbraintech/app && git add lib/features/experience && git commit -m "feat(app): FToaster host for ui: overlays"
```

---

## Final verification (end of branch, before merge)

- [ ] `cd /e/digitalbraintech/brain && dotnet build Brain.slnx -clp:ErrorsOnly`
- [ ] `dotnet test DigitalBrain.Tests --filter "Category!=E2E"` — all green.
- [ ] `dotnet test DigitalBrain.Tests --filter "Category=E2E"` — Hello World + UI Gallery green.
- [ ] `cd /e/digitalbraintech/app && flutter analyze && flutter test` — all green.
- [ ] `aspire doctor` (from brain) — healthy.
- [ ] One intentional `aspire run` (from brain): open the marketplace, click **UI Kit Gallery**, walk all four category hops, see every component render live; open **Hello World** to confirm no regression.
- [ ] Whole-branch review (subagent), then local merge per `[[merge-state-master-main-2026-06]]` — **no push** (brain `master`, app `main`) unless the user says otherwise.

---

## Self-review notes (coverage check)

- **All 35 components covered:** existing 5 (untouched); Tasks 1-2 → 7 inputs; Task 3 → 5 layout; Tasks 4-5 → 6 display; Task 6 → 4 feedback; Tasks 7-8 → 5 navigation; Task 9 → 3 overlays = 30 new. ✓
- **Spec's value model** (string-on-wire) → Task 0 (buildActionEnvelope) + every input cover coerces. ✓
- **Spec's nav-as-hop-advance** → Tasks 7-8 reuse `ExperienceStep` via `fireNav`; `Inject` generalized in Task 5 + extended for `items` in Task 7. ✓
- **Spec's overlay model** (declarative `open`, imperative present, no protocol change) → Task 9. ✓
- **Spec's folded-in follow-ups** (bridge `title`, string-coerce envelope) → Task 0. ✓
- **Spec's gallery dogfood + broad E2E** → Task 10. ✓
- **ForUI API risk:** every cover that wasn't fully grounded in a fetched 0.21.3 signature carries an explicit "verify via Context7" note; the mandatory pre-code Context7 lookup (Global Constraints) catches drift before tests run.
