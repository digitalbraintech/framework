var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

var app = builder.Build();

app.MapGet("/health", () => Results.Text("ok"));

app.MapGet("/status", (IConfiguration cfg) =>
{
    // TODO (sub-project B/Task 10): probe IClusterClient + BlobServiceClient and report "reachable"/"unreachable" (wrap in try/catch + ILogger when real probes are added).
    var cluster = "unknown";
    var storage = "unknown";

    return Results.Json(new
    {
        cluster,
        storage,
        llmMode = cfg["DigitalBrain:Llm:Provider"] ?? "none",
        journalSampled = -1,
    });
});

app.Run();

public partial class Program;
