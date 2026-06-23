using DigitalBrain.Silo.Llm;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Tests.Llm;

public class ChatClientRegistrationTests
{
    [Fact]
    public void NoProviderConfigured_DoesNotRegisterChatClient()
    {
        var config = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddDigitalBrainChat(config);
        using var sp = services.BuildServiceProvider();
        Assert.Null(sp.GetService<IChatClient>());
    }

    [Fact]
    public void OllamaConfigured_RegistersChatClient()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DigitalBrain:Llm:Provider"] = "ollama",
            ["DigitalBrain:Llm:OllamaEndpoint"] = "http://localhost:11434",
            ["DigitalBrain:Llm:Model"] = "qwen2.5-coder:1.5b",
        }).Build();
        var services = new ServiceCollection();
        services.AddDigitalBrainChat(config);
        using var sp = services.BuildServiceProvider();
        Assert.NotNull(sp.GetService<IChatClient>());
    }
}
