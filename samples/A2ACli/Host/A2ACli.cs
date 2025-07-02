using Microsoft.Extensions.Logging;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace A2A;

/// <summary>
/// This is a port of the A2A cli from the Google project. Adding this to help ensure we have feature parity.
/// </summary>
public static class A2ACli
{
    public static async Task<int> Main(string[] args)
    {
        // Create root command with options
        var rootCommand = new RootCommand("A2A CLI Client")
        {
            AgentOption,
            SessionOption,
            HistoryOption,
            UsePushNotificationsOption,
            PushNotificationReceiverOption
        };

        // Replace the problematic line with the following:
        rootCommand.SetHandler(RunCliAsync);

        // Build host with dependency injection
        //using var host = CreateHostBuilder(args).Build();

        // Run the command
        return await rootCommand.InvokeAsync(args);
    }

    public static async System.Threading.Tasks.Task RunCliAsync(InvocationContext context)
    {
        string agent = context.ParseResult.GetValueForOption<string>(AgentOption)!;
        string session = context.ParseResult.GetValueForOption<string>(SessionOption)!;
        bool history = context.ParseResult.GetValueForOption<bool>(HistoryOption);
        bool usePushNotifications = context.ParseResult.GetValueForOption<bool>(UsePushNotificationsOption);
        string pushNotificationReceiver = context.ParseResult.GetValueForOption<string>(PushNotificationReceiverOption)!;

        await RunCliAsync(agent, session, history, usePushNotifications, pushNotificationReceiver);
    }

    #region private
    private static readonly Option<string> AgentOption = new(
                "--agent",
                getDefaultValue: () => "http://localhost:10000",
                description: "Agent URL");
    private static readonly Option<string> SessionOption = new(
                "--session",
                getDefaultValue: () => "0",
                description: "Session ID (0 for new session)");
    private static readonly Option<bool> HistoryOption = new(
                "--history",
            getDefaultValue: () => false,
                description: "Show task history");
    private static readonly Option<bool> UsePushNotificationsOption = new(
                "--use-push-notifications",
                getDefaultValue: () => false,
                description: "Enable push notifications");
    private static readonly Option<string> PushNotificationReceiverOption = new(
                "--push-notification-receiver",
                getDefaultValue: () => "http://localhost:5000",
                description: "Push notification receiver URL");
    private static JsonSerializerOptions jsonOptions = new JsonSerializerOptions
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };


    private static async System.Threading.Tasks.Task RunCliAsync(
        string agentUrl,
        string session,
        bool history,
        bool usePushNotifications,
        string pushNotificationReceiver)
    {
        // Set up the logging
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });
        var logger = loggerFactory.CreateLogger("A2AClient");

        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri(agentUrl)
        };

        try
        {
            // Create the card resolver and get agentUrl card
            var cardResolver = new A2ACardResolver(httpClient);
            var card = await cardResolver.GetAgentCardAsync();

            Console.WriteLine("======= Agent Card ========");
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(card, jsonOptions));

            // Parse notification receiver URL
            var notificationReceiverUri = new Uri(pushNotificationReceiver!);
            string notificationReceiverHost = notificationReceiverUri.Host;
            int notificationReceiverPort = notificationReceiverUri.Port;

            // Create A2A client
            var client = new A2AClient(httpClient);

            // Create or use provided session ID
            string sessionId = session == "0" ? Guid.NewGuid().ToString("N") : session;

            // Main interaction loop
            bool continueLoop = true;
            bool streaming = false; // card.Capabilities.Streaming;
            while (continueLoop)
            {
                string taskId = Guid.NewGuid().ToString("N");

                continueLoop = await CompleteTaskAsync(
                    client,
                    streaming,
                    usePushNotifications,
                    notificationReceiverHost,
                    notificationReceiverPort,
                    taskId,
                    sessionId);

                if (history && continueLoop)
                {
                    Console.WriteLine("========= history ======== ");
                    var taskResponse = await client.GetTaskAsync(taskId);

                    // Display history in a way similar to the Python version
                    if (taskResponse.History != null)
                    {
                        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(
                            new { result = new { history = taskResponse.History } },
                            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                    }
                    taskResponse?.History?
                        .SelectMany(artifact => artifact.Parts.OfType<TextPart>())
                        .ToList()
                        .ForEach(textPart => Console.WriteLine(textPart.Text));
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while running the A2ACli");
            return;
        }
    }

    private static async Task<bool> CompleteTaskAsync(
        A2AClient client,
        bool streaming,
        bool usePushNotifications,
        string notificationReceiverHost,
        int notificationReceiverPort,
        string taskId,
        string sessionId)
    {
        // Get user prompt
        Console.Write("\nWhat do you want to send to the agentUrl? (:q or quit to exit): ");
        string? prompt = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(prompt))
        {
            Console.WriteLine("Request cannot be empty.");
            return true;
        }

        if (prompt == ":q" || prompt == "quit")
        {
            return false;
        }

        // Create message with text part
        var message = new Message
        {
            Role = MessageRole.User,
            Parts = new List<Part>
            {
                new TextPart
                {
                    Text = prompt
                }
            }
        };

        // Handle file attachment
        Console.Write("Select a file path to attach? (press enter to skip): ");
        string? filePath = Console.ReadLine();
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            try
            {
                byte[] fileBytes = await File.ReadAllBytesAsync(filePath);
                string fileContent = Convert.ToBase64String(fileBytes);
                string fileName = Path.GetFileName(filePath);

                message.Parts.Add(new FilePart
                {
                    File = new FileWithBytes
                    {
                        Name = fileName,
                        Bytes = fileContent
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading file: {ex.Message}");
            }
        }

        // Create payload for the task
        var payload = new MessageSendParams()
        {
            Configuration = new()
            {
                AcceptedOutputModes = new List<string> { "text" }
            },
                Message = message
            };

        // Add push notification configuration if enabled
        if (usePushNotifications)
        {
            payload.Configuration.PushNotification = new PushNotificationConfig
            {
                Url = $"http://{notificationReceiverHost}:{notificationReceiverPort}/notify",
                Authentication = new AuthenticationInfo
                {
                    Schemes = new List<string> { "bearer" }
                }
            };
        }

        AgentTask? agentTask = null;

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        // Process the task based on streaming capability
        Console.WriteLine($"Send task payload => {System.Text.Json.JsonSerializer.Serialize(payload, jsonOptions)}");
        if (streaming)
        {
            await foreach (var result in client.SendMessageStreamAsync(payload))
            {
                Console.WriteLine($"Stream event => {System.Text.Json.JsonSerializer.Serialize(result, jsonOptions)}");
            }

            var taskResult = await client.GetTaskAsync(taskId);
        }
        else
        {
            agentTask = await client.SendMessageAsync(payload) as AgentTask;
            Console.WriteLine($"\n{System.Text.Json.JsonSerializer.Serialize(agentTask, jsonOptions)}");
            agentTask?.Artifacts?
                .SelectMany(artifact => artifact.Parts.OfType<TextPart>())
                .ToList()
                .ForEach(textPart => Console.WriteLine(textPart.Text));
        }

        // If the task requires more input, continue the interaction
        if (agentTask?.Status.State == TaskState.InputRequired)
        {
            return await CompleteTaskAsync(
                client,
                streaming,
                usePushNotifications,
                notificationReceiverHost,
                notificationReceiverPort,
                taskId,
                sessionId);
        }

        // A2ATask is complete
        return true;
    }
    #endregion
}