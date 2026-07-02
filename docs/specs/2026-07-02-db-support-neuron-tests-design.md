# DbSupportNeuron Dedicated Tests — Design

**Date:** 2026-07-02
**Status:** Design
**Scope:** Add minimal targeted tests for the DbSupportNeuron path (Core interface + Kernel impl + wiring in Resolver/Mcp/Program). No prod changes.

## Context + Musk Step 1
- Post previous rounds: test boiler dedup landed, marker aliases trimmed, UnitTest1 inners cleaned.
- DbSupportNeuron remains the exact orphan called out in initial research: wired (Mcp "db_example" tool, NeuronResolver "db-main", Program warmup probe, Gateway), zero tests, tiny impl (log + Fire the input + echo result).
- Assumption "it's just an example, leave untested": dumb. It's exposed via MCP and resolver; if it regresses the contract changes, it will be silent until runtime. Trace to real need: MCP tools and dynamic grain resolution in Gateway.
- "Add tests" fits after delete work (Musk 2 done; now coverage for purity).

## Goals
- `DbSupportTests : NeuronTestBase` covering `DbConnect` / `DbQuery` (Fire, assert timeline events + echoed result).
- Use existing harness (Grain<T>, FireAsync/DeliverAsync).
- Keep Core/Kernel exactly as-is (protocol + impl purity).
- Self-explanatory test names. Delete bias (no bloat).

## Design
- New file `DigitalBrain.Tests/Kernel/DbSupportNeuronTests.cs` (or under a Db/ folder if exists; keep flat in Kernel/ for now).
- Tests:
  - Handle DbConnect → fires the command as event.
  - Handle DbQuery → fires result with echoed data.
- Simple, no extra DI.
- Verification: build + filter "DbSupport|Kernel".

## Non-goals
- Full integration with real DB (it's a stub).
- Changing the interface/impl.

## Risks
Zero. Pure addition of tests for existing path. Uses proven NeuronTestBase.

This closes the "DbSupportNeuron orphan" item from the original cleanup list. Small independent slice.