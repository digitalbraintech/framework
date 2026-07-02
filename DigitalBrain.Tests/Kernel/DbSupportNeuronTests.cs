using DigitalBrain.Core;
using DigitalBrain.TestKit;

namespace DigitalBrain.Tests.Kernel;

public class DbSupportNeuronTests : NeuronTestBase
{
    [Fact]
    public async Task DbConnect_Fires_Input_Back()
    {
        var db = Grain<IDbSupportNeuron>("db-test-connect");
        await db.FireAsync(new DbConnect("conn1", "sqlite", "Data Source=:memory:"));

        var timeline = await db.GetTimelineAsync();
        Assert.Contains(timeline, s => s is DbConnect connect && connect.ConnectionName == "conn1" && connect.Provider == "sqlite");
    }

    [Fact]
    public async Task DbQuery_Echoes_Result()
    {
        var db = Grain<IDbSupportNeuron>("db-test-query");
        await db.FireAsync(new DbQuery("conn2", "SELECT COUNT(*) FROM items"));

        var timeline = await db.GetTimelineAsync();
        var response = timeline.OfType<DbQuery>().FirstOrDefault(q => q.ConnectionName == "conn2" && q.Result != null);
        Assert.NotNull(response);
        Assert.Contains("42 rows", response!.Result!);
        Assert.Equal("SELECT COUNT(*) FROM items", response.Query);
    }
}
