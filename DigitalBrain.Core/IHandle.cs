namespace DigitalBrain.Core;

public interface IHandle<T> where T : Synapse
{
    Task HandleAsync(T synapse);
}
