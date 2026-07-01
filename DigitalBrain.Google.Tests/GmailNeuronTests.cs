using DigitalBrain.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Google.Tests;

public class GmailNeuronTests : NeuronTestBase
{
    private readonly FakeGmailApiClient _fake = new();

    protected override void ConfigureSilo(ISiloBuilder builder) =>
        builder.ConfigureServices(services => services.AddSingleton<IGmailApiClient>(_fake));

    [Fact]
    public async Task SendMessageAsync_Records_The_Send_On_The_Fake()
    {
        var gmail = Grain<IGmailNeuron>("gmail-test");
        await gmail.SendMessageAsync("someone@example.com", "hi", "hello there");
        Assert.Single(_fake.SentMessages, m => m.To == "someone@example.com" && m.Subject == "hi");
    }

    [Fact]
    public async Task ListMessagesAsync_Returns_Fake_Results()
    {
        var gmail = Grain<IGmailNeuron>("gmail-list-test");
        var messages = await gmail.ListMessagesAsync("is:unread", 10);
        Assert.NotEmpty(messages);
    }
}
