using DigitalBrain.Core;

namespace DigitalBrain.Context;

public interface IContextNeuron : INeuron, IHandle<ContextUpdate>
{
    Task<string> GetContextAsync(string contextName);

    // Semantic memory: store a memory (embedded) and recall the most relevant ones for a query.
    // Recall uses an in-grain hybrid (cosine + keyword) scorer; with a NoOp embedder it degrades to keyword.
    Task RememberAsync(string text);
    Task<string[]> RecallAsync(string query, int top = 5);
}
