// ACTS SUT behaviour contract (ACTS spec §11).
//
// ACTS tests are declarative — they say what to send and what to expect — so
// the agent under test has to produce a deterministic reply for each case.
// §11 does that with a message-prefix convention rather than a side-channel
// API: the text of the first user message names the behaviour. ItkAgent routes
// here when it sees a "tck-" prefix and otherwise runs the ITK instruction
// path, so one binary serves both suites.
//
// acts/sut-behaviors.yaml is what this SDK claims; this file is what it does.
// Nothing here restates the list of names — the prefix is read out of the
// message, and one that reaches DispatchAsync without a branch fails the task
// rather than completing it, so a gap shows up in the conformance report
// instead of passing quietly.

using System.Text.Json;
using System.Text.RegularExpressions;

namespace A2A.Itk;

/// <summary>Serves the ACTS <c>tck-*</c> behaviours.</summary>
public static partial class ActsBehaviors
{
    private const string MultiTurnDone = "done";

    /// <summary>
    /// Short enough not to dominate a run, long enough that a test polling for a
    /// non-terminal state sees one: the corpus polls every 2s, 15 times.
    /// </summary>
    private static readonly TimeSpan LongRunningDelay = TimeSpan.FromSeconds(1);

    // Greedy to the word boundary, which gives longest-match for free:
    // "tck-artifact-file-url" beats "tck-artifact-file" with no ordered table.
    [GeneratedRegex(@"^(tck-[a-z0-9]+(?:-[a-z0-9]+)*)")]
    private static partial Regex NamePattern();

    private static readonly Dictionary<string, TaskState> TerminalStates = new()
    {
        ["tck-complete-task"] = TaskState.Completed,
        ["tck-task-failure"] = TaskState.Failed,
        ["tck-reject-task"] = TaskState.Rejected,
        ["tck-input-required"] = TaskState.InputRequired,
        ["tck-auth-required"] = TaskState.AuthRequired,
    };

    /// <summary>
    /// What the card advertises — everything the SDK supports, unless the ACTS runner
    /// asked for less.
    /// </summary>
    /// <remarks>
    /// Tests asserting that an agent <em>without</em> a capability answers
    /// UnsupportedOperationError have preconditions requiring the card not to advertise it,
    /// so they can never run against a fully capable agent. The runner starts a second SUT
    /// with <c>ITK_ACTS_REDUCED_CAPABILITIES</c> set to reach them.
    ///
    /// PushNotifications stays false in both modes: A2AServer answers every push-config
    /// operation with PushNotificationNotSupported, so advertising it would be a claim the
    /// agent cannot honour.
    /// </remarks>
    public static AgentCapabilities Capabilities()
    {
        var reduced = !string.IsNullOrEmpty(
            Environment.GetEnvironmentVariable("ITK_ACTS_REDUCED_CAPABILITIES"));

        return new AgentCapabilities
        {
            Streaming = !reduced,
            PushNotifications = false,
            ExtendedAgentCard = !reduced,
        };
    }

    /// <summary>
    /// Names an asserted behaviour, not necessarily an implemented one.
    /// </summary>
    /// <remarks>
    /// An unknown "tck-" still routes to ACTS and is reported as unimplemented, which beats
    /// handing a message plainly meant for ACTS to the traversal decoder.
    /// </remarks>
    public static string? BehaviorIn(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = NamePattern().Match(text.Trim());
        return match.Success ? match.Value : null;
    }

    /// <summary>
    /// Resolves the behaviour from the incoming message, falling back to the task's history.
    /// </summary>
    /// <remarks>
    /// A multi-turn test opens with the prefix and then sends plain "here is more input" and
    /// "done", so a continuation has to recover the contract from where it was declared.
    /// </remarks>
    public static string? BehaviorFor(RequestContext context)
    {
        if (BehaviorIn(FirstText(context.Message)) is { } named)
        {
            return named;
        }

        foreach (var historical in context.Task?.History ?? [])
        {
            if (BehaviorIn(FirstText(historical)) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static string? FirstText(Message? message)
        => message?.Parts.FirstOrDefault(p => p.Text is not null)?.Text;

    /// <summary>Serves one ACTS behaviour.</summary>
    public static async Task RunAsync(
        RequestContext context,
        AgentEventQueue eventQueue,
        string behavior,
        IHttpClientFactory httpClientFactory,
        CancellationToken cancellationToken)
    {
        // This one must open no task at all: A2A lets an agent answer with a bare
        // Message, and a server that created a task first would turn the reply into
        // a task update, which is what CORE-SEND-003 checks.
        if (behavior == "tck-message-response")
        {
            await new MessageResponder(eventQueue, context.ContextId)
                .ReplyAsync("tck message response", cancellationToken: cancellationToken);
            eventQueue.Complete();
            return;
        }

        // On every turn, continuation included: A2AServer materializes its reply from
        // the first Task or Message event it sees and ignores status updates, so a
        // continuation that emitted only those would fail with "did not produce any
        // response events".
        await ItkAgent.OpenTaskAsync(context, eventQueue, cancellationToken);

        var updater = new TaskUpdater(eventQueue, context.TaskId, context.ContextId);
        await updater.StartWorkAsync(cancellationToken: cancellationToken);
        await DispatchAsync(context, updater, behavior, httpClientFactory, cancellationToken);
    }

    private static async Task DispatchAsync(
        RequestContext context,
        TaskUpdater updater,
        string behavior,
        IHttpClientFactory httpClientFactory,
        CancellationToken cancellationToken)
    {
        if (behavior == ActsClientParse.Behavior)
        {
            await ActsClientParse.RunAsync(context, updater, httpClientFactory, cancellationToken);
            return;
        }

        if (behavior.StartsWith("tck-artifact-", StringComparison.Ordinal))
        {
            var parts = ArtifactParts(behavior);
            if (parts is null)
            {
                await UnimplementedAsync(updater, behavior, cancellationToken);
                return;
            }

            await updater.AddArtifactAsync(parts, name: behavior, cancellationToken: cancellationToken);
            await updater.CompleteAsync(AgentText($"{behavior} ok"), cancellationToken: cancellationToken);
            return;
        }

        switch (behavior)
        {
            case "tck-multi-turn":
                await MultiTurnAsync(context, updater, cancellationToken);
                return;

            case "tck-cancel":
                // Hold in WORKING. CancelTaskAsync cancels this token and emits the
                // terminal event itself, so there is nothing to do but wait.
                await HoldUntilCanceledAsync(cancellationToken);
                return;

            case "tck-long-running":
                await LongRunningAsync(updater, cancellationToken);
                return;

            case "tck-stream-basic":
            case "tck-stream-chunked":
                await StreamAsync(updater, behavior, cancellationToken);
                return;
        }

        if (!TerminalStates.TryGetValue(behavior, out var state))
        {
            await UnimplementedAsync(updater, behavior, cancellationToken);
            return;
        }

        await ReachAsync(updater, state, $"{behavior} ok", cancellationToken);
    }

    private static Task ReachAsync(
        TaskUpdater updater, TaskState state, string text, CancellationToken cancellationToken)
    {
        var message = AgentText(text);
        return state switch
        {
            TaskState.Completed => updater.CompleteAsync(message, cancellationToken: cancellationToken).AsTask(),
            TaskState.Failed => updater.FailAsync(message, cancellationToken: cancellationToken).AsTask(),
            TaskState.Rejected => updater.RejectAsync(message, cancellationToken: cancellationToken).AsTask(),
            TaskState.InputRequired => updater.RequireInputAsync(message, cancellationToken: cancellationToken).AsTask(),
            TaskState.AuthRequired => updater.RequireAuthAsync(message, cancellationToken: cancellationToken).AsTask(),
            _ => updater.FailAsync(AgentText($"no transition for {state}"), cancellationToken: cancellationToken).AsTask(),
        };
    }

    /// <summary>
    /// Fails the task rather than completing it: a silent success would report conformance
    /// the agent never demonstrated.
    /// </summary>
    private static ValueTask UnimplementedAsync(
        TaskUpdater updater, string behavior, CancellationToken cancellationToken)
        => updater.FailAsync(
            AgentText($"unimplemented ACTS behaviour \"{behavior}\""),
            cancellationToken: cancellationToken);

    private static Task MultiTurnAsync(
        RequestContext context, TaskUpdater updater, CancellationToken cancellationToken)
    {
        var said = FirstText(context.Message)?.Trim() ?? string.Empty;
        return said.StartsWith(MultiTurnDone, StringComparison.OrdinalIgnoreCase)
            ? updater.CompleteAsync(AgentText("multi-turn complete"), cancellationToken: cancellationToken).AsTask()
            : updater.RequireInputAsync(AgentText("more input please"), cancellationToken: cancellationToken).AsTask();
    }

    private static async Task HoldUntilCanceledAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static async Task LongRunningAsync(TaskUpdater updater, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(LongRunningDelay, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // CORE-EXEC-001 polls to completion and then asserts the finished task carries at
        // least one artifact, so the work has to leave one behind even though §11.2
        // describes this behaviour only as delayed completion.
        await updater.AddArtifactAsync(
            [Part.FromText("long running result")],
            name: "long-running",
            cancellationToken: cancellationToken);
        await updater.CompleteAsync(
            AgentText("long running work finished"), cancellationToken: cancellationToken);
    }

    private static List<Part>? ArtifactParts(string behavior) => behavior switch
    {
        "tck-artifact-text" => [Part.FromText("generated text content")],
        "tck-artifact-data" => [Part.FromData(JsonSerializer.SerializeToElement(
            new Dictionary<string, object> { ["key"] = "value", ["count"] = 1 }))],
        "tck-artifact-file" => [Part.FromRaw(
            "file bytes"u8.ToArray(), mediaType: "text/plain", filename: "document.txt")],
        "tck-artifact-file-url" => [Part.FromUrl(
            "https://example.com/document.txt", mediaType: "text/plain", filename: "document.txt")],
        _ => null,
    };

    /// <summary>
    /// Emits working -> artifact(s) -> completed as separate events, so each becomes its own
    /// SSE frame; a single combined update would satisfy min_count only by accident.
    /// </summary>
    private static async Task StreamAsync(
        TaskUpdater updater, string behavior, CancellationToken cancellationToken)
    {
        await updater.StartWorkAsync(AgentText("streaming started"), cancellationToken: cancellationToken);

        if (behavior == "tck-stream-chunked")
        {
            string[] chunks = ["chunk one ", "chunk two ", "chunk three"];
            var artifactId = Guid.NewGuid().ToString("N");

            // The first chunk must go out with append=false; an update naming an
            // artifact the task has not seen fails the whole task.
            for (var i = 0; i < chunks.Length; i++)
            {
                await updater.AddArtifactAsync(
                    [Part.FromText(chunks[i])],
                    artifactId: artifactId,
                    name: "chunked",
                    lastChunk: i == chunks.Length - 1,
                    append: i > 0,
                    cancellationToken: cancellationToken);
            }
        }
        else
        {
            await updater.AddArtifactAsync(
                [Part.FromText("streamed content")], name: "streamed", cancellationToken: cancellationToken);
        }

        await updater.CompleteAsync(AgentText($"{behavior} ok"), cancellationToken: cancellationToken);
    }

    internal static Message AgentText(string text) => new()
    {
        Role = Role.Agent,
        MessageId = Guid.NewGuid().ToString("N"),
        Parts = [Part.FromText(text)],
    };
}
