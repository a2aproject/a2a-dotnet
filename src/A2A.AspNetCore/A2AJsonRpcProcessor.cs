using Microsoft.AspNetCore.Http;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace A2A.AspNetCore;

/// <summary>
/// Static processor class for handling A2A JSON-RPC requests in ASP.NET Core applications.
/// </summary>
public static class A2AJsonRpcProcessor
{
    private static readonly Lazy<(
        A2AOperationHandlerCatalog Handlers,
        A2AJsonRpcOperationBindings Bindings)> StandardDispatch =
        new(CreateStandardDispatch);

    /// <summary>
    /// Processes an A2A JSON-RPC request using request-specific state and typed operation bindings.
    /// </summary>
    /// <param name="scopeFactory">The request-scope factory.</param>
    /// <param name="handlers">The operation handlers.</param>
    /// <param name="bindings">The JSON-RPC operation bindings.</param>
    /// <param name="request">The HTTP request.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The deferred HTTP result.</returns>
    public static async Task<IResult> ProcessRequestAsync(
        A2ARequestScopeFactory scopeFactory,
        A2AOperationHandlerCatalog handlers,
        A2AJsonRpcOperationBindings bindings,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(handlers);
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(request);

        var preflightResult = CheckPreflight(request);
        if (preflightResult is not null)
        {
            return preflightResult;
        }

        using var activity = A2AAspNetCoreDiagnostics.Source.StartActivity(
            "HandleA2ARequest",
            ActivityKind.Server);

        JsonRpcRequest? rpcRequest = null;
        JsonRpcId? parsedRequestId = null;
        IA2AJsonRpcBoundOperation? boundOperation = null;
        A2ARequestScope? scope = null;

        try
        {
            using var document = await JsonDocument.ParseAsync(
                request.Body,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new A2AException(
                    "Invalid JSON-RPC request payload.",
                    A2AErrorCode.InvalidRequest);
            }

            parsedRequestId = TryReadRequestId(document.RootElement);
            rpcRequest = document.RootElement.Deserialize(
                (JsonTypeInfo<JsonRpcRequest>)A2AJsonUtilities.DefaultOptions
                    .GetTypeInfo(typeof(JsonRpcRequest)));
            if (rpcRequest is null)
            {
                throw new JsonException("The JSON-RPC request body is empty.");
            }

            activity?.SetTag("request.id", rpcRequest.Id.ToString());
            activity?.SetTag("request.method", rpcRequest.Method);

            if (rpcRequest.Params is null)
            {
                return new JsonRpcResponseResult(
                    JsonRpcResponse.InvalidParamsResponse(rpcRequest.Id));
            }

            if (!bindings.TryGetBinding(rpcRequest.Method, out var binding))
            {
                return new JsonRpcResponseResult(
                    JsonRpcResponse.MethodNotFoundResponse(rpcRequest.Id));
            }

            boundOperation = binding.Bind(rpcRequest.Params.Value);

            scope = await scopeFactory(
                request.HttpContext,
                cancellationToken).ConfigureAwait(false);
            ArgumentNullException.ThrowIfNull(scope);

            var result = await boundOperation.InvokeAsync(
                rpcRequest.Id,
                scope.Context,
                handlers,
                cancellationToken).ConfigureAwait(false);
            return WrapScope(result, ref scope);
        }
        catch (A2AException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            var errorId = GetErrorId(rpcRequest, parsedRequestId, ex);
            return WrapScope(
                new JsonRpcResponseResult(
                    JsonRpcResponse.CreateJsonRpcErrorResponse(errorId, ex)),
                ref scope);
        }
        catch (JsonException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddEvent(new ActivityEvent(
                "json.parse.error",
                tags: new ActivityTagsCollection
                {
                    { "exception.type", ex.GetType().FullName },
                    { "exception.message", ex.Message },
                }));
            var errorId = GetErrorId(rpcRequest, parsedRequestId);
            return WrapScope(
                new JsonRpcResponseResult(
                    JsonRpcResponse.ParseErrorResponse(errorId)),
                ref scope);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            var errorId = GetErrorId(rpcRequest, parsedRequestId);
            var response = boundOperation?.CreateErrorResponse(
                errorId,
                ex,
                "An internal error occurred.")
                ?? JsonRpcResponse.InternalErrorResponse(
                    errorId,
                    "An internal error occurred.");
            return WrapScope(
                new JsonRpcResponseResult(response),
                ref scope);
        }
        finally
        {
            if (scope is not null)
            {
                await scope.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    internal static IResult? CheckPreflight(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var version = request.Headers["A2A-Version"].FirstOrDefault();
        if (!string.IsNullOrEmpty(version)
            && version != "1.0"
            && version != "0.3")
        {
            return new JsonRpcResponseResult(
                JsonRpcResponse.CreateJsonRpcErrorResponse(
                    new JsonRpcId((string?)null),
                    new A2AException(
                        $"Protocol version '{version}' is not supported. Supported versions: 0.3, 1.0",
                        A2AErrorCode.VersionNotSupported)));
        }

        return null;
    }

    internal static Task<IResult> ProcessRequestAsync(
        IA2ARequestHandler requestHandler,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requestHandler);
        var dispatch = StandardDispatch.Value;
        A2ARequestScopeFactory scopeFactory = (_, _) => ValueTask.FromResult(
            new A2ARequestScope(
                new A2AOperationContext(requestHandler)));
        return ProcessRequestAsync(
            scopeFactory,
            dispatch.Handlers,
            dispatch.Bindings,
            request,
            cancellationToken);
    }

    internal static (
        A2AOperationHandlerCatalog Handlers,
        A2AJsonRpcOperationBindings Bindings) CreateStandardDispatch()
    {
        var operationBuilder = new A2AOperationCatalogBuilder();
        var standard = operationBuilder.AddStandardA2AOperations();
        var operations = operationBuilder.Build();
        var handlers = new A2AOperationHandlerCatalogBuilder()
            .AddStandardA2AHandlers(standard)
            .Build(operations);
        var bindings = new A2AJsonRpcOperationBindingBuilder()
            .AddStandardA2AJsonRpcBindings(standard)
            .Build(operations);
        return (handlers, bindings);
    }

    private static JsonRpcId? TryReadRequestId(JsonElement request)
    {
        if (!request.TryGetProperty("id", out var id))
        {
            return null;
        }

        return id.ValueKind switch
        {
            JsonValueKind.String => new JsonRpcId(id.GetString()),
            JsonValueKind.Number when id.TryGetInt64(out var value) =>
                new JsonRpcId(value),
            JsonValueKind.Null => new JsonRpcId((string?)null),
            _ => null,
        };
    }

    private static JsonRpcId GetErrorId(
        JsonRpcRequest? rpcRequest,
        JsonRpcId? parsedRequestId,
        A2AException? exception = null)
    {
        if (rpcRequest is not null)
        {
            return rpcRequest.Id;
        }

        if (parsedRequestId.HasValue)
        {
            return parsedRequestId.Value;
        }

        return new JsonRpcId(exception?.GetRequestId());
    }

    private static IResult WrapScope(
        IResult result,
        ref A2ARequestScope? scope)
    {
        if (scope is null)
        {
            return result;
        }

        var scopedResult = new A2ARequestScopeResult(result, scope);
        scope = null;
        return scopedResult;
    }
}
