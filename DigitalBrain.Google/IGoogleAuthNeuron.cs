using DigitalBrain.Core;

namespace DigitalBrain.Google;

// Marker interface so tests/callers can resolve the single GoogleAuthNeuron grain unambiguously —
// GetGrain<INeuron>(...) can't disambiguate among the 40+ concrete grain types in DigitalBrain.Kernel
// (same pattern as IDemoNeuron/IGeneratedNeuron in DigitalBrain.Core/Synapse.cs).
public interface IGoogleAuthNeuron : INeuron, IHandle<Signal>
{
}
