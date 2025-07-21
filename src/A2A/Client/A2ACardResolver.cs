using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using System.Net;
using System.Text.Json;

namespace A2A;

/// <summary>
/// Resolves Agent Card information from an A2A-compatible endpoint.
/// </summary>
public sealed class A2ACardResolver
{
    private readonly HttpClient _httpClient;
    private readonly Uri _agentCardPath;
    private readonly ILogger _logger;

    /// <summary>
    /// Creates a new instance of the A2ACardResolver.
    /// </summary>
    /// <param name="httpClient">Optional HTTP client (if not provided, a shared one will be used).</param>
    /// <param name="agentCardPath">Path to the agent card (defaults to "/.well-known/agent.json").</param>
    /// <param name="logger">Optional logger.</param>
    public A2ACardResolver(
        HttpClient? httpClient,
        string agentCardPath = "/.well-known/agent.json",
        ILogger? logger = null)
    {
        if (string.IsNullOrEmpty(agentCardPath))
        {
            throw new ArgumentNullException(nameof(agentCardPath), "Agent card path cannot be null or empty.");
        }

        _httpClient = httpClient ?? A2AClient.s_sharedClient;
        _agentCardPath = new Uri(agentCardPath.TrimStart('/'), UriKind.RelativeOrAbsolute);
        _logger = logger ?? NullLogger.Instance;

        if (_httpClient.BaseAddress is null && !_agentCardPath.IsAbsoluteUri)
        {
            throw new ArgumentException($"HttpClient.BaseAddress must be set when using a relative {nameof(agentCardPath)}.", nameof(httpClient));
        }
    }

    /// <summary>
    /// Gets the agent card asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>The agent card.</returns>
    public async Task<AgentCard> GetAgentCardAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_logger.IsEnabled(LogLevel.Information))
        {
            Debug.Assert(_agentCardPath.IsAbsoluteUri || _httpClient.BaseAddress is not null);
            _logger.FetchingAgentCardFromUrl(_agentCardPath.IsAbsoluteUri ?
                _agentCardPath :
                new Uri(_httpClient.BaseAddress!, _agentCardPath.ToString()));
        }

        try
        {
            using var response = await _httpClient.GetAsync(_agentCardPath, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            using var responseStream = await response.Content.ReadAsStreamAsync(
#if NET
                cancellationToken
#endif
                ).ConfigureAwait(false);

            return JsonSerializer.Deserialize(responseStream, A2AJsonUtilities.JsonContext.Default.AgentCard) ??
                throw new A2AException("Failed to parse agent card JSON.");
        }
        catch (JsonException ex)
        {
            _logger.FailedToParseAgentCardJSON(ex);
            throw new A2AException($"Failed to parse JSON: {ex.Message}");
        }
        catch (HttpRequestException ex)
        {
            HttpStatusCode statusCode =
#if NET
                ex.StatusCode ??
#endif
                HttpStatusCode.InternalServerError;

            _logger.HTTPRequestFailedWithStatusCodeStatusCode(ex, statusCode);
            throw new A2AException("HTTP request failed", ex);
        }
    }
}
