using A2A;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Polly;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace SemanticKernelAgent;

#region Plugin
/// <summary>
/// A simple currency plugin that leverages Frankfurter for exchange rates.
/// The Plugin is used by the currency_exchange_agent.
/// </summary>
public class CurrencyPlugin
{
    private readonly ILogger<CurrencyPlugin> _logger;
    private readonly HttpClient _httpClient;
    private readonly AsyncPolicy<HttpResponseMessage> _retryPolicy;

    /// <summary>
    /// Initialize a new instance of the CurrencyPlugin
    /// </summary>
    /// <param name="logger">Logger for the plugin</param>
    /// <param name="httpClientFactory">HTTP client factory for making API requests</param>
    public CurrencyPlugin(ILogger<CurrencyPlugin> logger, HttpClient httpClient)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

        // Create a retry policy for transient HTTP errors
        _retryPolicy = Policy
            .HandleResult<HttpResponseMessage>(r => !r.IsSuccessStatusCode && IsTransientError(r))
            .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
    }

    /// <summary>
    /// Retrieves exchange rate between currency_from and currency_to using Frankfurter API
    /// </summary>
    /// <param name="currencyFrom">Currency code to convert from, e.g. USD</param>
    /// <param name="currencyTo">Currency code to convert to, e.g. EUR or INR</param>
    /// <param name="date">Date or 'latest'</param>
    /// <returns>String representation of exchange rate</returns>
    [KernelFunction]
    [Description("Retrieves exchange rate between currency_from and currency_to using Frankfurter API")]
    public async Task<string> GetExchangeRateAsync(
        [Description("Currency code to convert from, e.g. USD")] string currencyFrom,
        [Description("Currency code to convert to, e.g. EUR or INR")] string currencyTo,
        [Description("Date or 'latest'")] string date = "latest")
    {
        try
        {
            _logger.LogInformation("Getting exchange rate from {CurrencyFrom} to {CurrencyTo} for date {Date}",
                currencyFrom, currencyTo, date);

            // Build request URL with query parameters
            var requestUri = $"https://api.frankfurter.app/{date}?from={Uri.EscapeDataString(currencyFrom)}&to={Uri.EscapeDataString(currencyTo)}";

            // Use retry policy for resilience
            var response = await _retryPolicy.ExecuteAsync(() => _httpClient.GetAsync(requestUri));
            response.EnsureSuccessStatusCode();

            var jsonContent = await response.Content.ReadAsStringAsync();
            var data = JsonSerializer.Deserialize<JsonElement>(jsonContent);

            if (!data.TryGetProperty("rates", out var rates) ||
                !rates.TryGetProperty(currencyTo, out var rate))
            {
                _logger.LogWarning("Could not retrieve rate for {CurrencyFrom} to {CurrencyTo}", currencyFrom, currencyTo);
                return $"Could not retrieve rate for {currencyFrom} to {currencyTo}";
            }

            return $"1 {currencyFrom} = {rate.GetDecimal()} {currencyTo}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting exchange rate from {CurrencyFrom} to {CurrencyTo}", currencyFrom, currencyTo);
            return $"Currency API call failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Checks if the HTTP response indicates a transient error
    /// </summary>
    /// <param name="response">HTTP response message</param>
    /// <returns>True if the status code indicates a transient error</returns>
    private static bool IsTransientError(HttpResponseMessage response) =>
        (int)response.StatusCode is 408 // Request Timeout
            or 429 // Too Many Requests
            or >= 500 and < 600; // Server errors
}
#endregion

#region Semantic Kernel Agent

/// <summary>
/// Wraps Semantic Kernel-based agents to handle Travel related tasks
/// </summary>
public class SemanticKernelTravelAgent : IDisposable
{
    public static readonly ActivitySource ActivitySource = new("A2A.SemanticKernelTravelAgent", "1.0.0");

    /// <summary>
    /// Initializes a new instance of the SemanticKernelTravelAgent
    /// </summary>
    /// <param name="configuration">Application configuration</param>
    /// <param name="httpClient">HTTP client</param>
    /// <param name="logger">Logger for the agent</param>
    public SemanticKernelTravelAgent(
        IConfiguration configuration,
        HttpClient httpClient,
        ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

        // Create currency plugin
        _currencyPlugin = new CurrencyPlugin(
            logger: new Logger<CurrencyPlugin>(new LoggerFactory()),
            httpClient: _httpClient);

        // Initialize the agent
        _agent = InitializeAgent();
    }

    /// <summary>
    /// Dispose of resources
    /// </summary>
    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    public void Attach(ITaskManager taskManager)
    {
        _taskManager = taskManager;
        taskManager.OnTaskCreated = ExecuteAgentTask;
        taskManager.OnTaskUpdated = ExecuteAgentTask;
        taskManager.OnAgentCardQuery = GetAgentCard;
    }

    public async Task ExecuteAgentTask(AgentTask task)
    {
        if (_taskManager == null)
        {
            throw new InvalidOperationException("TaskManager is not attached.");
        }

        await _taskManager.UpdateStatusAsync(task.Id, TaskState.Working);

        // Get message from the user
        var userMessage = task.History!.Last().Parts.First().AsTextPart().Text;

        // Get the response from the agent
        var artifact = new Artifact();
        await foreach (AgentResponseItem<ChatMessageContent> response in _agent.InvokeAsync(userMessage))
        {
            var content = response.Message.Content;
            artifact.Parts.Add(new TextPart() { Text = content! });
        }

        // Return as artifacts
        await _taskManager.ReturnArtifactAsync(task.Id, artifact);
        await _taskManager.UpdateStatusAsync(task.Id, TaskState.Completed);
    }

    public static AgentCard GetAgentCard(string agentUrl)
    {
        var capabilities = new AgentCapabilities()
        {
            Streaming = false,
            PushNotifications = false,
        };

        var skillTripPlanning = new AgentSkill()
        {
            Id = "trip_planning_sk",
            Name = "Semantic Kernel Trip Planning",
            Description = "Handles comprehensive trip planning, including currency exchanges, itinerary creation, sightseeing, dining recommendations, and event bookings using Frankfurter API for currency conversions.",
            Tags = ["trip", "planning", "travel", "currency", "semantic-kernel"],
            Examples =
            [
                "I am from Korea. Plan a budget-friendly day trip to Dublin including currency exchange.",
                "I am from Korea. What's the exchange rate and recommended itinerary for visiting Galway?",
            ],
        };

        return new AgentCard()
        {
            Name = "SK Travel Agent",
            Description = "Semantic Kernel-based travel agent providing comprehensive trip planning services including currency exchange and personalized activity planning.",
            Url = agentUrl,
            Version = "1.0.0",
            DefaultInputModes = ["text"],
            DefaultOutputModes = ["text"],
            Capabilities = capabilities,
            Skills = [skillTripPlanning],
        };
    }

    #region private
    private readonly ILogger _logger;
    private readonly IConfiguration _configuration;
    private readonly CurrencyPlugin _currencyPlugin;
    private readonly HttpClient _httpClient;
    private readonly ChatCompletionAgent _agent;
    private ITaskManager? _taskManager;

    public List<string> SupportedContentTypes { get; } = ["text", "text/plain"];

    private ChatCompletionAgent InitializeAgent()
    {
        try
        {
            var openAiConfig = _configuration.GetSection("OpenAI") ?? throw new ArgumentException("OpenAI configuration must be provided");
            string apiKey = openAiConfig["ApiKey"] ?? throw new ArgumentException("OPENAI_API_KEY must be provided");
            string modelId = openAiConfig["Model"] ?? "gpt-4.1";

            _logger.LogInformation("Initializing Semantic Kernel agent with model {ModelId}", modelId);

            // Define the TravelPlannerAgent
            var builder = Kernel.CreateBuilder();
            builder.AddOpenAIChatCompletion(modelId, apiKey);
            builder.Plugins.AddFromObject(_currencyPlugin);

            var kernel = builder.Build();
            var travelPlannerAgent = new ChatCompletionAgent()
            {
                Kernel = kernel,
                Arguments = new KernelArguments(new PromptExecutionSettings()
                { FunctionChoiceBehavior = FunctionChoiceBehavior.Auto() }),
                Name = "TravelPlannerAgent",
                Instructions =
                    """
                    You specialize in planning and recommending activities for travelers.
                    This includes suggesting sightseeing options, local events, dining recommendations,
                    booking tickets for attractions, advising on travel itineraries, and ensuring activities
                    align with traveler preferences and schedule.
                    Your goal is to create enjoyable and personalized experiences for travelers.
                    You specialize in planning and recommending activities for travelers.
                    This includes suggesting sightseeing options, local events, dining recommendations,
                    booking tickets for attractions, advising on travel itineraries, and ensuring activities
                    align with traveler preferences and schedule.
                    This includes providing current exchange rates, converting amounts between different currencies,
                    explaining fees or charges related to currency exchange, and giving advice on the best practices for exchanging currency.
                    Your goal is to create enjoyable and personalized experiences for travelers.
                    """
            };

            return travelPlannerAgent;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Semantic Kernel agent");
            throw;
        }
    }

    #endregion
}
#endregion

