namespace A2A.V0_3Compat.UnitTests;

public class V03CompatClientFactoryTests
{
    [Fact]
    public void Create_WithUrl_ReturnsV03Adapter()
    {
        var client = V03CompatClientFactory.Create(new Uri("http://localhost/a2a"));

        Assert.IsAssignableFrom<IA2AClient>(client);
        Assert.IsNotType<A2A.A2AClient>(client);
    }

    [Fact]
    public void Create_WithJson_UsesUrlFromCard()
    {
        var cardJson = """
        {
            "name": "Legacy Agent",
            "url": "http://agent-host/a2a",
            "protocolVersion": "0.3"
        }
        """;

        var client = V03CompatClientFactory.Create(cardJson, new Uri("http://fallback-host"));

        Assert.IsAssignableFrom<IA2AClient>(client);
        Assert.IsNotType<A2A.A2AClient>(client);
    }

    [Fact]
    public void Create_WithJson_NoUrlInCard_UsesBaseUrl()
    {
        var cardJson = """
        {
            "name": "Legacy Agent",
            "protocolVersion": "0.3"
        }
        """;

        var client = V03CompatClientFactory.Create(cardJson, new Uri("http://fallback-host/a2a"));

        Assert.IsAssignableFrom<IA2AClient>(client);
    }
}
