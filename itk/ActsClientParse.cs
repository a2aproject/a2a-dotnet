// The tck-client-parse behaviour: ACTS §10 client tests.
//
// Every other ACTS step drives the SUT as a server — send bytes, assert on
// what comes back. A client test inverts that: it supplies a canonical wire
// payload and asks whether this SDK's *client* parses it correctly, which no
// A2A operation can ask of a server. §10 defines the file format and says
// nothing about the mechanism, so without a convention like this one the
// runner cannot reach the client at all and skips the CLIENT-* tests.
//
// The runner sends an ordinary send_message naming this behaviour with
// {operation, wire_payload} in a data part; the agent builds a real client
// whose HTTP transport returns that payload verbatim, performs the operation,
// and hands back whatever its own client produced.
//
// A fake transport rather than a bare deserialize: decoding the payload
// straight into A2A types would be a fraction of the code and would prove much
// less, skipping the JSON-RPC envelope, the error mapping and the response
// plumbing that are most of what a client test is about. CLIENT-PARSE-004 makes
// that concrete — it feeds an error envelope and expects {error: {code, message}}.

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace A2A.Itk;

/// <summary>Runs a canonical wire payload through this SDK's own client.</summary>
public static class ActsClientParse
{
    /// <summary>The behaviour prefix the runner delivers §10 steps through.</summary>
    public const string Behavior = "tck-client-parse";

    /// <summary>
    /// Nothing dials this — the fake transport answers before a socket is opened — but the
    /// client needs a syntactically valid base.
    /// </summary>
    private const string BaseUrl = "http://acts-client-parse.invalid";

    private const string CardPath = "/.well-known/agent-card.json";

    /// <summary>Serves one <c>client_response</c> step.</summary>
    public static async Task RunAsync(
        RequestContext context,
        TaskUpdater updater,
        IHttpClientFactory httpClientFactory,
        CancellationToken cancellationToken)
    {
        if (!TryReadRequest(context.Message, out var operation, out var payload))
        {
            await updater.FailAsync(
                ActsBehaviors.AgentText($"{Behavior} needs {{operation, wire_payload}}"),
                cancellationToken: cancellationToken);
            return;
        }

        var parsed = await ParseAsync(operation, payload, cancellationToken);

        await updater.AddArtifactAsync(
            [Part.FromData(JsonSerializer.SerializeToElement(parsed))],
            name: Behavior,
            cancellationToken: cancellationToken);
        await updater.CompleteAsync(
            ActsBehaviors.AgentText($"{operation} parsed"), cancellationToken: cancellationToken);
    }

    private static bool TryReadRequest(Message message, out string operation, out string payload)
    {
        operation = string.Empty;
        payload = string.Empty;

        foreach (var part in message.Parts)
        {
            if (part.Data is not { } data || data.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (!data.TryGetProperty("operation", out var operationValue)
                || operationValue.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            operation = operationValue.GetString()!;
            payload = data.TryGetProperty("wire_payload", out var wire)
                ? wire.GetRawText()
                : "null";
            return true;
        }

        return false;
    }

    private static async Task<JsonNode> ParseAsync(
        string operation, string payload, CancellationToken cancellationToken)
    {
        try
        {
            var parsed = await RunOperationAsync(operation, payload, cancellationToken);
            // Only send_message keeps its envelope: §4.2 makes the task/message
            // discriminator part of that operation's assertion root. get_task and the
            // card operations are asserted on their own fields.
            return WireMap(parsed, enveloped: operation == "send_message");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ParseError(ex, payload);
        }
    }

    private static async Task<object> RunOperationAsync(
        string operation, string payload, CancellationToken cancellationToken)
    {
        switch (operation)
        {
            case "get_agent_card":
                return await ResolveCardAsync(payload, CardPath, cancellationToken);

            case "get_extended_agent_card":
                // The corpus writes this as a bare card, matching the wire: its own payload
                // names REST, where the extended card is a plain GET. Accept an envelope
                // too, since a JSON-RPC binding does wrap it.
                if (!IsEnveloped(payload))
                {
                    return await ResolveCardAsync(payload, ActsAuth.ExtendedCardPath, cancellationToken);
                }

                return await Client(payload).GetExtendedAgentCardAsync(
                    new GetExtendedAgentCardRequest(), cancellationToken);

            case "send_message":
                return await Client(payload).SendMessageAsync(
                    new SendMessageRequest
                    {
                        Message = new Message
                        {
                            Role = Role.User,
                            MessageId = Guid.NewGuid().ToString("N"),
                            Parts = [Part.FromText("acts")],
                        },
                    },
                    cancellationToken);

            case "get_task":
                return await Client(payload).GetTaskAsync(
                    new GetTaskRequest { Id = "acts" }, cancellationToken);
        }

        throw new NotSupportedException($"unsupported client operation \"{operation}\"");
    }

    /// <summary>
    /// Builds a real client bound to the fake transport.
    /// </summary>
    /// <remarks>
    /// JSON-RPC specifically: the corpus's envelopes are JSON-RPC, and a card is not
    /// consulted, so the client cannot short-circuit on its own capability flags the way it
    /// would if one had been loaded.
    /// </remarks>
    private static A2AClient Client(string payload) =>
        new(new Uri(BaseUrl), FixedHttpClient(payload));

    /// <summary>
    /// Runs a bare card payload through the SDK's own card handling.
    /// </summary>
    /// <remarks>
    /// Both card operations land here when the payload is a bare card, which is how the
    /// corpus writes them — correctly, since a card is fetched over plain HTTP on every
    /// binding, so there is no envelope to unwrap.
    /// </remarks>
    private static Task<AgentCard> ResolveCardAsync(
        string payload, string path, CancellationToken cancellationToken)
        => new A2ACardResolver(new Uri(BaseUrl), FixedHttpClient(payload), path)
            .GetAgentCardAsync(cancellationToken);

    private static HttpClient FixedHttpClient(string payload) => new(new FixedHandler(payload));

    /// <summary>
    /// Answers every request with the payload under test.
    /// </summary>
    /// <remarks>
    /// The status is always 200: the JSON-RPC transport checks the HTTP status before it
    /// looks at the body, so an error envelope served at 4xx is discarded as "unexpected
    /// HTTP status" and never parsed.
    /// </remarks>
    private sealed class FixedHandler(string payload) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = payload;
            if (request.Content is not null)
            {
                var sent = await request.Content.ReadAsStringAsync(cancellationToken);
                body = EchoId(payload, sent) ?? payload;
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
                RequestMessage = request,
            };
        }
    }

    /// <summary>
    /// Rewrites the response's JSON-RPC id to the request's, which is what a real server
    /// does.
    /// </summary>
    /// <remarks>
    /// The corpus's canned payloads carry a fixed id that cannot match one the client
    /// invented at call time, so a client validating the correlation rejects the payload
    /// before parsing any of it — leaving the test measuring correlation rather than parsing.
    /// </remarks>
    private static string? EchoId(string payload, string sent)
    {
        try
        {
            if (JsonNode.Parse(payload) is not JsonObject envelope || !IsEnveloped(envelope))
            {
                return null;
            }

            if (JsonNode.Parse(sent) is not JsonObject request
                || !request.TryGetPropertyValue("id", out var id))
            {
                return null;
            }

            envelope["id"] = id?.DeepClone();
            return envelope.ToJsonString(JsonSerializerOptions.Default);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsEnveloped(string payload)
    {
        try
        {
            return JsonNode.Parse(payload) is JsonObject envelope && IsEnveloped(envelope);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsEnveloped(JsonObject payload)
        => payload.ContainsKey("jsonrpc") || payload.ContainsKey("result") || payload.ContainsKey("error");

    /// <summary>
    /// Renders what the client produced as A2A wire JSON, wrapping it in the StreamResponse
    /// envelope when the operation's assertion root is the discriminated event rather than
    /// the object itself.
    /// </summary>
    private static JsonNode WireMap(object parsed, bool enveloped)
    {
        object subject = (enveloped, parsed) switch
        {
            (true, SendMessageResponse response) => new StreamResponse
            {
                Task = response.Task,
                Message = response.Message,
            },
            _ => parsed,
        };

        return JsonSerializer.SerializeToNode(subject, subject.GetType(), A2AJsonUtilities.DefaultOptions)
            ?? new JsonObject();
    }

    /// <summary>
    /// Renders a client-raised error the way expect_parsed addresses it.
    /// </summary>
    /// <remarks>
    /// The envelope's own error is preferred when the payload carried one: the assertion is
    /// about the client having surfaced <em>that</em> error, and inventing a code here would
    /// pass the test without the client having done anything.
    /// </remarks>
    private static JsonObject ParseError(Exception error, string payload)
    {
        try
        {
            if (JsonNode.Parse(payload) is JsonObject envelope
                && envelope.TryGetPropertyValue("error", out var wire)
                && wire is JsonObject)
            {
                return new JsonObject
                {
                    ["error"] = wire.DeepClone(),
                    ["raised"] = error.Message,
                };
            }
        }
        catch (JsonException)
        {
        }

        var body = new JsonObject { ["message"] = error.Message };
        if (error is A2AException typed)
        {
            body["code"] = (int)typed.ErrorCode;
        }

        return new JsonObject { ["error"] = body };
    }
}
