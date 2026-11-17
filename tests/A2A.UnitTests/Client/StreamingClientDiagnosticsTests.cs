namespace A2A.UnitTests.Client;

using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Text;
using System.Text.Json;

public sealed class StreamingClientDiagnosticsTests
{
    [Theory]
    [InlineData(false, "protocol")]
    [InlineData(true, "protocol")]
    [InlineData(false, "invalid-json")]
    [InlineData(true, "invalid-json")]
    [InlineData(false, "network")]
    [InlineData(true, "network")]
    [InlineData(false, "network-initial")]
    [InlineData(true, "network-initial")]
    [InlineData(true, "id-mismatch")]
    [InlineData(false, "success")]
    [InlineData(true, "success")]
    public async Task StreamAfterFirstEvent_RecordsFailureExactlyOnce(bool jsonRpc, string scenario)
    {
        using var parent = new Activity("stream-diagnostics-test").SetIdFormat(ActivityIdFormat.W3C).Start();
        var traceId = parent.TraceId;
        Activity? completed = null;
        using var activities = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "A2A",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => { if (activity.TraceId == traceId) completed = activity; },
        };
        ActivitySource.AddActivityListener(activities);
        long errors = 0;
        using var meters = new MeterListener();
        meters.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == "A2A" && instrument.Name == "a2a.client.error.count")
                listener.EnableMeasurementEvents(instrument);
        };
        meters.SetMeasurementEventCallback<long>((_, value, _, _) =>
        {
            if (Activity.Current?.TraceId == traceId) Interlocked.Add(ref errors, value);
        });
        meters.Start();

        var handler = new StreamHandler(jsonRpc, scenario);
        using var http = new HttpClient(handler);
        IA2AClient client = jsonRpc
            ? new A2AClient(new Uri("https://agent.example"), http)
            : new A2AHttpJsonClient(new Uri("https://agent.example"), http);
        var seen = 0;
        async Task ConsumeAsync()
        {
            await foreach (var item in client.SubscribeToTaskAsync(new SubscribeToTaskRequest { Id = "t" }))
            {
                seen++;
                Assert.Equal("m", item.Message!.MessageId);
            }
        }

        Exception? failure = null;
        if (scenario == "success")
        {
            await ConsumeAsync();
        }
        else
        {
            failure = await Record.ExceptionAsync(ConsumeAsync);
            Assert.NotNull(failure);
            if (scenario == "protocol")
            {
                var protocol = Assert.IsType<A2AException>(failure);
                Assert.Equal(A2AErrorCode.TaskNotFound, protocol.ErrorCode);
                Assert.Equal("stream failure", protocol.Message);
                Assert.Contains("https://example.org/ext", protocol.ActivatedExtensions);
            }
            else if (scenario == "id-mismatch")
            {
                var protocol = Assert.IsType<A2AException>(failure);
                Assert.Equal(A2AErrorCode.InvalidAgentResponse, protocol.ErrorCode);
                Assert.Contains("https://example.org/ext", protocol.ActivatedExtensions);
            }
            else if (scenario == "invalid-json") Assert.IsType<JsonException>(failure);
            else Assert.IsType<IOException>(failure);
        }

        Assert.Equal(scenario == "network-initial" ? 0 : 1, seen);
        Assert.Equal(scenario == "success" ? 0 : 1, errors);
        Assert.NotNull(completed);
        Assert.Equal(scenario == "success" ? ActivityStatusCode.Unset : ActivityStatusCode.Error, completed.Status);
        if (failure is not null) Assert.Equal(failure.Message, completed.StatusDescription);
        Assert.True(handler.Stream!.Disposed);
        Assert.True(handler.Response!.Disposed);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ConsumerCancellationOrStoppingEarly_DoesNotRecordError(bool jsonRpc, bool cancel)
    {
        using var parent = new Activity("stream-cancellation-test").SetIdFormat(ActivityIdFormat.W3C).Start();
        var traceId = parent.TraceId;
        Activity? completed = null;
        using var activities = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "A2A",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => { if (activity.TraceId == traceId) completed = activity; },
        };
        ActivitySource.AddActivityListener(activities);
        long errors = 0;
        using var meters = new MeterListener();
        meters.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == "A2A" && instrument.Name == "a2a.client.error.count")
                listener.EnableMeasurementEvents(instrument);
        };
        meters.SetMeasurementEventCallback<long>((_, value, _, _) =>
        {
            if (Activity.Current?.TraceId == traceId) Interlocked.Add(ref errors, value);
        });
        meters.Start();
        var handler = new StreamHandler(jsonRpc, "network");
        using var http = new HttpClient(handler);
        using var cts = new CancellationTokenSource();
        IA2AClient client = jsonRpc
            ? new A2AClient(new Uri("https://agent.example"), http)
            : new A2AHttpJsonClient(new Uri("https://agent.example"), http);
        async Task ConsumeAsync()
        {
            await foreach (var _ in client.SubscribeToTaskAsync(new SubscribeToTaskRequest { Id = "t" }, cts.Token))
            {
                if (cancel) await cts.CancelAsync();
                else break;
            }
        }
        var failure = await Record.ExceptionAsync(ConsumeAsync);
        if (cancel) Assert.IsAssignableFrom<OperationCanceledException>(failure);
        else Assert.Null(failure);
        Assert.Equal(0, errors);
        Assert.NotNull(completed);
        Assert.Equal(ActivityStatusCode.Unset, completed.Status);
        Assert.True(handler.Stream!.Disposed);
        Assert.True(handler.Response!.Disposed);
    }

    private sealed class StreamHandler(bool jsonRpc, string scenario) : HttpMessageHandler
    {
        public TrackingStream? Stream { get; private set; }
        public TrackingResponse? Response { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string? id = null;
            if (jsonRpc)
            {
                using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                id = doc.RootElement.GetProperty("id").GetRawText();
            }
            var item = JsonSerializer.Serialize(new StreamResponse
            {
                Message = new Message { Role = Role.Agent, MessageId = "m", Parts = [Part.FromText("first")] },
            }, A2AJsonUtilities.DefaultOptions);
            var first = jsonRpc ? $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"result\":{item}}}" : item;
            var second = scenario switch
            {
                "protocol" when jsonRpc => $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"error\":{{\"code\":-32001,\"message\":\"stream failure\"}}}}",
                "protocol" => """{"error":{"code":-32001,"message":"stream failure"}}""",
                "id-mismatch" => $"{{\"jsonrpc\":\"2.0\",\"id\":\"wrong-id\",\"result\":{item}}}",
                "invalid-json" => "{",
                _ => null,
            };
            var payload = scenario == "network-initial" ? "" : $"data: {first}\n\n" + (second is null ? "" : $"data: {second}\n\n");
            Stream = new TrackingStream(Encoding.UTF8.GetBytes(payload), scenario is "network" or "network-initial");
            var response = new TrackingResponse { Content = new StreamContent(Stream) };
            Response = response;
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
            response.Headers.TryAddWithoutValidation("A2A-Extensions", "https://example.org/ext");
            return response;
        }
    }

    private sealed class TrackingResponse : HttpResponseMessage
    {
        public bool Disposed { get; private set; }
        protected override void Dispose(bool disposing) { if (disposing) Disposed = true; base.Dispose(disposing); }
    }

    private sealed class TrackingStream(byte[] bytes, bool failAfterData) : Stream
    {
        private readonly MemoryStream _inner = new(bytes);
        public bool Disposed { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = _inner.Read(buffer, offset, count);
            return read == 0 && failAfterData ? throw new IOException("read failed") : read;
        }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = _inner.Read(buffer.Span);
            return read == 0 && failAfterData ? ValueTask.FromException<int>(new IOException("read failed")) : ValueTask.FromResult(read);
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        protected override void Dispose(bool disposing) { if (disposing) { Disposed = true; _inner.Dispose(); } base.Dispose(disposing); }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
