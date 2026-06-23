using System.Reflection;
using DigitalBrain.Protocol;
using Xunit;

namespace DigitalBrain.Tests.Protocol;

public class JournalJsonContextTests
{
    [Fact]
    public void ContextCoversEverySynapseSubtype()
    {
        var synapseTypes = typeof(Synapse).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(Synapse).IsAssignableFrom(t))
            .ToList();

        var ctx = JournalJsonContext.Default;
        var missing = synapseTypes
            .Where(t => ctx.GetTypeInfo(t) is null)
            .Select(t => t.Name)
            .ToList();

        Assert.True(missing.Count == 0, "JournalJsonContext missing: " + string.Join(", ", missing));
    }
}
