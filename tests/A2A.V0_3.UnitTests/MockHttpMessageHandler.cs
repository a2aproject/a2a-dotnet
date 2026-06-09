namespace A2A.V0_3.UnitTests;

public class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpResponseMessage _response;
    private readonly Action<HttpRequestMessage, string?>? _capture;

    public MockHttpMessageHandler(HttpResponseMessage response, Action<HttpRequestMessage, string?>? capture = null)
    {
        _response = response;
        _capture = capture;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? body = null;
        if (request.Content is not null)
        {
            body = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }

        _capture?.Invoke(request, body);
        return _response;
    }
}
