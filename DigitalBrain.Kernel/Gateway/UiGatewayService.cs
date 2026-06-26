using DigitalBrain.Core;
using DigitalBrain.Runtime.Grpc.Ui;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Kernel.Gateway;

// Bidirectional UI session handler. Complements WatchHomeFeed (one-way RfwCard stream).
// Supports interactive canvas (DRAG, CLICK etc from RFW onEvent) and kernel signals back (viewport, future rfw/sound).
// Inputs can trigger neuron actions via existing resolver (data-driven, matches surface action descriptors).
// Reuses HomeFeedBus for potential future unified push of state signals.
public sealed class UiGatewayService(
    IGrainFactory grainFactory,
    ILogger<UiGatewayService> logger) : UiGateway.UiGatewayBase
{
    public override async Task EngageUiSession(
        IAsyncStreamReader<UiInputSynapse> requestStream,
        IServerStreamWriter<UiStateSignal> responseStream,
        ServerCallContext context)
    {
        var cts = context.CancellationToken;

        // Send an initial viewport signal so the bidi session is immediately healthy for canvas auto-layout.
        await responseStream.WriteAsync(new UiStateSignal
        {
            Viewport = new UiViewportSignal { ZoomDepth = 1.0, CenterX = 0, CenterY = 0 }
        }, cts);

        // Read client inputs (from living canvas or rich RFW surfaces) and act.
        // Dispatch via resolver when payload or target indicates a fireable synapse (e.g. from surface actions).
        await foreach (var input in requestStream.ReadAllAsync(cts))
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(input.TargetNeuronId) || !string.IsNullOrWhiteSpace(input.InputPayload))
                {
                    // Best effort: if payload looks like a trigger, fire via resolver (reuses main gateway pattern).
                    // For full actions the RFW onEvent typically uses the main Fire/Send; this keeps bidi path open.
                    var neuronId = string.IsNullOrWhiteSpace(input.TargetNeuronId) ? "ino-main" : input.TargetNeuronId;
                    try
                    {
                        var neuron = NeuronResolver.Resolve(grainFactory, neuronId);
                        if (!string.IsNullOrWhiteSpace(input.InputPayload))
                        {
                            await neuron.FireAsync(new DemoMessageSynapse(input.InputPayload));
                        }
                    }
                    catch (ArgumentException)
                    {
                        // Unknown neuron, ignore for UI session resilience.
                    }
                }

                // Echo a viewport update (or richer signal) to keep live feedback loop for client canvas.
                // In future: on bus events push rfwCard signals here too for single bidi transport.
                var signal = new UiStateSignal
                {
                    Viewport = new UiViewportSignal
                    {
                        ZoomDepth = 1.0,
                        CenterX = input.Coordinates?.X ?? 0,
                        CenterY = input.Coordinates?.Y ?? 0
                    }
                };
                await responseStream.WriteAsync(signal, cts);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "UiGateway input handling non-fatal");
                // Keep session alive for other interactions.
            }
        }
    }
}
