namespace A2A.V0_3.UnitTests;

public class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpResponseMessage _response;
    private readonly Action<HttpRequestMessage>? _capture;

    public MockHttpMessageHandler(HttpResponseMessage response, Action<HttpRequestMessage>? capture = null)
    {
        _response = response;
        _capture = capture;
    }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _capture?.Invoke(request);
        return Task.FromResult(_response);
    }
}
