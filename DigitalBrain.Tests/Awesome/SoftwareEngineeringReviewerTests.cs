using DigitalBrain.Core;
using DigitalBrain.TestKit;

namespace DigitalBrain.Tests.Awesome;

public class SoftwareEngineeringReviewerTests : NeuronTestBase
{

    [Fact]
    public async Task Reviewer_Reviews_Project_And_Emits_Typed_Result()
    {
        var dir = Path.Combine(Path.GetTempPath(), "se-review-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "a.cs"), "class A {}\n// TODO fix me\n");

            var reviewer = Grain<ISoftwareEngineeringReviewer>("se-reviewer");
            await reviewer.FireAsync(new ReviewProjectRequest(dir));

            var result = (await reviewer.GetTimelineAsync()).OfType<ReviewResult>().LastOrDefault();
            Assert.NotNull(result);
            Assert.True(result!.FileCount >= 1);
            Assert.True(result.TodoCount >= 1);
            Assert.Contains("# Review:", result.Report);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); }
            catch (IOException) { /* best-effort temp cleanup */ }
        }
    }

    [Fact]
    public async Task Reviewer_Reviews_Content_Diff()
    {
        var reviewer = Grain<ISoftwareEngineeringReviewer>("se-content");
        await reviewer.FireAsync(new ReviewRequest("PR-42", "some code\nTODO write tests\n"));

        var result = (await reviewer.GetTimelineAsync()).OfType<ReviewResult>().LastOrDefault();
        Assert.NotNull(result);
        Assert.Equal("PR-42", result!.Target);
        Assert.Equal(1, result.TodoCount);
    }
}

