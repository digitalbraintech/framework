# Phase 1b (trust) — Trust-Gated Publishing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Gate publishing to trusted publishers — a pack may be published only if it carries a valid author signature whose public key is on a trusted allowlist — while defaulting the gate OFF so dev/test (and existing unsigned-publish flows) are unaffected.

**Architecture:** A pure `PublisherTrust.IsTrusted(pack, trustedKeys)` decision in `DigitalBrain.Core` (signed + integrity-valid + author key on allowlist). `MarketplaceNeuron.HandleAsync(PublishToMarketplace)` reads `DigitalBrain:Marketplace:GatePublishing` (default false) and, when true, rejects publishes that are not from a trusted publisher — where the trusted set is `TrustedPublisher.PublicKeyBase64` plus any keys in `DigitalBrain:Marketplace:TrustedPublisherKeys`. Integrity verification (`VerifyPack`) proves the code was not tampered after signing; the allowlist proves the *publisher* is trusted (a self-signed pack from a stranger passes integrity but fails the allowlist).

**Tech Stack:** .NET 11 (net11.0), Orleans (`TestCluster`), xUnit, existing `PackSignatureVerifier` + `TrustedPublisher`.

## Global Constraints

- Target framework **net11.0**; never pin `Version="*"`.
- **No vacuous `/// <summary>`**; self-explanatory names; small inline comments only where genuinely non-obvious.
- Tests are executable specs. **Run the relevant tests and confirm they pass before claiming a task done.**
- `DigitalBrain.Core` has global usings — new Core files: `namespace DigitalBrain.Core;`, no `using` lines unless the compiler demands one.
- **Backward compatibility is critical:** the gate MUST default to **false**. With the gate off, publishing behaves exactly as today (existing tests that publish unsigned packs — `HandlerGrowthTests`, `CatalogMaterializationTests` — MUST stay green).
- Look up unfamiliar library/framework APIs via **Context7** before writing code against them.
- Work in the `brain` repo on branch `spec/phase1b-trust-gate` (already checked out). Relative paths; never leak user-profile paths. Do NOT `git add` anything under `.superpowers/`.
- Commit messages end with: `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`.

---

## File Structure

- **Create** `DigitalBrain.Core/Trust/PublisherTrust.cs` — pure `IsTrusted(NeuroPack, IReadOnlyCollection<string>)` decision.
- **Create** `DigitalBrain.Tests/Trust/PublisherTrustTests.cs` — pure unit tests (trusted / untrusted / unsigned / tampered).
- **Modify** `DigitalBrain.Kernel/SystemNeurons.cs` — read the gate + trusted keys and reject untrusted publishes in `MarketplaceNeuron.HandleAsync(PublishToMarketplace)`.
- **Create** `DigitalBrain.Tests/Trust/PublishGateTests.cs` — TestCluster: gate on → trusted-signed lists, stranger-signed rejected; gate off → unsigned still lists.

---

## Task 1: `PublisherTrust.IsTrusted` (Core, pure)

**Files:**
- Create: `DigitalBrain.Core/Trust/PublisherTrust.cs`
- Test: `DigitalBrain.Tests/Trust/PublisherTrustTests.cs`

**Interfaces:**
- Consumes: `NeuroPack`, `PackSignatureVerifier.{VerifyPack, GenerateKeyPair, SignPack}` (existing).
- Produces: `PublisherTrust.IsTrusted(NeuroPack pack, IReadOnlyCollection<string> trustedPublisherKeys) : bool`.

- [ ] **Step 1: Write the failing tests**

Create `DigitalBrain.Tests/Trust/PublisherTrustTests.cs`:

```csharp
using System.Collections.Generic;
using DigitalBrain.Core;
using Xunit;

namespace DigitalBrain.Tests.Trust;

public class PublisherTrustTests
{
    private static NeuroPack Pack(string code = "public class P : DigitalBrain.Core.IPackBehavior { public string Respond(string i) => i; }")
        => new("p", "1.0.0", Code: code);

    [Fact]
    public void Signed_by_a_key_on_the_allowlist_is_trusted()
    {
        var (priv, pub) = PackSignatureVerifier.GenerateKeyPair();
        var signed = PackSignatureVerifier.SignPack(Pack(), priv, pub);

        Assert.True(PublisherTrust.IsTrusted(signed, new[] { pub }));
    }

    [Fact]
    public void Signed_by_a_key_not_on_the_allowlist_is_untrusted()
    {
        var (priv, pub) = PackSignatureVerifier.GenerateKeyPair();
        var (_, otherPub) = PackSignatureVerifier.GenerateKeyPair();
        var signed = PackSignatureVerifier.SignPack(Pack(), priv, pub);

        Assert.False(PublisherTrust.IsTrusted(signed, new[] { otherPub }));
    }

    [Fact]
    public void Unsigned_is_untrusted()
    {
        Assert.False(PublisherTrust.IsTrusted(Pack(), new[] { "any-key" }));
    }

    [Fact]
    public void Tampered_code_after_signing_is_untrusted()
    {
        var (priv, pub) = PackSignatureVerifier.GenerateKeyPair();
        var signed = PackSignatureVerifier.SignPack(Pack(), priv, pub);
        var tampered = signed with { Code = signed.Code + " // sneaky" };

        Assert.False(PublisherTrust.IsTrusted(tampered, new[] { pub }));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd /e/digitalbraintech/brain && dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~PublisherTrustTests"`
Expected: FAIL — compile error, `PublisherTrust` does not exist.

- [ ] **Step 3: Create `PublisherTrust`**

Create `DigitalBrain.Core/Trust/PublisherTrust.cs`:

```csharp
namespace DigitalBrain.Core;

// Publisher-trust decision for gated publishing. A pack is trusted iff it carries a valid author signature
// (integrity — code untampered since signing) AND its author public key is on the trusted allowlist.
// VerifyPack alone does NOT establish publisher trust: a stranger's self-signed pack passes integrity but
// must still fail the allowlist. Unsigned or tampered packs fail VerifyPack and are never trusted.
public static class PublisherTrust
{
    public static bool IsTrusted(NeuroPack pack, IReadOnlyCollection<string> trustedPublisherKeys)
    {
        if (!PackSignatureVerifier.VerifyPack(pack)) return false;
        return trustedPublisherKeys.Contains(pack.AuthorPublicKeyBase64);
    }
}
```

(If the build reports `Contains` unresolved, add `using System.Linq;` — Core normally has it globally.)

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd /e/digitalbraintech/brain && dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~PublisherTrustTests"`
Expected: PASS (4 passed).

- [ ] **Step 5: Commit**

```bash
cd /e/digitalbraintech/brain
git add DigitalBrain.Core/Trust/PublisherTrust.cs DigitalBrain.Tests/Trust/PublisherTrustTests.cs
git commit -m "$(cat <<'MSG'
feat(core): PublisherTrust.IsTrusted — signature + allowlist decision

Pure publisher-trust decision for gated publishing: signed + integrity-valid
+ author key on the trusted allowlist. Integrity alone is not trust.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
MSG
)"
```

---

## Task 2: Gate the publish handler (Kernel)

**Files:**
- Modify: `DigitalBrain.Kernel/SystemNeurons.cs` (`MarketplaceNeuron.HandleAsync(PublishToMarketplace)` + helpers)
- Test: `DigitalBrain.Tests/Trust/PublishGateTests.cs`

**Interfaces:**
- Consumes: `PublisherTrust.IsTrusted` (Task 1); `TrustedPublisher.PublicKeyBase64` and `TrustedPublisher.SignPublishCommand(PublishToMarketplace)` (existing); `PackSignatureVerifier.{GenerateKeyPair, SignPack}`; config via `ServiceProvider.GetService<IConfiguration>()`; existing `ToNeuroPack`, `_publishedCache`, `Logger`, `IMarketplaceNeuron`, `PublishToMarketplace`, `ListPublished`, `PublishedList`.
- Produces: publishing rejected (no cache write, no surface emit) when the gate is on and the pack is not from a trusted publisher.

- [ ] **Step 1: Write the failing tests**

Create `DigitalBrain.Tests/Trust/PublishGateTests.cs`. The gate is read from `DigitalBrain:Marketplace:GatePublishing`; inject it into the test silo's configuration. If `TestClusterBuilder.ConfigureHostConfiguration` does not surface `IConfiguration` to the grain in this Orleans version, use the same config-injection mechanism an existing config-reading test uses (search the test project for one that sets `DigitalBrain:` config over a `TestCluster`, e.g. a `PackConfig`/scoped-LLM test) and mirror it.

```csharp
using System.Collections.Generic;
using System.Linq;
using DigitalBrain.Core;
using DigitalBrain.Kernel;
using DigitalBrain.Tests.TestSupport;
using Microsoft.Extensions.Configuration;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.Tests.Trust;

public class PublishGateTests
{
    private static TestCluster GatedCluster()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<NeuronTestSiloConfigurator>();
        builder.ConfigureHostConfiguration(cfg => cfg.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DigitalBrain:Marketplace:GatePublishing"] = "true"
        }));
        return builder.Build();
    }

    private static async Task<IReadOnlyList<NeuroPack>> ListedAsync(IMarketplaceNeuron market)
    {
        await market.FireAsync(new ListPublished());
        return (await market.GetTimelineAsync()).OfType<PublishedList>().Last().Packs;
    }

    [Fact]
    public async Task Gate_on_admits_a_trusted_publisher()
    {
        var cluster = GatedCluster();
        await cluster.DeployAsync();
        try
        {
            var market = cluster.GrainFactory.GetGrain<IMarketplaceNeuron>("market-gate-trusted");
            var signed = TrustedPublisher.SignPublishCommand(
                new PublishToMarketplace("trusted-pack", "1.0.0", Code: "public class P : DigitalBrain.Core.IPackBehavior { public string Respond(string i) => i; }"));
            await market.FireAsync(signed);

            Assert.Contains(await ListedAsync(market), p => p.Name == "trusted-pack");
        }
        finally { await cluster.StopAllSilosAsync(); }
    }

    [Fact]
    public async Task Gate_on_rejects_a_stranger()
    {
        var cluster = GatedCluster();
        await cluster.DeployAsync();
        try
        {
            var market = cluster.GrainFactory.GetGrain<IMarketplaceNeuron>("market-gate-stranger");
            var (priv, pub) = PackSignatureVerifier.GenerateKeyPair();
            var stranger = PackSignatureVerifier.SignPack(
                new NeuroPack("stranger-pack", "1.0.0", Code: "public class P : DigitalBrain.Core.IPackBehavior { public string Respond(string i) => i; }"),
                priv, pub);
            await market.FireAsync(new PublishToMarketplace(
                stranger.Name, stranger.Version, Code: stranger.Code,
                AuthorPublicKeyBase64: stranger.AuthorPublicKeyBase64, SignatureBase64: stranger.SignatureBase64));

            Assert.DoesNotContain(await ListedAsync(market), p => p.Name == "stranger-pack");
        }
        finally { await cluster.StopAllSilosAsync(); }
    }

    [Fact]
    public async Task Gate_off_by_default_admits_unsigned()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<NeuronTestSiloConfigurator>();
        var cluster = builder.Build();
        await cluster.DeployAsync();
        try
        {
            var market = cluster.GrainFactory.GetGrain<IMarketplaceNeuron>("market-gate-off");
            await market.FireAsync(new PublishToMarketplace("unsigned-pack", "1.0.0",
                Code: "public class P : DigitalBrain.Core.IPackBehavior { public string Respond(string i) => i; }"));

            Assert.Contains(await ListedAsync(market), p => p.Name == "unsigned-pack");
        }
        finally { await cluster.StopAllSilosAsync(); }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd /e/digitalbraintech/brain && dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~PublishGateTests"`
Expected: the two gate-on tests FAIL (`Gate_on_rejects_a_stranger` lists the stranger because there is no gate yet); `Gate_off_by_default_admits_unsigned` already passes. (If the gate-on config does not reach the grain, fix the config injection first — see Step 1 note — so that `Gate_on_rejects_a_stranger` is a meaningful failing test, not a false pass.)

- [ ] **Step 3: Add the gate to the publish handler**

In `DigitalBrain.Kernel/SystemNeurons.cs`, in `MarketplaceNeuron`, add these helpers (near `RejectUnsignedPacks`):

```csharp
    private bool GatePublishing =>
        ServiceProvider.GetService<IConfiguration>()?.GetValue("DigitalBrain:Marketplace:GatePublishing", false) ?? false;

    private IReadOnlyCollection<string> TrustedPublisherKeys()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal) { TrustedPublisher.PublicKeyBase64 };
        var configured = ServiceProvider.GetService<IConfiguration>()
            ?.GetSection("DigitalBrain:Marketplace:TrustedPublisherKeys").Get<string[]>();
        if (configured is not null)
            foreach (var key in configured) keys.Add(key);
        return keys;
    }
```

Then at the top of `HandleAsync(PublishToMarketplace)` — after the remote-publish block and before `EnsureCache()` / the cache write — add:

```csharp
        if (GatePublishing && !PublisherTrust.IsTrusted(ToNeuroPack(cmd), TrustedPublisherKeys()))
        {
            Logger.LogWarning("Publish REJECTED - pack {Name}@{Ver} is not from a trusted publisher (publishing gate enabled)",
                cmd.PackName, cmd.Version);
            return;
        }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd /e/digitalbraintech/brain && dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~PublishGateTests"`
Expected: PASS (3 passed).

- [ ] **Step 5: Commit**

```bash
cd /e/digitalbraintech/brain
git add DigitalBrain.Kernel/SystemNeurons.cs DigitalBrain.Tests/Trust/PublishGateTests.cs
git commit -m "$(cat <<'MSG'
feat(kernel): trust-gate publishing behind DigitalBrain:Marketplace:GatePublishing

When the gate is enabled, MarketplaceNeuron rejects publishes that are not
from a trusted publisher (valid signature + author key on the allowlist:
TrustedPublisher + configured keys). Gate defaults off — dev/test unaffected.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
MSG
)"
```

---

## Final verification (after both tasks)

- [ ] **Build**

Run: `cd /e/digitalbraintech/brain && dotnet build`
Expected: 0 errors.

- [ ] **Run trust + no-regression on the publish path**

Run: `cd /e/digitalbraintech/brain && dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~PublisherTrustTests|FullyQualifiedName~PublishGateTests|FullyQualifiedName~HandlerGrowthTests|FullyQualifiedName~CatalogMaterializationTests|FullyQualifiedName~PackSignature"`
Expected: all pass — critically, `HandlerGrowthTests` and `CatalogMaterializationTests` (which publish unsigned packs with the gate OFF) stay green, proving the gate is non-breaking by default.

> `aspire doctor` not required — Core/Kernel/test only.

## Out of scope (later)

- Facet UI in the Flutter app (tier/channel filters) — the marketplace surface already carries the data (1a-catalog); the app-side UI is a separate app-repo effort.
- Deep-links (1c), Telegram deployed channel (1d).
- Prod config: enabling `DigitalBrain:Marketplace:GatePublishing=true` and populating `TrustedPublisherKeys` in the deployment is an ops/config step, not code.
