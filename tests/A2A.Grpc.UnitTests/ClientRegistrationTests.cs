namespace A2A.Grpc.UnitTests;

using A2A;

public sealed class ClientRegistrationTests
{
    [Theory]
    [InlineData("localhost:50051", "https://localhost:50051/")]
    [InlineData("127.0.0.1:50051", "https://127.0.0.1:50051/")]
    [InlineData("grpc.example.com:443", "https://grpc.example.com/")]
    [InlineData("[::1]:50051", "https://[::1]:50051/")]
    public void ResolveAddress_SchemeLessAddress_DefaultsToHttps(string address, string expected)
    {
        var result = A2AGrpcClientRegistration.ResolveAddress(address, useTlsForSchemeLessAddresses: true);

        Assert.Equal(expected, result.AbsoluteUri);
    }

    [Fact]
    public void ResolveAddress_SchemeLessAddress_CanUsePlaintext()
    {
        var result = A2AGrpcClientRegistration.ResolveAddress(
            "127.0.0.1:50051",
            useTlsForSchemeLessAddresses: false);

        Assert.Equal("http://127.0.0.1:50051/", result.AbsoluteUri);
    }

    [Theory]
    [InlineData("http://localhost:50051", "http://localhost:50051/")]
    [InlineData("https://grpc.example.com:8443", "https://grpc.example.com:8443/")]
    public void ResolveAddress_ExplicitHttpScheme_IsPreserved(string address, string expected)
    {
        var result = A2AGrpcClientRegistration.ResolveAddress(address, useTlsForSchemeLessAddresses: true);

        Assert.Equal(expected, result.AbsoluteUri);
    }

    [Theory]
    [InlineData("")]
    [InlineData("localhost")]
    [InlineData("localhost:")]
    [InlineData("localhost:0")]
    [InlineData("localhost:65536")]
    [InlineData("localhost:not-a-port")]
    [InlineData("localhost:50051/path")]
    [InlineData("localhost:50051?query=value")]
    [InlineData("localhost:50051#fragment")]
    [InlineData("user@localhost:50051")]
    [InlineData("::1:50051")]
    [InlineData("-grpc.example.com:50051")]
    [InlineData("grpc-.example.com:50051")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.example.com:50051")]
    [InlineData("ftp://localhost:50051")]
    public void ResolveAddress_InvalidAddress_ThrowsInvalidRequest(string address)
    {
        var exception = Assert.Throws<A2AException>(() =>
            A2AGrpcClientRegistration.ResolveAddress(address, useTlsForSchemeLessAddresses: true));

        Assert.Equal(A2AErrorCode.InvalidRequest, exception.ErrorCode);
        Assert.Contains("gRPC interface address", exception.Message);
    }

    [Fact]
    public void Register_SchemeLessAddress_CreatesGrpcClient()
    {
        A2AGrpcClientRegistration.Register();
        var card = new AgentCard
        {
            Name = "peer",
            Description = "peer",
            Version = "1.0",
            SupportedInterfaces =
            [
                new AgentInterface
                {
                    ProtocolBinding = ProtocolBindingNames.Grpc,
                    Url = "localhost:50051",
                    ProtocolVersion = "1.0",
                },
            ],
        };

        using var client = Assert.IsType<A2AGrpcClient>(A2AClientFactory.Create(
            card,
            options: new A2AClientOptions { PreferredBindings = [ProtocolBindingNames.Grpc] }));
    }
}
