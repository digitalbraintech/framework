using DigitalBrain.Core;

namespace DigitalBrain.Kernel;

[GrainType("db.support.v1")]
public class DbSupportNeuron(ILogger<DbSupportNeuron> logger, NeuronJournals journals) : Neuron(logger, journals), IDbSupportNeuron
{
    public async Task HandleAsync(DbConnect cmd)
    {
        Logger.LogInformation("DB connected {Name} via {Provider}", cmd.ConnectionName, cmd.Provider);
        // Input already journaled by initiating FireAsync; omit re-fire of handled type to prevent dispatch recursion on echo.
    }

    public async Task HandleAsync(DbQuery cmd)
    {
        if (cmd.Result is not null)
        {
            // This is the echoed result; already journaled. Skip to avoid re-dispatch loop on same IHandle type.
            return;
        }
        Logger.LogInformation("DB query on {Name}: {Q}", cmd.ConnectionName, cmd.Query);
        var result = $"[DB result for {cmd.Query}] 42 rows";
        await FireAsync(new DbQuery(cmd.ConnectionName, cmd.Query, result));
    }
}
