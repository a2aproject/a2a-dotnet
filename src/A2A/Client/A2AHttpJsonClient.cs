namespace A2A;

using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;

/// <summary>Client for communicating with an A2A agent via HTTP+JSON (REST) protocol binding.</summary>
public sealed class A2AHttpJsonClient : IA2AClient, IDisposable
{
    private static readonly Dictionary<string, A2AErrorCode>
        s_reasonToErrorCode = new(StringComparer.OrdinalIgnoreCase)
        {
            ["TASK_NOT_FOUND"] = A2AErrorCode.TaskNotFound,
            ["TASK_NOT_CANCELABLE"] = A2AErrorCode.TaskNotCancelable,
            ["PUSH_NOTIFICATION_NOT_SUPPORTED"] =
                A2AErrorCode.PushNotificationNotSupported,
            ["UNSUPPORTED_OPERATION"] = A2AErrorCode.UnsupportedOperation,
            ["CONTENT_TYPE_NOT_SUPPORTED"] =
                A2AErrorCode.ContentTypeNotSupported,
            ["INVALID_AGENT_RESPONSE"] =
                A2AErrorCode.InvalidAgentResponse,
            ["EXTENDED_AGENT_CARD_NOT_CONFIGURED"] =
                A2AErrorCode.ExtendedAgentCardNotConfigured,
            ["EXTENSION_SUPPORT_REQUIRED"] =
                A2AErrorCode.ExtensionSupportRequired,
            ["VERSION_NOT_SUPPORTED"] = A2AErrorCode.VersionNotSupported,
        };

    private readonly HttpClient _httpClient;
    private readonly Uri _endpoint;
    private readonly A2AClientOperationBindings _operationBindings;
    private readonly A2AStandardOperations _standardOperations;

    /// <summary>Initializes a new instance of the <see cref="A2AHttpJsonClient"/> class.</summary>
    /// <param name="baseUrl">The HTTP+JSON endpoint URL.</param>
    /// <param name="httpClient">The HTTP client to use for requests.</param>
    public A2AHttpJsonClient(Uri baseUrl, HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(baseUrl);

        _endpoint = baseUrl;
        _operationBindings = A2AStandardClientBindings.Default.Bindings;
        _standardOperations = A2AStandardClientBindings.Default.Operations;
        _httpClient = httpClient ?? A2AClient.s_sharedClient;
    }

    /// <summary>Initializes a client with custom operation bindings.</summary>
    /// <param name="baseUrl">The HTTP+JSON endpoint URL.</param>
    /// <param name="operationBindings">The client operation bindings.</param>
    /// <param name="httpClient">The HTTP client to use for requests.</param>
    public A2AHttpJsonClient(
        Uri baseUrl,
        A2AClientOperationBindings operationBindings,
        HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(baseUrl);
        ArgumentNullException.ThrowIfNull(operationBindings);
        ArgumentNullException.ThrowIfNull(httpClient);

        _endpoint = baseUrl;
        _operationBindings =
            A2AStandardClientBindings.IncludeDefaults(operationBindings);
        _standardOperations = _operationBindings.StandardOperations
            ?? throw new InvalidOperationException(
                "Standard A2A client operations are not configured.");
        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public async Task<TResult> InvokeAsync<TRequest, TResult>(
        A2AOperation<TRequest, TResult> operation,
        TRequest request,
        CancellationToken cancellationToken = default)
    {
        var binding = _operationBindings.GetHttp(operation);
        binding.Validate(request);
        using var requestMessage = await binding.RequestMapper(
            _endpoint,
            request,
            cancellationToken).ConfigureAwait(false);
        requestMessage.Headers.TryAddWithoutValidation(
            "A2A-Version",
            "1.0");

        using var activity = A2ADiagnostics.Source.StartActivity(
            $"A2AClient/{binding.OperationName}",
            ActivityKind.Client);
        var stopwatch = Stopwatch.StartNew();
        activity?.SetTag("http.method", requestMessage.Method.Method);
        activity?.SetTag(
            "url.full",
            requestMessage.RequestUri?.ToString());

        try
        {
            A2ADiagnostics.ClientRequestCount.Add(1);
            using var response = await _httpClient.SendAsync(
                requestMessage,
                cancellationToken).ConfigureAwait(false);
            await EnsureSuccessOrThrowAsync(
                response,
                binding,
                cancellationToken).ConfigureAwait(false);

            if (typeof(TResult) == typeof(A2AEmptyResult))
            {
                return (TResult)(object)A2AEmptyResult.Instance;
            }

            using var stream = await response.Content.ReadAsStreamAsync(
                cancellationToken).ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync(
                stream,
                binding.ResponseTypeInfo,
                cancellationToken).ConfigureAwait(false)
                ?? throw new A2AException(
                    "Failed to deserialize REST response.",
                    A2AErrorCode.InternalError);
        }
        catch (A2AException)
        {
            throw;
        }
        catch (A2AOperationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            A2ADiagnostics.ClientErrorCount.Add(1);
            activity?.SetStatus(
                ActivityStatusCode.Error,
                exception.Message);
            throw;
        }
        finally
        {
            A2ADiagnostics.ClientRequestDuration.Record(
                stopwatch.Elapsed.TotalMilliseconds);
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<TEvent> InvokeStreamingAsync<TRequest, TEvent>(
        A2AStreamingOperation<TRequest, TEvent> operation,
        TRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var binding = _operationBindings.GetHttpStreaming(operation);
        binding.Validate(request);
        using var requestMessage = await binding.RequestMapper(
            _endpoint,
            request,
            cancellationToken).ConfigureAwait(false);
        requestMessage.Headers.TryAddWithoutValidation(
            "A2A-Version",
            "1.0");
        requestMessage.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var activity = A2ADiagnostics.Source.StartActivity(
            $"A2AClient/{binding.OperationName}",
            ActivityKind.Client);
        A2ADiagnostics.ClientRequestCount.Add(1);
        var eventCount = 0;
        activity?.SetTag("http.method", requestMessage.Method.Method);
        activity?.SetTag(
            "url.full",
            requestMessage.RequestUri?.ToString());

        HttpResponseMessage? response = null;
        Stream? stream = null;
        try
        {
            response = await _httpClient.SendAsync(
                requestMessage,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            await EnsureSuccessOrThrowAsync(
                response,
                binding,
                cancellationToken).ConfigureAwait(false);
            stream = await response.Content.ReadAsStreamAsync(
                cancellationToken).ConfigureAwait(false);
        }
        catch (A2AException)
        {
            response?.Dispose();
            throw;
        }
        catch (A2AOperationException)
        {
            response?.Dispose();
            throw;
        }
        catch (Exception exception)
        {
            A2ADiagnostics.ClientErrorCount.Add(1);
            activity?.SetStatus(
                ActivityStatusCode.Error,
                exception.Message);
            response?.Dispose();
            throw;
        }

        using (response)
        using (stream)
        {
            await foreach (var sseItem in SseParser.Create(stream)
                .EnumerateAsync(cancellationToken).ConfigureAwait(false))
            {
                var result = JsonSerializer.Deserialize(
                    sseItem.Data,
                    binding.ResponseTypeInfo)
                    ?? throw new A2AException(
                        "Failed to deserialize streaming REST response.",
                        A2AErrorCode.InternalError);

                eventCount++;
                yield return result;
            }
        }

        A2ADiagnostics.ClientStreamEventCount.Record(eventCount);
    }

    /// <inheritdoc />
    public Task<SendMessageResponse> SendMessageAsync(
        SendMessageRequest request,
        CancellationToken cancellationToken = default) =>
        InvokeAsync(
            _standardOperations.SendMessage,
            request,
            cancellationToken);

    /// <inheritdoc />
    public IAsyncEnumerable<StreamResponse> SendStreamingMessageAsync(
        SendMessageRequest request,
        CancellationToken cancellationToken = default) =>
        InvokeStreamingAsync(
            _standardOperations.SendStreamingMessage,
            request,
            cancellationToken);

    /// <inheritdoc />
    public Task<AgentTask> GetTaskAsync(
        GetTaskRequest request,
        CancellationToken cancellationToken = default) =>
        InvokeAsync(
            _standardOperations.GetTask,
            request,
            cancellationToken);

    /// <inheritdoc />
    public Task<ListTasksResponse> ListTasksAsync(
        ListTasksRequest request,
        CancellationToken cancellationToken = default) =>
        InvokeAsync(
            _standardOperations.ListTasks,
            request,
            cancellationToken);

    /// <inheritdoc />
    public Task<AgentTask> CancelTaskAsync(
        CancelTaskRequest request,
        CancellationToken cancellationToken = default) =>
        InvokeAsync(
            _standardOperations.CancelTask,
            request,
            cancellationToken);

    /// <inheritdoc />
    public IAsyncEnumerable<StreamResponse> SubscribeToTaskAsync(
        SubscribeToTaskRequest request,
        CancellationToken cancellationToken = default) =>
        InvokeStreamingAsync(
            _standardOperations.SubscribeToTask,
            request,
            cancellationToken);

    /// <inheritdoc />
    public Task<TaskPushNotificationConfig>
        CreateTaskPushNotificationConfigAsync(
            TaskPushNotificationConfig config,
            CancellationToken cancellationToken = default) =>
        InvokeAsync(
            _standardOperations.CreateTaskPushNotificationConfig,
            config,
            cancellationToken);

    /// <inheritdoc />
    public Task<TaskPushNotificationConfig>
        GetTaskPushNotificationConfigAsync(
            GetTaskPushNotificationConfigRequest request,
            CancellationToken cancellationToken = default) =>
        InvokeAsync(
            _standardOperations.GetTaskPushNotificationConfig,
            request,
            cancellationToken);

    /// <inheritdoc />
    public Task<ListTaskPushNotificationConfigsResponse>
        ListTaskPushNotificationConfigsAsync(
            ListTaskPushNotificationConfigsRequest request,
            CancellationToken cancellationToken = default) =>
        InvokeAsync(
            _standardOperations.ListTaskPushNotificationConfigs,
            request,
            cancellationToken);

    /// <inheritdoc />
    public async Task DeleteTaskPushNotificationConfigAsync(
        DeleteTaskPushNotificationConfigRequest request,
        CancellationToken cancellationToken = default)
    {
        _ = await InvokeAsync(
            _standardOperations.DeleteTaskPushNotificationConfig,
            request,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<AgentCard> GetExtendedAgentCardAsync(
        GetExtendedAgentCardRequest request,
        CancellationToken cancellationToken = default) =>
        InvokeAsync(
            _standardOperations.GetExtendedAgentCard,
            request,
            cancellationToken);

    /// <inheritdoc />
    public void Dispose()
    {
    }

    private static async Task EnsureSuccessOrThrowAsync<TRequest, TResponse>(
        HttpResponseMessage response,
        A2AHttpClientOperationBinding<TRequest, TResponse> binding,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string? body = null;
        try
        {
            body = await response.Content.ReadAsStringAsync(
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception readException)
        {
            Activity.Current?.AddException(readException);
        }

        var contentType = response.Content.Headers.ContentType?.MediaType;
        if (body is not null
            && string.Equals(
                contentType,
                "application/json",
                StringComparison.OrdinalIgnoreCase)
            && TryCreateOperationException(
                response.StatusCode,
                body,
                binding,
                out var operationException))
        {
            throw operationException;
        }

        string? detail = null;
        A2AErrorCode? reasonErrorCode = null;
        try
        {
            if (string.Equals(
                    contentType,
                    "application/json",
                    StringComparison.OrdinalIgnoreCase))
            {
                var errorResponse = body is null
                    ? null
                    : JsonSerializer.Deserialize(
                        body,
                        (System.Text.Json.Serialization.Metadata.JsonTypeInfo<
                            A2AErrorResponse>)A2AJsonUtilities.DefaultOptions
                            .GetTypeInfo(typeof(A2AErrorResponse)));
                if (errorResponse?.Error is { } error)
                {
                    detail = error.Message;
                    var errorInfo = error.Details?.FirstOrDefault(
                        static detail => string.Equals(
                            detail.Domain,
                            "a2a-protocol.org",
                            StringComparison.OrdinalIgnoreCase));
                    if (errorInfo?.Reason is not null
                        && s_reasonToErrorCode.TryGetValue(
                            errorInfo.Reason,
                            out var mapped))
                    {
                        reasonErrorCode = mapped;
                    }
                }
            }
            else
            {
                detail = body;
            }
        }
        catch (Exception parseException)
        {
            Activity.Current?.AddException(parseException);
        }

        var errorCode = reasonErrorCode ?? response.StatusCode switch
        {
            HttpStatusCode.NotFound => A2AErrorCode.TaskNotFound,
            HttpStatusCode.BadRequest => A2AErrorCode.InvalidRequest,
            HttpStatusCode.Conflict => A2AErrorCode.TaskNotCancelable,
            HttpStatusCode.UnsupportedMediaType =>
                A2AErrorCode.ContentTypeNotSupported,
            HttpStatusCode.BadGateway => A2AErrorCode.InvalidAgentResponse,
            _ => A2AErrorCode.InternalError,
        };

        var message = !string.IsNullOrEmpty(detail)
            ? $"HTTP {(int)response.StatusCode}: {detail}"
            : $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}";

        throw new A2AException(message, errorCode);
    }

    private static bool TryCreateOperationException<TRequest, TResponse>(
        HttpStatusCode statusCode,
        string body,
        A2AHttpClientOperationBinding<TRequest, TResponse> binding,
        out A2AOperationException exception)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty(
                    "error",
                    out var error)
                || error.ValueKind != JsonValueKind.Object
                || !error.TryGetProperty("code", out var codeElement)
                || codeElement.ValueKind != JsonValueKind.Number
                || !codeElement.TryGetInt32(out var code)
                || code != (int)statusCode
                || !error.TryGetProperty("status", out var statusElement)
                || statusElement.ValueKind != JsonValueKind.String
                || statusElement.GetString() is null
                || !error.TryGetProperty("message", out var messageElement)
                || messageElement.ValueKind != JsonValueKind.String
                || messageElement.GetString() is not { } message
                || !error.TryGetProperty("details", out var details)
                || details.ValueKind != JsonValueKind.Array)
            {
                exception = null!;
                return false;
            }

            foreach (var detail in details.EnumerateArray())
            {
                if (detail.ValueKind != JsonValueKind.Object
                    || !detail.TryGetProperty("@type", out var typeElement)
                    || typeElement.GetString() is not { } errorId)
                {
                    continue;
                }

                if (binding.TryCreateOperationException(
                    (int)statusCode,
                    errorId,
                    message,
                    detail,
                    out exception))
                {
                    return true;
                }
            }
        }
        catch (Exception parseException)
        {
            Activity.Current?.AddException(parseException);
        }

        exception = null!;
        return false;
    }
}
