# Code Foundry

Runtime code-generation & hot-load pipeline. Lets the self-improving loop generate,
compile, run, and durably load real C# at runtime.

## Tiers
- **Tier 1 (Run):** `CodeRunNeuron` → `InProcessAlcExecutor`. Roslyn compile in memory,
  run in a collectible `AssemblyLoadContext`, unload. No restart. For logic/experiences.
- **Tier 2 (Deploy):** `CodeDeployNeuron`. Verify-build to a temp project (Orleans codegen
  runs, silo untouched) → on success commit source to `Generated/` → request Aspire silo
  restart → Orleans auto-registers the new grain types. Journals (Redis) survive.

## Entry points
- MCP: `run_code_foundry(spec, tier, autoApply)`
- Synapse: fire `FoundryRequest` at grain `foundry-main` (`ICodeFoundryLoopNeuron`).

## Safety
- Checkpoint before apply; capability gate (`CapabilityGate`) bans dangerous symbols at
  compile time; Tier-2 restarts only after a passing verify-build; failures roll back.
- The in-process `AssemblyLoadContext` is a guardrail, NOT a security sandbox (.NET has no
  CAS). For untrusted specs, swap `InProcessAlcExecutor` for a future out-of-process
  `ICodeExecutor` implementation.
- **Tier-2 gate caveat:** `CapabilityGate` currently runs only in the Tier-1 in-process
  execution path (`InProcessAlcExecutor`). The Tier-2 deploy path relies on verify-build
  plus checkpoint/rollback for safety; running the gate on Tier-2 source is planned hardening.
