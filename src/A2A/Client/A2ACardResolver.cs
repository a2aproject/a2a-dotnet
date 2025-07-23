using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
    /// Initializes a new instance of <see cref="A2ACardResolver"/>.
    /// </summary>
    /// <param name="baseUrl">The base url of the agent's hosting service.</param>
    /// <param name="httpClient">Optional HTTP client (if not provided, a shared one will be used).</param>
    /// <param name="agentCardPath">Path to the agent card (defaults to "/.well-known/agent.json").</param>
    /// <param name="logger">Optional logger.</param>
    public A2ACardResolver(
        Uri baseUrl,
        HttpClient? httpClient = null,
        string agentCardPath = "/.well-known/agent.json",
        ILogger? logger = null)
    {
        if (baseUrl is null)
        {
            throw new ArgumentNullException(nameof(baseUrl), "Base URL cannot be null.");
        }

        if (string.IsNullOrEmpty(agentCardPath))
        {
            throw new ArgumentNullException(nameof(agentCardPath), "Agent card path cannot be null or empty.");
        }

        _agentCardPath = new Uri(baseUrl, agentCardPath.TrimStart('/'));

        _httpClient = httpClient ?? A2AClient.s_sharedClient;

        _logger = logger ?? NullLogger.Instance;
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
            _logger.FetchingAgentCardFromUrl(_agentCardPath);
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

            return await JsonSerializer.DeserializeAsync(responseStream, A2AJsonUtilities.JsonContext.Default.AgentCard, cancellationToken).ConfigureAwait(false) ??
                throw new A2AException("Failed to parse agent card JSON.");
        }
        catch (JsonException ex)
        {
            _logger.FailedToParseAgentCardJson(ex);
            throw new A2AException($"Failed to parse JSON: {ex.Message}");
        }
        catch (HttpRequestException ex)
        {
            HttpStatusCode statusCode =
#if NET
                ex.StatusCode ??
#endif
                HttpStatusCode.InternalServerError;

            _logger.HttpRequestFailedWithStatusCode(ex, statusCode);
            throw new A2AException("HTTP request failed", ex);
        }
    }
}
