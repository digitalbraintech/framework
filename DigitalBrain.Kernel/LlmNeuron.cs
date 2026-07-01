using DigitalBrain.Core;
using DigitalBrain.Kernel.Foundry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Orleans.Journaling;
using Orleans.Runtime;
using System.Reflection;
using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.CSharp;
namespace DigitalBrain.Kernel;

[GrainType("digitalbrain.llm.qwen.v1")]
public class LlmNeuron : Neuron, ILlmNeuron
{
    public LlmNeuron(ILogger<LlmNeuron> logger, NeuronJournals journals)
        : base(logger, journals)
    {
    }

    public async Task HandleAsync(LlmPrompt prompt)
    {
        var chat = ServiceProvider.GetService<IChatClient>();
        if (chat == null)
        {
            await FireAsync(new LlmResponse(prompt.Prompt, "[no local llm client]", "none"));
            return;
        }

        var options = string.IsNullOrWhiteSpace(prompt.PreferredModel)
            ? null
            : new Microsoft.Extensions.AI.ChatOptions { ModelId = prompt.PreferredModel };
        var response = await chat.GetResponseAsync(prompt.Prompt, options);
        await FireAsync(new LlmResponse(prompt.Prompt, response.Text.Trim(), prompt.PreferredModel ?? "qwen2.5-coder:1.5b"));
    }
}


