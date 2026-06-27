using Grpc.Core;

namespace DigitalBrain.Tests.TestSupport;

// Minimal ServerCallContext for unit-testing gRPC service methods directly (no transport).
public sealed class TestServerCallContext : ServerCallContext
{
    private readonly CancellationToken _cancellationToken;

    private TestServerCallContext(CancellationToken cancellationToken = default)
    {
        _cancellationToken = cancellationToken;
    }

    public static TestServerCallContext Create(CancellationToken cancellationToken = default) => new(cancellationToken);
    protected override string MethodCore => "test";
    protected override string HostCore => "test";
    protected override string PeerCore => "test";
    protected override DateTime DeadlineCore => DateTime.MaxValue;
    protected override Metadata RequestHeadersCore => new();
    protected override CancellationToken CancellationTokenCore => _cancellationToken;
    protected override Metadata ResponseTrailersCore { get; } = new();
    protected override Status StatusCore { get; set; }
    protected override WriteOptions? WriteOptionsCore { get; set; }
    protected override AuthContext AuthContextCore => new(string.Empty, new Dictionary<string, List<AuthProperty>>());
    protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options) => null!;
    protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
}
