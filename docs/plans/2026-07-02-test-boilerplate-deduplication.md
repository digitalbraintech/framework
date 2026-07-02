# Test Boilerplate Deduplication Implementation Plan

> Follow strictly with subagent-driven-development (fresh implementer + reviewer per task or logical chunk). Checkbox steps. Final whole-branch review + finishing-a-development-branch.

**Goal:** Remove duplicated manual cluster boilerplate from `DigitalBrain.Tests/` by migrating to `NeuronTestBase`. Split/extract contents of the grab-bag `UnitTest1.cs` (NeuronTests) into focused domain files. Delete duplication. (Follows Musk 5-step: 1. requirements questioned — see design; 2. strong delete bias on boiler; 3-5 later.)

## Global Constraints
- Mechanical, behavior-preserving.
- Use existing `NeuronTestBase` (from TestKit).
- Relative paths only. NEVER C:\Users paths.
- Self-explanatory names; NO vacuous /// <summary>. Small inline comments ONLY for genuinely non-obvious "why".
- Verification ritual AFTER EVERY change + per task/chunk: `dotnet build` → `dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "<targeted e.g. NeuronCore|Marketplace|Ui|Auth|Context|Gateway>" --no-build -l minimal` (high-severity relevant). aspire doctor ONLY on AppHost/hosting edits (none here).
- Use Context7 (resolve + query) for any Orleans.TestingHost / ISiloConfigurator / grain test API before edits (done; local base + examples govern).
- Branch: spec/test-boilerplate-deduplication (created).
- No edits to .superpowers/sdd/.
- Delete bias (target net delete of boilerplate LOC; ~10% or less add back).
- After changes: use search_replace only after read_file exact text match. Fresh subagents for impl/reviewer work.
- Update CONTINUITY.md + progress ledger at round end.
- Keep slice small/focused (like completed NeuronTestBase slice).

### Task 1: Inventory and audit remaining manual boilerplate

- [x] Grep + reads (limits) + pwsh inventory for `TestClusterBuilder| : IAsyncLifetime` in `DigitalBrain.Tests/**/*.cs` (and Steps). (Current on branch: UnitTest1.cs, Auth/UserSessionNeuronTests.cs, Awesome/SoftwareEngineeringReviewerTests.cs, Company/CompanyKnowledgeTests.cs, Context/ContextRecallTests.cs, Economics/LicenseAndEntitlementTests.cs, Gateway/*Tests (4), Kernel/*Tests (TimelineStream, ExperienceStepDispatch, RollingUpdateRollback), Mcp/DigitalBrainToolsTests.cs, Telegram/TelegramDeepLinkRoutingTests.cs, Ui/ChatNeuronTests.cs, Steps/*, E2E fixture — audit which can migrate vs must stay for collection fixtures/custom config.)
- [ ] Read full NeuronTestBase.cs + TestDigitalBrain.cs + 2-3 migrated examples (e.g. ContextNeuronTests.cs) + current UnitTest1.cs for exact patterns.
- [x] Run baseline targeted tests + build (passed pre-edit on branch).
- [ ] Note any strict configurator helpers or side-effect tests (e.g. git/temp in developer steps — may need ConfigureSilo override in subclass).

### Task 2: Migrate non-grab-bag files to NeuronTestBase (chunked)

- [x] Group 1 (simple recall/auth/company): ContextRecallTests.cs, UserSessionNeuronTests.cs, CompanyKnowledgeTests.cs (plus dead _services delete) — completed by impl+review subagents; rituals green (21 pass).
- [x] Group 2 partial: DigitalBrainToolsTests.cs (Mcp; custom IGrainFactory wrapper adapted for protected Grain access) migrated + fixed (build+2 pass). ChatNeuronTests was already on base. Gateway* , TimelineStream, TelegramDeepLink use custom silo/client configs + collections + extra buses (strict; deferred per plan note "strict configurator helpers"; not mechanical). SoftwareEngineeringReviewerTests (standard) migrated manually (2 pass).
- [ ] Group 3 (other + economics + steps audit): LicenseAndEntitlementTests.cs, ExperienceStepDispatchTests, RollingUpdateRollbackTests, HomeFeedCrossSiloTests — use custom or collection; left for audit. E2E fixture non-migratable (hosting). Steps/Reqnroll specific stay. UnitTest1 grab handled separately.
- [ ] After groups: full grep for stray manual in DigitalBrain.Tests/ (should only be E2E fixture + Steps if any).
- [ ] Commit groups with focused messages.

### Task 3: Split/extract UnitTest1.cs (NeuronTests) grab-bag

- [ ] Read full `UnitTest1.cs` (exact current content; ~600 LOC, mixed concerns + IDemoNeuron usage).
- [ ] Create focused (self-explanatory):
  - `Kernel/NeuronCoreTests.cs`: activation/journal/fire/status/branch/restore/embody facts using Grain<IDemoNeuron> + Fire/Deliver (or typed if possible).
  - `Distribution/MarketplaceCoreTests.cs` (or keep in place if small): publish/install/embody related.
  - Other extracts only if clear domain split adds value; prefer minimal moves to avoid churn (delete bias on boiler > reorganization).
- [ ] Delete the original UnitTest1.cs file after extracting value (or empty + git rm if all moved). Update any [Trait] or namespaces for clarity. File name "UnitTest1" is dumb legacy.
- [ ] Ritual after: build + broad filter "NeuronCore|Marketplace|Ui".
- [ ] Note: preserve useful tests/asserts; IDemoNeuron usage OK here (practical).

### Task 4: Cleanup + final verification

- [ ] Remove unused usings after migrations (self-explanatory).
- [ ] Grep for stray "TestClusterBuilder| : IAsyncLifetime" (exclude E2E fixture + any justified Steps) in DigitalBrain.Tests/.
- [ ] Full relevant test filters across touched (e.g. "NeuronCore|Marketplace|Ui|Context|Auth|Gateway|Mcp|Kernel") + build. High-sev only.
- [ ] Update docs/CONTINUITY.md with one-line ledger (this slice).
- [ ] Prepare diff range for reviewer (git diff master..HEAD or similar).
- [ ] Verify no AppHost changes (no aspire doctor needed).

### Task 5: Review + handoff + finishing

- [ ] Fresh implementer subagent(s) per chunk (above groups) + reviewer subagent per logical chunk or final.
- [ ] Whole-branch code review (fresh reviewer subagent) on the full diff vs plan + guardrails (delete bias, self-explanatory names, ritual evidence, Context7 use, relative paths, no vacuous comments).
- [ ] Address any findings (0 critical/important target).
- [ ] Final ritual: build + broad targeted tests.
- [ ] Finishing-a-development-branch (per skill: present options for merge/PR/cleanup, update progress).
- [ ] Append ledger entry to CONTINUITY.md and/or .superpowers/sdd/progress.md (do not edit sdd/ during impl per constraints; update at end).

This eliminates the post-NeuronTestBase dupe and makes the main test project consistent with the dedicated ones. Small, focused, delete-heavy where possible. Ships independently.