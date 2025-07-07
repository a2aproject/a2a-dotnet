using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
#if NET
using System.Diagnostics;
#endif
using System.Net;
using System.Text.Json;

namespace A2A;

/// <summary>
/// Resolves Agent Card information from an A2A-compatible endpoint
/// </summary>
public sealed class A2ACardResolver
{
    private static readonly HttpClient s_sharedClient = new();

    private readonly HttpClient _httpClient;
    private readonly Uri _agentCardPath;
    private readonly ILogger _logger;

    /// <summary>
    /// Creates a new instance of the A2ACardResolver
    /// </summary>
    /// <param name="httpClient">Optional HTTP client (if not provided, a shared one will be used)</param>
    /// <param name="agentCardPath">Path to the agent card (defaults to /.well-known/agent.json)</param>
    /// <param name="logger">Optional logger</param>
    public A2ACardResolver(
        HttpClient? httpClient = null,
        string agentCardPath = "/.well-known/agent.json",
        ILogger? logger = null)
    {
        if (agentCardPath is null)
        {
            throw new ArgumentNullException(nameof(agentCardPath), "Agent card path cannot be null.");
        }

        _httpClient = httpClient ?? s_sharedClient;
        _agentCardPath = new Uri(agentCardPath.TrimStart('/'), UriKind.RelativeOrAbsolute);
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Gets the agent card synchronously
    /// </summary>
    /// <returns>The agent card</returns>
    public AgentCard GetAgentCard()
    {
        Task<AgentCard> t = GetAgentCardAsync(useAsync: false, default);
#if NET
        Debug.Assert(t.IsCompleted, "With synchronous APIs available, specifying useAsync:false should always result in synchronous completion.");
#endif
        return t.GetAwaiter().GetResult();
    }

    /// <summary>
    /// Gets the agent card asynchronously
    /// </summary>
    /// <param name="cancellationToken">Optional cancellation token</param>
    /// <returns>The agent card</returns>
    public Task<AgentCard> GetAgentCardAsync(CancellationToken cancellationToken = default) =>
        GetAgentCardAsync(useAsync: true, cancellationToken);

    private async Task<AgentCard> GetAgentCardAsync(
        bool useAsync,
        CancellationToken cancellationToken)
    {
        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("Fetching agent card from {Url}", $"{_httpClient.BaseAddress}/{_agentCardPath}");
        }

        try
        {
            using var response =
#if NET
                !useAsync ? _httpClient.Send(new(HttpMethod.Get, _agentCardPath), HttpCompletionOption.ResponseHeadersRead, cancellationToken) :
#endif
                await _httpClient.GetAsync(_agentCardPath, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            using var responseStream =
#if NET
                !useAsync ?
                    response.Content.ReadAsStream(cancellationToken) :
                    await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
#else
                await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
#endif

            return JsonSerializer.Deserialize(responseStream, A2AJsonUtilities.JsonContext.Default.AgentCard) ??
                throw new A2AClientJsonException("Failed to parse agent card JSON.");
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse agent card JSON");
            throw new A2AClientJsonException($"Failed to parse JSON: {ex.Message}");
        }
        catch (HttpRequestException ex)
        {
            HttpStatusCode statusCode =
#if NET
                ex.StatusCode ??
#endif
                HttpStatusCode.InternalServerError;

            _logger.LogError(ex, "HTTP request failed with status code {StatusCode}", statusCode);
            throw new A2AClientHTTPException(statusCode, ex.Message);
        }
    }
}
