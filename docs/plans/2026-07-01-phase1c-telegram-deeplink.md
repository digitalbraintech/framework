# Phase 1c — Telegram Deep-Link Chat→Bundle Binding Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans. Steps use checkbox (`- [ ]`) syntax.

**Goal:** A Telegram user who opens `t.me/<bot>?start=<bundleId>` (which arrives as a "/start <bundleId>" message) binds their chat to that bundle; subsequent messages route to the bound bundle. Unbound chats behave exactly as today (fall back to the broadcast Responder).

**Architecture:** A new per-chat `TelegramChatNeuron` (grain key `tg-chat-<chatId>`) becomes the entry point for inbound Telegram messages. It handles `Signal("TelegramMessageReceived", …)`: a `/start <bundleId>` message replies with a confirmation (`Signal("TelegramReplyRequested", {chatId,text})`, broadcast so the transport's egress picks it up); any other message routes to the bound bundle (the most recent `/start` bundleId, derived from the neuron's own incoming journal) by point-to-point delivery to `generated-<bundleId>`, or — if the chat has never bound — broadcasts the message so today's Responder handles it. No new synapse type and no new state store: the binding is derived from the durable journal (NeuroOS idiom, like `MarketplaceNeuron`'s cache). Telegram bundles are text handlers emitting `TelegramReplyRequested`; RFW experiences remain in-app (out of scope).

**Tech Stack:** .NET 11 (net11.0), Orleans (`Neuron : DurableGrain`, `TestCluster`), xUnit, existing `Signal`, `GatewayService`.

## Global Constraints

- Target framework **net11.0**; never pin `Version="*"`.
- **No vacuous `/// <summary>`**; self-explanatory names; small inline comments only where genuinely non-obvious.
- Tests are executable specs. **Run the relevant tests and confirm they pass before claiming a task done.**
- **Non-breaking:** an unbound chat MUST behave exactly as today (its message is broadcast so the seeded Responder handles it). Do not change the Responder pack or the outbound `TelegramReplyRequested` contract (`{chatId:int64, text:string}`).
- Use `Signal` (Core) with string names — do NOT add new synapse record types and do NOT add an assembly dependency on `DigitalBrain.Telegram` from the kernel.
- Beware `Neuron.FireAsync`: with no `Receiver` and `IsBroadcast=false` it SELF-delivers (re-enters dispatch). Use `Broadcast(...)` for the outbound reply, and set an explicit `Receiver` when forwarding to another grain.
- Look up unfamiliar APIs via **Context7** before writing code against them.
- Work in the `brain` repo on branch `spec/phase1c-telegram-deeplink` (already checked out). Do NOT `git add` anything under `.superpowers/`.
- Commit messages end with: `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`.

---

## File Structure

- **Modify** `DigitalBrain.Core/Synapse.cs` — add `ITelegramChatNeuron : INeuron` interface (next to `IMarketplaceNeuron`), exposing `Task<string?> GetBoundBundleAsync()`.
- **Create** `DigitalBrain.Kernel/TelegramChatNeuron.cs` — the per-chat grain.
- **Create** `DigitalBrain.Tests/Telegram/TelegramChatNeuronTests.cs` — TestCluster tests for bind / reply / route / default.
- **Modify** `DigitalBrain.Kernel/Gateway/GatewayService.cs` — route inbound `TelegramMessageReceived` to `TelegramChatNeuron("tg-chat-<chatId>")`.
- **Create** `DigitalBrain.Tests/Telegram/TelegramDeepLinkRoutingTests.cs` — the gateway routes an inbound Telegram envelope to the chat neuron.

---

## Task 1: `TelegramChatNeuron` — bind, confirm, route (Kernel)

**Files:**
- Modify: `DigitalBrain.Core/Synapse.cs` (add `ITelegramChatNeuron`)
- Create: `DigitalBrain.Kernel/TelegramChatNeuron.cs`
- Test: `DigitalBrain.Tests/Telegram/TelegramChatNeuronTests.cs`

**Interfaces:**
- Consumes: `Signal(string Name, IReadOnlyDictionary<string,object?> Props)`; `Neuron` base (`Broadcast`, `FireAsync`, `IncomingJournal`, `Self`, `GrainFactory`); `IGeneratedNeuron` (grain key `"generated-" + packName.ToLowerInvariant()`); `NeuronId`; `INeuron`.
- Produces: `ITelegramChatNeuron : INeuron` with `Task<string?> GetBoundBundleAsync()`; grain `TelegramChatNeuron` (`[GrainType("digitalbrain.telegram-chat.v1")]`) handling `Signal` named `"TelegramMessageReceived"`.

- [ ] **Step 1: Write the failing tests**

Create `DigitalBrain.Tests/Telegram/TelegramChatNeuronTests.cs`. Verify `Signal`'s constructor and the `IGeneratedNeuron` key convention against `DigitalBrain.Core/Signals.cs` and `DigitalBrain.Kernel/Gateway/GatewayService.cs` if anything doesn't resolve.

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DigitalBrain.Core;
using DigitalBrain.Kernel;
using DigitalBrain.Tests.TestSupport;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.Tests.Telegram;

public class TelegramChatNeuronTests
{
    private static TestCluster Cluster()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<NeuronTestSiloConfigurator>();
        return builder.Build();
    }

    private static Signal Inbound(long chatId, string text) =>
        new("TelegramMessageReceived", new Dictionary<string, object?>
        {
            ["chatId"] = chatId, ["fromUserId"] = 1L, ["text"] = text, ["updateId"] = 1L
        });

    [Fact]
    public async Task Start_command_binds_the_chat_and_confirms()
    {
        var cluster = Cluster();
        await cluster.DeployAsync();
        try
        {
            var chat = cluster.GrainFactory.GetGrain<ITelegramChatNeuron>("tg-chat-100");
            await chat.DeliverAsync(Inbound(100, "/start hello-world"));

            Assert.Equal("hello-world", await chat.GetBoundBundleAsync());

            var reply = (await chat.GetOutgoingTimelineAsync())
                .OfType<Signal>().Single(s => s.Name == "TelegramReplyRequested");
            Assert.Equal(100L, System.Convert.ToInt64(reply.Props["chatId"]));
            Assert.Contains("hello-world", reply.Props["text"]?.ToString());
        }
        finally { await cluster.StopAllSilosAsync(); }
    }

    [Fact]
    public async Task Latest_start_wins_as_the_binding()
    {
        var cluster = Cluster();
        await cluster.DeployAsync();
        try
        {
            var chat = cluster.GrainFactory.GetGrain<ITelegramChatNeuron>("tg-chat-101");
            await chat.DeliverAsync(Inbound(101, "/start alpha"));
            await chat.DeliverAsync(Inbound(101, "/start beta"));

            Assert.Equal("beta", await chat.GetBoundBundleAsync());
        }
        finally { await cluster.StopAllSilosAsync(); }
    }

    [Fact]
    public async Task Bound_chat_routes_a_normal_message_to_the_bound_bundle()
    {
        var cluster = Cluster();
        await cluster.DeployAsync();
        try
        {
            var chat = cluster.GrainFactory.GetGrain<ITelegramChatNeuron>("tg-chat-102");
            await chat.DeliverAsync(Inbound(102, "/start hello-world"));
            await chat.DeliverAsync(Inbound(102, "hi there"));

            var forwarded = (await chat.GetOutgoingTimelineAsync())
                .OfType<Signal>()
                .Where(s => s.Name == "TelegramMessageReceived" && s.Receiver is not null)
                .ToList();
            Assert.Contains(forwarded, s =>
                s.Receiver!.Value == "generated-hello-world" && s.Props["text"]?.ToString() == "hi there");
        }
        finally { await cluster.StopAllSilosAsync(); }
    }

    [Fact]
    public async Task Unbound_chat_broadcasts_so_the_default_responder_handles_it()
    {
        var cluster = Cluster();
        await cluster.DeployAsync();
        try
        {
            var chat = cluster.GrainFactory.GetGrain<ITelegramChatNeuron>("tg-chat-103");
            await chat.DeliverAsync(Inbound(103, "just a question"));

            var broadcast = (await chat.GetOutgoingTimelineAsync())
                .OfType<Signal>()
                .Where(s => s.Name == "TelegramMessageReceived" && s.IsBroadcast)
                .ToList();
            Assert.Contains(broadcast, s => s.Props["text"]?.ToString() == "just a question");
        }
        finally { await cluster.StopAllSilosAsync(); }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd /e/digitalbraintech/brain && dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~TelegramChatNeuronTests"`
Expected: FAIL — `ITelegramChatNeuron` / `TelegramChatNeuron` do not exist (compile error).

- [ ] **Step 3: Add the interface**

In `DigitalBrain.Core/Synapse.cs`, next to `public interface IMarketplaceNeuron`, add:

```csharp
public interface ITelegramChatNeuron : INeuron
{
    Task<string?> GetBoundBundleAsync();
}
```

- [ ] **Step 4: Create the grain**

Create `DigitalBrain.Kernel/TelegramChatNeuron.cs`:

```csharp
using DigitalBrain.Core;

namespace DigitalBrain.Kernel;

// One grain per Telegram chat (key "tg-chat-<chatId>"). It is the entry point for inbound Telegram messages.
// The chat's bound bundle is derived from the durable incoming journal (the most recent "/start <bundleId>"),
// so binding needs no separate state store. A bound chat routes normal messages point-to-point to the bound
// bundle's generated neuron; an unbound chat broadcasts (today's behaviour → the seeded Responder handles it).
[GrainType("digitalbrain.telegram-chat.v1")]
public sealed class TelegramChatNeuron : Neuron, ITelegramChatNeuron, IHandle<Signal>
{
    private const string InboundName = "TelegramMessageReceived";
    private const string ReplyName = "TelegramReplyRequested";
    private const string StartPrefix = "/start";

    public TelegramChatNeuron(ILogger<TelegramChatNeuron> logger, NeuronJournals journals)
        : base(logger, journals) { }

    public Task<string?> GetBoundBundleAsync() => Task.FromResult(BoundBundle());

    public async Task HandleAsync(Signal signal)
    {
        if (signal.Name != InboundName) return;

        var text = signal.Props.TryGetValue("text", out var t) ? t?.ToString() ?? "" : "";
        var chatId = signal.Props.TryGetValue("chatId", out var c) ? c : null;

        if (TryParseStart(text, out var bundleId))
        {
            // The binding itself is implicit: this "/start" is now the most recent one in the journal.
            await Broadcast(new Signal(ReplyName, new Dictionary<string, object?>
            {
                ["chatId"] = chatId,
                ["text"] = $"You're now chatting with {bundleId}."
            }));
            return;
        }

        var bound = BoundBundle();
        if (bound is not null)
        {
            var receiver = new NeuronId("generated-" + bound.ToLowerInvariant());
            await FireAsync(signal with { Receiver = receiver });
        }
        else
        {
            await Broadcast(signal);
        }
    }

    // Most recent "/start <bundleId>" in the incoming journal is the active binding; null if none.
    private string? BoundBundle()
    {
        for (var i = IncomingJournal.Count - 1; i >= 0; i--)
        {
            if (IncomingJournal[i] is Signal s && s.Name == InboundName
                && s.Props.TryGetValue("text", out var t)
                && TryParseStart(t?.ToString() ?? "", out var bundleId))
            {
                return bundleId;
            }
        }
        return null;
    }

    private static bool TryParseStart(string text, out string bundleId)
    {
        bundleId = "";
        var trimmed = text.Trim();
        if (!trimmed.StartsWith(StartPrefix, StringComparison.Ordinal)) return false;
        var rest = trimmed[StartPrefix.Length..].Trim();
        if (rest.Length == 0) return false;
        bundleId = rest;
        return true;
    }
}
```

Note on the forward path: `signal with { Receiver = ... }` re-uses the inbound `Signal` (same props) and sets its point-to-point receiver; `FireAsync` journals it (outgoing) and delivers it to that grain. `Broadcast` sets `IsBroadcast=true` and puts it on the timeline (does not self-deliver). Both are visible in the outgoing journal for the tests.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `cd /e/digitalbraintech/brain && dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~TelegramChatNeuronTests"`
Expected: PASS (4 passed). If `signal with { Receiver = ... }` doesn't compile, confirm `Synapse.Receiver` is an `init`-settable `NeuronId?` (it is — see `Synapse` in `DigitalBrain.Core/Synapse.cs`).

- [ ] **Step 6: Commit**

```bash
cd /e/digitalbraintech/brain
git add DigitalBrain.Core/Synapse.cs DigitalBrain.Kernel/TelegramChatNeuron.cs DigitalBrain.Tests/Telegram/TelegramChatNeuronTests.cs
git commit -m "$(cat <<'MSG'
feat(kernel): TelegramChatNeuron — deep-link chat->bundle binding

Per-chat grain (tg-chat-<chatId>): /start <bundleId> binds the chat (derived
from the durable journal) and confirms; a bound chat routes messages to the
bound bundle; an unbound chat broadcasts (today's Responder path). Text-only.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
MSG
)"
```

---

## Task 2: Route inbound Telegram messages to the chat neuron (Gateway)

**Files:**
- Modify: `DigitalBrain.Kernel/Gateway/GatewayService.cs` (the generic-Send path that currently builds `Signal("TelegramMessageReceived", …)` and broadcasts via `IngressNeuron`)
- Test: `DigitalBrain.Tests/Telegram/TelegramDeepLinkRoutingTests.cs`

**Context:** Today, inbound `SynapseEnvelope{TypeName="TelegramMessageReceived"}` hits `GatewayService.Send`'s generic path, which builds a `Signal` and delivers it to `IngressNeuron(correlationId)` which broadcasts it (around GatewayService.cs line 170-175). This task special-cases `TelegramMessageReceived` so the `Signal` is delivered to `TelegramChatNeuron("tg-chat-<chatId>")` (which then binds/routes/broadcasts per Task 1). Other envelope types are unchanged.

**Interfaces:**
- Consumes: `ITelegramChatNeuron` (Task 1); existing `GatewayService.Send` generic path; the props dictionary already parsed there (has `chatId`).
- Produces: inbound Telegram messages entering through `TelegramChatNeuron`.

- [ ] **Step 1: Write the failing test**

Create `DigitalBrain.Tests/Telegram/TelegramDeepLinkRoutingTests.cs`. Read the existing `GatewayService` tests (e.g. `DigitalBrain.Tests/Gateway/GenericSendTests.cs`) to mirror how they invoke `Send` with a `SynapseEnvelope` over the TestCluster, then assert that after sending a `TelegramMessageReceived` envelope with `text="/start hello-world"` for `chatId=200`, the grain `ITelegramChatNeuron("tg-chat-200")` reports `GetBoundBundleAsync() == "hello-world"`. (If the existing gateway tests use a specific harness/fixture, reuse it rather than hand-rolling the gRPC call.)

Write the test to assert: send envelope → `cluster.GrainFactory.GetGrain<ITelegramChatNeuron>("tg-chat-200").GetBoundBundleAsync()` returns `"hello-world"`.

- [ ] **Step 2: Run it; verify it fails**

Run: `cd /e/digitalbraintech/brain && dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~TelegramDeepLinkRoutingTests"`
Expected: FAIL — the message still goes to `IngressNeuron`, so `tg-chat-200` has no binding (`GetBoundBundleAsync()` is null).

- [ ] **Step 3: Route TelegramMessageReceived to the chat neuron**

In `GatewayService.Send`, in the generic path, before the existing `IngressNeuron` broadcast, special-case the Telegram inbound type. Locate where the props dict + `Signal` are built for the generic path and add:

```csharp
        if (string.Equals(request.TypeName, "TelegramMessageReceived", StringComparison.Ordinal)
            && props.TryGetValue("chatId", out var chatIdValue) && chatIdValue is not null)
        {
            var chatKey = "tg-chat-" + System.Convert.ToInt64(chatIdValue);
            var chat = grains.GetGrain<ITelegramChatNeuron>(chatKey);
            await chat.DeliverAsync(new Signal(request.TypeName, props));
            return /* the same success response the generic path returns */;
        }
```

Match the surrounding code exactly: use the same `props` variable already parsed there, the same `grains`/`GrainFactory` reference the method uses, and return whatever the generic path returns (e.g. an `Ack`/empty response) — do not change the method signature. Leave the `IngressNeuron` path in place for all other types.

- [ ] **Step 4: Run it; verify it passes**

Run: `cd /e/digitalbraintech/brain && dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~TelegramDeepLinkRoutingTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
cd /e/digitalbraintech/brain
git add DigitalBrain.Kernel/Gateway/GatewayService.cs DigitalBrain.Tests/Telegram/TelegramDeepLinkRoutingTests.cs
git commit -m "$(cat <<'MSG'
feat(kernel): route inbound Telegram messages through TelegramChatNeuron

The gateway now delivers TelegramMessageReceived to the per-chat neuron
(tg-chat-<chatId>) so deep-link binding + routing apply; other envelope
types keep the existing ingress path.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
MSG
)"
```

---

## Final verification (after both tasks)

- [ ] **Build**: `cd /e/digitalbraintech/brain && dotnet build` → 0 errors.
- [ ] **Telegram + no-regression**: `cd /e/digitalbraintech/brain && dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~TelegramChatNeuronTests|FullyQualifiedName~TelegramDeepLinkRoutingTests|FullyQualifiedName~ResponderPackTests|FullyQualifiedName~TransportContractTests|FullyQualifiedName~GenericSendTests"` → all pass (Responder/transport/generic-send green confirms non-breaking).

> `aspire doctor` not required — Core/Kernel/test only; no AppHost change.

## Out of scope

- Per-message routing UX polish, "unknown bundle" handling, unbinding — v1 keeps it minimal.
- Rendering RFW experiences in Telegram (SDUI→Telegram bridge) — deferred.
- The transport already forwards `/start <id>` text verbatim (confirmed) — no transport change needed.
