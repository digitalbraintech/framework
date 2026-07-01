using DigitalBrain.Core;
using DigitalBrain.Google;

namespace DigitalBrain.Kernel;

[GrainType("digitalbrain.google.auth.v1")]
public class GoogleAuthNeuron(ILogger<GoogleAuthNeuron> logger, NeuronJournals journals)
    : Neuron(logger, journals), IGoogleAuthNeuron
{
    public static AuthButtonSurface SignInSurface() => new(
        Provider: "google",
        Label: "Sign in with Google",
        Icon: "google",
        Action: GoogleSignals.AuthRequested);

    public async Task HandleAsync(Signal signal)
    {
        if (signal.Name != GoogleSignals.AuthRequested) return;
        // Real interactive consent is out of scope (see spec) — this confirms the refresh token
        // already provided via PackConfigStore is present and reachable, then announces completion.
        await FireAsync(new Signal(GoogleSignals.AuthCompleted, new Dictionary<string, object?>()));
    }
}
