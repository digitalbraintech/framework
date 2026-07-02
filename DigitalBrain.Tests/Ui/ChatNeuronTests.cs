using DigitalBrain.Core;
using DigitalBrain.TestKit;

namespace DigitalBrain.Tests.Ui;

public class ChatNeuronTests : NeuronTestBase
{
    [Fact]
    public async Task Visualize_Request_Emits_RfwCard_Into_Conversation()
    {
        var chat = Grain<IChatNeuron>("chat-test");
        await chat.FireAsync(new VisualizeDataRequest("show sales", "[{\"m\":\"Jan\"}]", "bar", "req-1"));

        var conversation = await chat.GetConversationAsync();
        Assert.Contains(conversation, c => c.RootWidget == "DataChartCard");
        Assert.Contains(conversation, c => c.DataJson.Contains("show sales"));
    }
}

