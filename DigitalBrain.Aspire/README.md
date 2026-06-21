# DigitalBrain.Aspire

MVP Aspire hosting resource and SDK for DigitalBrain (core + marketplace + self-aware LLM).

## Usage (intended fluent API)

```csharp
var db = builder.AddDigitalBrain("digitalbrain", options =>
{
    options.LlmModel = "qwen2.5-coder:1.5b";
    options.KernelReplicas = 3;
    options.UseLocalMarketplace = true;
})
.WithLLM()
.WithTUI(explicitStart: true)
.WithMarketplace(cfg => cfg.UseLocal = true)
.AddExperience<SomeExperience>(cfg => { ... })
.WithKernelReplicas(3);

// Then add your silo and cli projects with .WithReference to the orleans/llm resources set up by the SDK.
```

See the main AppHost for current wiring example.

The resource enables future full encapsulation and self-awareness features (SystemStatusNeuron connects to own Aspire MCP for observability and auto-diagnosis).

## Self-awareness
- SystemStatusNeuron activates on kernel launch
- Attempts MCP connection to own Aspire instance (using pattern from AspireAgent examples)
- Uses local LLM (Qwen) to analyze issues
- Fires FixProposal and runs isolated simulations from journal checkpoints

This is part of preparing DigitalBrain for open source with excellent structure and demo state.