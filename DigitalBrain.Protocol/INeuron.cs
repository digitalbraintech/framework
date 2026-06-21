namespace DigitalBrain.Protocol;

public interface INeuron : IGrainWithStringKey
{
    ValueTask FireAsync<T>(T payload) where T : Synapse;
    Task<IReadOnlyList<Synapse>> GetTimelineAsync();
    Task DeliverAsync(Synapse synapse);
}
