namespace A2A;

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http.Headers;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

/// <summary>Client for communicating with an A2A agent via HTTP+JSON (REST) protocol binding.</summary>
public sealed class A2AHttpJsonClient : IA2AClient, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;

    /// <summary>Initializes a new instance of the <see cref="A2AHttpJsonClient"/> class.</summary>
    /// <param name="baseUrl">
    /// The base URL of the agent's HTTP+JSON interface
    /// (the <see cref="AgentInterface.Url"/> where <see cref="AgentInterface.ProtocolBinding"/> is <c>"HTTP+JSON"</c>).
    /// </param>
    /// <param name="httpClient">The HTTP client to use for requests.</param>
    public A2AHttpJsonClient(Uri baseUrl, HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(baseUrl);

        _baseUrl = baseUrl.ToString().TrimEnd('/');
        _httpClient = httpClient ?? A2AClient.s_sharedClient;
    }

    /// <inheritdoc />
    public async Task<SendMessageResponse> SendMessageAsync(SendMessageRequest request, CancellationToken cancellationToken = default)
    {
        return await PostJsonAsync<SendMessageRequest, SendMessageResponse>(
            "/message:send", request, "SendMessage", cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<StreamResponse> SendStreamingMessageAsync(SendMessageRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in PostStreamingAsync(
            "/message:stream", request, "SendStreamingMessage", cancellationToken).ConfigureAwait(false))
        {
            yield return item;
        }
    }

    /// <inheritdoc />
    public async Task<AgentTask> GetTaskAsync(GetTaskRequest request, CancellationToken cancellationToken = default)
    {
        var query = request.HistoryLength.HasValue
            ? $"?historyLength={request.HistoryLength.Value}"
            : string.Empty;

        return await GetJsonAsync<AgentTask>(
            $"/tasks/{Uri.EscapeDataString(request.Id)}{query}",
            "GetTask", cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ListTasksResponse> ListTasksAsync(ListTasksRequest request, CancellationToken cancellationToken = default)
    {
        var query = BuildQueryString(
            ("contextId", request.ContextId),
            ("status", request.Status?.ToString()),
            ("pageSize", request.PageSize?.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("pageToken", request.PageToken),
            ("historyLength", request.HistoryLength?.ToString(System.Globalization.CultureInfo.InvariantCulture)));

        return await GetJsonAsync<ListTasksResponse>(
            $"/tasks{query}", "ListTasks", cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<AgentTask> CancelTaskAsync(CancelTaskRequest request, CancellationToken cancellationToken = default)
    {
        return await PostEmptyAsync<AgentTask>(
            $"/tasks/{Uri.EscapeDataString(request.Id)}:cancel",
            "CancelTask", cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<StreamResponse> SubscribeToTaskAsync(SubscribeToTaskRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in PostEmptyStreamingAsync(
            $"/tasks/{Uri.EscapeDataString(request.Id)}:subscribe",
            "SubscribeToTask", cancellationToken).ConfigureAwait(false))
        {
            yield return item;
        }
    }

    /// <inheritdoc />
    public async Task<TaskPushNotificationConfig> CreateTaskPushNotificationConfigAsync(
        CreateTaskPushNotificationConfigRequest request, CancellationToken cancellationToken = default)
    {
        return await PostJsonAsync<PushNotificationConfig, TaskPushNotificationConfig>(
            $"/tasks/{Uri.EscapeDataString(request.TaskId)}/pushNotificationConfigs",
            request.Config, "CreateTaskPushNotificationConfig", cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<TaskPushNotificationConfig> GetTaskPushNotificationConfigAsync(
        GetTaskPushNotificationConfigRequest request, CancellationToken cancellationToken = default)
    {
        return await GetJsonAsync<TaskPushNotificationConfig>(
            $"/tasks/{Uri.EscapeDataString(request.TaskId)}/pushNotificationConfigs/{Uri.EscapeDataString(request.Id)}",
            "GetTaskPushNotificationConfig", cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ListTaskPushNotificationConfigResponse> ListTaskPushNotificationConfigAsync(
        ListTaskPushNotificationConfigRequest request, CancellationToken cancellationToken = default)
    {
        var query = BuildQueryString(
            ("pageSize", request.PageSize?.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("pageToken", request.PageToken));

        return await GetJsonAsync<ListTaskPushNotificationConfigResponse>(
            $"/tasks/{Uri.EscapeDataString(request.TaskId)}/pushNotificationConfigs{query}",
            "ListTaskPushNotificationConfig", cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteTaskPushNotificationConfigAsync(
        DeleteTaskPushNotificationConfigRequest request, CancellationToken cancellationToken = default)
    {
        await SendAsync(HttpMethod.Delete,
            $"/tasks/{Uri.EscapeDataString(request.TaskId)}/pushNotificationConfigs/{Uri.EscapeDataString(request.Id)}",
            "DeleteTaskPushNotificationConfig", cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<AgentCard> GetExtendedAgentCardAsync(GetExtendedAgentCardRequest request, CancellationToken cancellationToken = default)
    {
        return await GetJsonAsync<AgentCard>(
            "/extendedAgentCard", "GetExtendedAgentCard", cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    // ---- HTTP primitives ----

    [UnconditionalSuppressMessage("AOT", "IL2026:RequiresUnreferencedCode", Justification = "All types are registered in source-generated JsonContext.")]
    [UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode", Justification = "All types are registered in source-generated JsonContext.")]
    private async Task<TResult> GetJsonAsync<TResult>(string path, string operationName, CancellationToken cancellationToken)
    {
        using var activity = A2ADiagnostics.Source.StartActivity($"A2AClient/{operationName}", ActivityKind.Client);
        var stopwatch = Stopwatch.StartNew();

        var url = $"{_baseUrl}{path}";
        activity?.SetTag("http.method", "GET");
        activity?.SetTag("url.full", url);

        try
        {
            A2ADiagnostics.ClientRequestCount.Add(1);

            using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            await EnsureSuccessOrThrowA2AExceptionAsync(response, cancellationToken).ConfigureAwait(false);

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync<TResult>(stream, A2AJsonUtilities.DefaultOptions, cancellationToken).ConfigureAwait(false)
                ?? throw new A2AException("Failed to deserialize REST response.", A2AErrorCode.InternalError);
        }
        catch (A2AException) { throw; }
        catch (Exception ex)
        {
            A2ADiagnostics.ClientErrorCount.Add(1);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
        finally
        {
            A2ADiagnostics.ClientRequestDuration.Record(stopwatch.Elapsed.TotalMilliseconds);
        }
    }

    [UnconditionalSuppressMessage("AOT", "IL2026:RequiresUnreferencedCode", Justification = "All types are registered in source-generated JsonContext.")]
    [UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode", Justification = "All types are registered in source-generated JsonContext.")]
    private async Task<TResult> PostJsonAsync<TBody, TResult>(string path, TBody body, string operationName, CancellationToken cancellationToken)
    {
        using var activity = A2ADiagnostics.Source.StartActivity($"A2AClient/{operationName}", ActivityKind.Client);
        var stopwatch = Stopwatch.StartNew();

        var url = $"{_baseUrl}{path}";
        activity?.SetTag("http.method", "POST");
        activity?.SetTag("url.full", url);

        try
        {
            A2ADiagnostics.ClientRequestCount.Add(1);

            var json = JsonSerializer.Serialize(body, A2AJsonUtilities.DefaultOptions);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await _httpClient.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
            await EnsureSuccessOrThrowA2AExceptionAsync(response, cancellationToken).ConfigureAwait(false);

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync<TResult>(stream, A2AJsonUtilities.DefaultOptions, cancellationToken).ConfigureAwait(false)
                ?? throw new A2AException("Failed to deserialize REST response.", A2AErrorCode.InternalError);
        }
        catch (A2AException) { throw; }
        catch (Exception ex)
        {
            A2ADiagnostics.ClientErrorCount.Add(1);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
        finally
        {
            A2ADiagnostics.ClientRequestDuration.Record(stopwatch.Elapsed.TotalMilliseconds);
        }
    }

    [UnconditionalSuppressMessage("AOT", "IL2026:RequiresUnreferencedCode", Justification = "All types are registered in source-generated JsonContext.")]
    [UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode", Justification = "All types are registered in source-generated JsonContext.")]
    private async Task<TResult> PostEmptyAsync<TResult>(string path, string operationName, CancellationToken cancellationToken)
    {
        using var activity = A2ADiagnostics.Source.StartActivity($"A2AClient/{operationName}", ActivityKind.Client);
        var stopwatch = Stopwatch.StartNew();

        var url = $"{_baseUrl}{path}";
        activity?.SetTag("http.method", "POST");
        activity?.SetTag("url.full", url);

        try
        {
            A2ADiagnostics.ClientRequestCount.Add(1);

            using var response = await _httpClient.PostAsync(url, null, cancellationToken).ConfigureAwait(false);
            await EnsureSuccessOrThrowA2AExceptionAsync(response, cancellationToken).ConfigureAwait(false);

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync<TResult>(stream, A2AJsonUtilities.DefaultOptions, cancellationToken).ConfigureAwait(false)
                ?? throw new A2AException("Failed to deserialize REST response.", A2AErrorCode.InternalError);
        }
        catch (A2AException) { throw; }
        catch (Exception ex)
        {
            A2ADiagnostics.ClientErrorCount.Add(1);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
        finally
        {
            A2ADiagnostics.ClientRequestDuration.Record(stopwatch.Elapsed.TotalMilliseconds);
        }
    }

    private async Task SendAsync(HttpMethod method, string path, string operationName, CancellationToken cancellationToken)
    {
        using var activity = A2ADiagnostics.Source.StartActivity($"A2AClient/{operationName}", ActivityKind.Client);
        var stopwatch = Stopwatch.StartNew();

        var url = $"{_baseUrl}{path}";
        activity?.SetTag("http.method", method.Method);
        activity?.SetTag("url.full", url);

        try
        {
            A2ADiagnostics.ClientRequestCount.Add(1);

            using var requestMessage = new HttpRequestMessage(method, url);
            using var response = await _httpClient.SendAsync(requestMessage, cancellationToken).ConfigureAwait(false);
            await EnsureSuccessOrThrowA2AExceptionAsync(response, cancellationToken).ConfigureAwait(false);
        }
        catch (A2AException) { throw; }
        catch (Exception ex)
        {
            A2ADiagnostics.ClientErrorCount.Add(1);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
        finally
        {
            A2ADiagnostics.ClientRequestDuration.Record(stopwatch.Elapsed.TotalMilliseconds);
        }
    }

    [UnconditionalSuppressMessage("AOT", "IL2026:RequiresUnreferencedCode", Justification = "All types are registered in source-generated JsonContext.")]
    [UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode", Justification = "All types are registered in source-generated JsonContext.")]
    private async IAsyncEnumerable<StreamResponse> PostStreamingAsync<TBody>(
        string path, TBody body, string operationName,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var activity = A2ADiagnostics.Source.StartActivity($"A2AClient/{operationName}", ActivityKind.Client);
        A2ADiagnostics.ClientRequestCount.Add(1);
        int eventCount = 0;

        var url = $"{_baseUrl}{path}";
        activity?.SetTag("http.method", "POST");
        activity?.SetTag("url.full", url);

        HttpResponseMessage? response = null;
        Stream? stream = null;

        try
        {
            var json = JsonSerializer.Serialize(body, A2AJsonUtilities.DefaultOptions);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var requestMessage = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
            requestMessage.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

            response = await _httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            await EnsureSuccessOrThrowA2AExceptionAsync(response, cancellationToken).ConfigureAwait(false);

            stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (A2AException)
        {
            response?.Dispose();
            throw;
        }
        catch (Exception ex)
        {
            A2ADiagnostics.ClientErrorCount.Add(1);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            response?.Dispose();
            throw;
        }

        using (response)
        using (stream)
        {
            await foreach (var sseItem in SseParser.Create(stream).EnumerateAsync(cancellationToken).ConfigureAwait(false))
            {
                var result = JsonSerializer.Deserialize<StreamResponse>(sseItem.Data, A2AJsonUtilities.DefaultOptions)
                    ?? throw new A2AException("Failed to deserialize streaming REST response.", A2AErrorCode.InternalError);

                eventCount++;
                yield return result;
            }
        }

        A2ADiagnostics.ClientStreamEventCount.Record(eventCount);
    }

    [UnconditionalSuppressMessage("AOT", "IL2026:RequiresUnreferencedCode", Justification = "All types are registered in source-generated JsonContext.")]
    [UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode", Justification = "All types are registered in source-generated JsonContext.")]
    private async IAsyncEnumerable<StreamResponse> PostEmptyStreamingAsync(
        string path, string operationName,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var activity = A2ADiagnostics.Source.StartActivity($"A2AClient/{operationName}", ActivityKind.Client);
        A2ADiagnostics.ClientRequestCount.Add(1);
        int eventCount = 0;

        var url = $"{_baseUrl}{path}";
        activity?.SetTag("http.method", "POST");
        activity?.SetTag("url.full", url);

        HttpResponseMessage? response = null;
        Stream? stream = null;

        try
        {
            using var requestMessage = new HttpRequestMessage(HttpMethod.Post, url);
            requestMessage.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

            response = await _httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            await EnsureSuccessOrThrowA2AExceptionAsync(response, cancellationToken).ConfigureAwait(false);

            stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (A2AException)
        {
            response?.Dispose();
            throw;
        }
        catch (Exception ex)
        {
            A2ADiagnostics.ClientErrorCount.Add(1);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            response?.Dispose();
            throw;
        }

        using (response)
        using (stream)
        {
            await foreach (var sseItem in SseParser.Create(stream).EnumerateAsync(cancellationToken).ConfigureAwait(false))
            {
                var result = JsonSerializer.Deserialize<StreamResponse>(sseItem.Data, A2AJsonUtilities.DefaultOptions)
                    ?? throw new A2AException("Failed to deserialize streaming REST response.", A2AErrorCode.InternalError);

                eventCount++;
                yield return result;
            }
        }

        A2ADiagnostics.ClientStreamEventCount.Record(eventCount);
    }

    private static async Task EnsureSuccessOrThrowA2AExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string? detail = null;
        try
        {
            detail = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort body read
        }

        var errorCode = response.StatusCode switch
        {
            HttpStatusCode.NotFound => A2AErrorCode.TaskNotFound,
            HttpStatusCode.BadRequest => A2AErrorCode.InvalidRequest,
            HttpStatusCode.UnprocessableEntity => A2AErrorCode.ContentTypeNotSupported,
            _ => A2AErrorCode.InternalError,
        };

        var message = !string.IsNullOrEmpty(detail)
            ? $"HTTP {(int)response.StatusCode}: {detail}"
            : $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}";

        throw new A2AException(message, errorCode);
    }

    private static string BuildQueryString(params (string key, string? value)[] parameters)
    {
        var parts = new List<string>();
        foreach (var (key, value) in parameters)
        {
            if (!string.IsNullOrEmpty(value))
            {
                parts.Add($"{key}={Uri.EscapeDataString(value)}");
            }
        }

        return parts.Count > 0 ? "?" + string.Join("&", parts) : string.Empty;
    }
}
