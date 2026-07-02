# DbSupportNeuron Tests Implementation Plan

**Goal:** Add small dedicated tests for the untested DbSupportNeuron (closes orphan gap). Use NeuronTestBase. Delete bias where possible (minimal code).

## Global Constraints
- Use existing `NeuronTestBase`.
- Relative paths.
- Ritual: `dotnet build` + `dotnet test --filter "DbSupport|Kernel" --no-build` after every change.
- Self-explanatory names, no vacuous comments.
- Context7 for any new grain/stream usage (if any; likely not).
- Fresh subagent for impl + reviewer.
- Update CONTINUITY at end of round.

### Task 1: Audit current wiring (read + grep)

- [ ] Read `DigitalBrain.Kernel/DbSupportNeuron.cs` full.
- [ ] Read interface + synapses in `DigitalBrain.Core/Synapse.cs`.
- [ ] Grep wiring sites (Resolver, Program, Mcp.Tools, any tests).
- [ ] Confirm zero *Tests for it.
- [ ] Baseline build + test filter "Kernel".
- [ ] Read example `NeuronTestBase` usage (e.g. a simple Kernel test).

### Task 2: Implement the tests

- [ ] Create `DigitalBrain.Tests/Kernel/DbSupportNeuronTests.cs`.
- [ ] Class `DbSupportNeuronTests : NeuronTestBase`.
- [ ] Test 1: `DbConnect_Fires_Input_Back` — Grain<IDbSupportNeuron>, FireAsync(new DbConnect(...)), assert timeline contains the command.
- [ ] Test 2: `DbQuery_Echoes_Result` — similar, assert result prop in the fired response.
- [ ] Use `Grain<T>`, `FireAsync`.
- [ ] Build + ritual filter.
- [ ] Commit "test(kernel): add dedicated DbSupportNeuronTests (covers the previous zero-test orphan)".

### Task 3: Final

- [ ] Grep no other references needed.
- [ ] Full Kernel filter + build.
- [ ] Reviewer subagent.
- [ ] Update CONTINUITY.
- [ ] Finishing.

Small slice. All rituals. Ready for next.