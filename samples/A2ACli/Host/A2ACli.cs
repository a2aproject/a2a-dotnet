using Microsoft.Extensions.Logging;
using System.CommandLine;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace A2A;

/// <summary>
/// This is a port of the A2A cli from the Google project. Adding this to help ensure we have feature parity.
/// </summary>
public static class A2ACli
{
    public static Task<int> Main(string[] args)
    {
        // Create root command with options
        var rootCommand = new RootCommand("A2A CLI Client")
        {
            s_agentOption,
            s_sessionOption,
            s_historyOption,
            s_usePushNotificationsOption,
            s_pushNotificationReceiverOption
        };

        // Replace the problematic line with the following:
        rootCommand.SetAction(RunCliAsync);

        // Build host with dependency injection
        //using var host = CreateHostBuilder(args).Build();

        // Run the command
        return rootCommand.Parse(args).InvokeAsync();
    }

    public static Task RunCliAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        string agent = parseResult.GetValue(s_agentOption)!;
        string session = parseResult.GetValue(s_sessionOption)!;
        bool history = parseResult.GetValue(s_historyOption);
        bool usePushNotifications = parseResult.GetValue(s_usePushNotificationsOption);
        string pushNotificationReceiver = parseResult.GetValue(s_pushNotificationReceiverOption)!;

        return RunCliAsync(agent, session, history, usePushNotifications, pushNotificationReceiver, cancellationToken);
    }

    #region private
    private static readonly Option<string> s_agentOption = new("--agent")
    {
        DefaultValueFactory = _ => "http://localhost:10000",
        Description = "Agent URL"
    };
    private static readonly Option<string> s_sessionOption = new("--session")
    {
        DefaultValueFactory = _ => "0",
        Description = "Session ID (0 for new session)"
    };
    private static readonly Option<bool> s_historyOption = new("--history")
    {
        Description = "Show task history"
    };
    private static readonly Option<bool> s_usePushNotificationsOption = new("--use-push-notifications")
    {
        Description = "Enable push notifications"
    };
    private static readonly Option<string> s_pushNotificationReceiverOption = new("--push-notification-receiver")
    {
        DefaultValueFactory = _ => "http://localhost:5000",
        Description = "Push notification receiver URL"
    };

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly JsonSerializerOptions s_indentOptions = new() { WriteIndented = true };

    private static async Task RunCliAsync(
        string agentUrl,
        string session,
        bool history,
        bool usePushNotifications,
        string pushNotificationReceiver,
        CancellationToken cancellationToken)
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
            var card = await cardResolver.GetAgentCardAsync(cancellationToken);

            Console.WriteLine("======= Agent Card ========");
            Console.WriteLine(JsonSerializer.Serialize(card, s_jsonOptions));

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
                    sessionId,
                    cancellationToken);

                if (history && continueLoop)
                {
                    Console.WriteLine("========= history ======== ");
                    var taskResponse = await client.GetTaskAsync(taskId, cancellationToken);

                    // Display history in a way similar to the Python version
                    if (taskResponse.History != null)
                    {
                        Console.WriteLine(JsonSerializer.Serialize(
                            new { result = new { history = taskResponse.History } },
                            s_indentOptions));
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
        string sessionId,
        CancellationToken cancellationToken)
    {
        // Get user prompt
        Console.Write("\nWhat do you want to send to the agentUrl? (:q or quit to exit): ");
        string? prompt = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(prompt))
        {
            Console.WriteLine("Request cannot be empty.");
            return true;
        }

        if (prompt is ":q" or "quit")
        {
            return false;
        }

        // Create message with text part
        var message = new Message
        {
            Role = MessageRole.User,
            Parts =
            [
                new TextPart
                {
                    Text = prompt
                }
            ]
        };

        // Handle file attachment
        Console.Write("Select a file path to attach? (press enter to skip): ");
        string? filePath = Console.ReadLine();
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            try
            {
                byte[] fileBytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
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
                AcceptedOutputModes = ["text"]
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
                    Schemes = ["bearer"]
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
        Console.WriteLine($"Send task payload => {JsonSerializer.Serialize(payload, jsonOptions)}");
        if (streaming)
        {
            await foreach (var result in client.SendMessageStreamAsync(payload, cancellationToken))
            {
                Console.WriteLine($"Stream event => {JsonSerializer.Serialize(result, jsonOptions)}");
            }

            var taskResult = await client.GetTaskAsync(taskId, cancellationToken);
        }
        else
        {
            agentTask = await client.SendMessageAsync(payload, cancellationToken) as AgentTask;
            Console.WriteLine($"\n{JsonSerializer.Serialize(agentTask, jsonOptions)}");
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
                sessionId,
                cancellationToken);
        }

        // A2ATask is complete
        return true;
    }
    #endregion
}