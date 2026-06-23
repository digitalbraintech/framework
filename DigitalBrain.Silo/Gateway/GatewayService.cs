using DigitalBrain.Protocol;
using DigitalBrain.Runtime.Grpc;
using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Silo.Gateway;

public sealed class GatewayService(
    IGrainFactory grains,
    IConfiguration configuration,
    ILogger<GatewayService> logger) : DigitalBrainGateway.DigitalBrainGatewayBase
{
    public override Task<HealthReply> Health(HealthRequest request, ServerCallContext context) =>
        Task.FromResult(new HealthReply
        {
            Ok = true,
            LlmMode = configuration["DigitalBrain:Llm:Provider"] ?? "none"
        });

    public override async Task<AskReply> Ask(AskRequest request, ServerCallContext context)
    {
        var neuronId = string.IsNullOrWhiteSpace(request.NeuronId) ? "ino-main" : request.NeuronId;
        if (neuronId != "ino-main")
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Ask currently supports only 'ino-main'."));

        try
        {
            var ino = grains.GetGrain<IInoNeuron>(neuronId);
            return new AskReply { Text = await ino.AskAsync(request.Prompt) };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ask failed for {NeuronId}", neuronId);
            throw new RpcException(new Status(StatusCode.Internal, ex.GetBaseException().Message));
        }
    }

    public override async Task<FireReply> Fire(FireRequest request, ServerCallContext context)
    {
        try
        {
            var neuron = NeuronResolver.Resolve(grains, request.NeuronId);
            await neuron.FireAsync(new DemoMessageSynapse(request.Text));
            return new FireReply { Accepted = true };
        }
        catch (ArgumentException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Fire failed for {NeuronId}", request.NeuronId);
            throw new RpcException(new Status(StatusCode.Internal, ex.GetBaseException().Message));
        }
    }

    public override async Task<TimelineReply> Timeline(TimelineRequest request, ServerCallContext context)
    {
        var max = request.MaxEntries <= 0 ? 10 : request.MaxEntries;
        var neuron = NeuronResolver.Resolve(grains, request.NeuronId);
        var timeline = await neuron.GetTimelineAsync();

        var reply = new TimelineReply();
        foreach (var s in timeline.TakeLast(max))
        {
            reply.Entries.Add(new TimelineEntry
            {
                Type = s.Type,
                Timestamp = s.Timestamp.ToString("O"),
                Text = s.ToString() ?? string.Empty
            });
        }
        return reply;
    }
}
