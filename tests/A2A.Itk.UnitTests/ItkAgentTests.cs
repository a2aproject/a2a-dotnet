namespace A2A.Itk.UnitTests;

using A2A;

public sealed class ItkAgentTests
{
    [Fact]
    public void GetAgentCard_AdvertisesGrpcInterface()
    {
        var card = ItkAgent.GetAgentCard(10102);

        var grpcInterface = Assert.Single(
            card.SupportedInterfaces,
            agentInterface => agentInterface.ProtocolBinding == ProtocolBindingNames.Grpc);

        Assert.Equal("http://127.0.0.1:11002", grpcInterface.Url);
        Assert.Equal("1.0", grpcInterface.ProtocolVersion);
    }

    [Fact]
    public void GetAgentCard_UsesConfiguredGrpcPort()
    {
        var card = ItkAgent.GetAgentCard(20102, 21002);

        var grpcInterface = Assert.Single(
            card.SupportedInterfaces,
            agentInterface => agentInterface.ProtocolBinding == ProtocolBindingNames.Grpc);

        Assert.Equal("http://127.0.0.1:21002", grpcInterface.Url);
    }
}
