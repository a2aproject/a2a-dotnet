using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace A2A.Core;

/// <summary>
/// Resolves Agent Card information from an A2A-compatible endpoint
/// </summary>
public sealed class A2ACardResolver
{
    /// <summary>
    /// Creates a new instance of the A2ACardResolver
    /// </summary>
    /// <param name="httpClient">Optional HTTP client (if not provided, a new one will be created)</param>
    /// <param name="agentCardPath">Path to the agent card (defaults to /.well-known/agent.json)</param>
    /// <param name="logger">Optional logger</param>
    public A2ACardResolver(
        HttpClient httpClient,
        string agentCardPath = "/.well-known/agent.json",
        ILogger? logger = null)
    {
        _agentCardPath = agentCardPath.TrimStart('/');
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(30); // Set a reasonable timeout
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Gets the agent card synchronously
    /// </summary>
    /// <returns>The agent card</returns>
    public AgentCard GetAgentCard()
    {
        return GetAgentCardAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Gets the agent card asynchronously
    /// </summary>
    /// <param name="cancellationToken">Optional cancellation token</param>
    /// <returns>The agent card</returns>
    public async Task<AgentCard> GetAgentCardAsync(CancellationToken cancellationToken = default)
    {
        string url = $"{_httpClient.BaseAddress}/{_agentCardPath}";
        _logger?.LogInformation("Fetching agent card from {Url}", url);

        try
        {
            var response = await _httpClient.GetAsync(_agentCardPath, cancellationToken);

            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStreamAsync();

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            return JsonSerializer.Deserialize<AgentCard>(content, options) ?? throw new A2AClientJsonError($"Failed to parse agent card JSON.");
        }
        catch (JsonException ex)
        {
            _logger?.LogError(ex, "Failed to parse agent card JSON");
            throw new A2AClientJsonError($"Failed to parse JSON: {ex.Message}");
        }
        catch (HttpRequestException ex)
        {
#if NET8_0_OR_GREATER
            int statusCode = (int)(ex.StatusCode ?? System.Net.HttpStatusCode.InternalServerError);
#else
            int statusCode = (int)System.Net.HttpStatusCode.InternalServerError;
#endif
            _logger?.LogError(ex, "HTTP request failed with status code {StatusCode}", statusCode);
            throw new A2AClientHTTPError(statusCode, ex.Message);
        }
    }

    #region private
    private readonly HttpClient _httpClient;
    private readonly string _agentCardPath;
    private readonly ILogger _logger;
    #endregion
}
