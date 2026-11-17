namespace A2A.V0_3Compat.UnitTests;

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
        if (_response.Content is StringContent)
        {
            using var requestDocument = System.Text.Json.JsonDocument.Parse(request.Content!.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult());
            var id = requestDocument.RootElement.GetProperty("id").GetString();
            var body = _response.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            var updated = body.Replace("\"id\":\"test-id\"", $"\"id\":{System.Text.Json.JsonSerializer.Serialize(id)}", StringComparison.Ordinal)
                .Replace("\"id\":\"request-1\"", $"\"id\":{System.Text.Json.JsonSerializer.Serialize(id)}", StringComparison.Ordinal);
            if (updated != body)
            {
                var media = _response.Content.Headers.ContentType!.MediaType!;
                _response.Content.Dispose();
                _response.Content = new StringContent(updated, System.Text.Encoding.UTF8, media);
            }
        }
        return Task.FromResult(_response);
    }
}
