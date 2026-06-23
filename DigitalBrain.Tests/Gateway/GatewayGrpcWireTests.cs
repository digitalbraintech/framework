using DigitalBrain.Runtime.Grpc;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace DigitalBrain.Tests.Gateway;

public class GatewayGrpcWireTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public GatewayGrpcWireTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private DigitalBrainGateway.DigitalBrainGatewayClient NewClient()
    {
        var channel = GrpcChannel.ForAddress(_factory.Server.BaseAddress, new GrpcChannelOptions
        {
            HttpHandler = _factory.Server.CreateHandler()
        });
        return new DigitalBrainGateway.DigitalBrainGatewayClient(channel);
    }

    [Fact]
    public async Task Health_OverGrpc_ReturnsOk()
    {
        var reply = await NewClient().HealthAsync(new HealthRequest());
        Assert.True(reply.Ok);
    }

    [Fact]
    public async Task Ask_Ino_OverGrpc_ReturnsText()
    {
        var reply = await NewClient().AskAsync(new AskRequest { NeuronId = "ino-main", Prompt = "hi" });
        Assert.False(string.IsNullOrWhiteSpace(reply.Text));
    }
}
