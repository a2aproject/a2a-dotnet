using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace A2A;

internal static class A2AOperationDiagnostics
{
    internal static Activity? Start(
        ActivitySource activitySource,
        A2AOperationId operationId,
        A2AOperationKind kind,
        A2AOperationSource source,
        string role,
        string transport)
    {
        var activity = activitySource.StartActivity("a2a.operation", ActivityKind.Internal);
        activity?.SetTag("a2a.operation.id", operationId.Value);
        activity?.SetTag("a2a.operation.kind", kind == A2AOperationKind.Unary ? "unary" : "streaming");
        activity?.SetTag("a2a.operation.source", source == A2AOperationSource.Standard ? "standard" : "extension");
        activity?.SetTag("a2a.operation.role", role);
        activity?.SetTag("a2a.transport", transport);
        return activity;
    }

    internal static void SetOutcome(Activity? activity, string outcome)
    {
        // A handled transport error must not become a success when its response is written.
        if (outcome == "success" && activity?.GetTagItem("a2a.operation.outcome") is not null)
        {
            return;
        }

        activity?.SetTag("a2a.operation.outcome", outcome);
        activity?.SetStatus(outcome == "error" ? ActivityStatusCode.Error : ActivityStatusCode.Unset);
    }

    internal static void SetError(
        Activity? activity, Exception exception, CancellationToken cancellationToken = default) =>
        SetOutcome(activity, exception is OperationCanceledException && cancellationToken.IsCancellationRequested
            ? "cancelled" : "error");

    internal static async Task<TResult> InvokeAsync<TResult>(
        A2AOperationId id,
        A2AOperationSource source,
        string transport,
        Func<CancellationToken, Task<TResult>> invoke,
        CancellationToken cancellationToken)
    {
        using var activity = Start(A2ADiagnostics.Source, id, A2AOperationKind.Unary, source, "client", transport);
        try
        {
            var result = await invoke(cancellationToken).ConfigureAwait(false);
            SetOutcome(activity, "success");
            return result;
        }
        catch (Exception exception)
        {
            SetError(activity, exception, cancellationToken);
            throw;
        }
    }

    internal static async IAsyncEnumerable<TEvent> InvokeStreamingAsync<TEvent>(
        A2AOperationId id,
        A2AOperationSource source,
        string transport,
        Func<IAsyncEnumerable<TEvent>> invoke,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var activity = Start(A2ADiagnostics.Source, id, A2AOperationKind.Streaming, source, "client", transport);
        IAsyncEnumerator<TEvent> enumerator;
        try
        {
            enumerator = invoke().GetAsyncEnumerator(cancellationToken);
        }
        catch (Exception exception)
        {
            SetError(activity, exception, cancellationToken);
            throw;
        }

        var completed = false;
        try
        {
            while (true)
            {
                TEvent current;
                try
                {
                    if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                    {
                        completed = true;
                        break;
                    }

                    current = enumerator.Current;
                }
                catch (Exception exception)
                {
                    SetError(activity, exception, cancellationToken);
                    throw;
                }

                yield return current;
            }
        }
        finally
        {
            await DisposeAsync(enumerator, activity, cancellationToken).ConfigureAwait(false);
            if (activity?.GetTagItem("a2a.operation.outcome") is null)
            {
                SetOutcome(activity, completed ? "success" : cancellationToken.IsCancellationRequested ? "cancelled" : "error");
            }
        }
    }

    internal static async ValueTask DisposeAsync(
        IAsyncDisposable resource, Activity? activity, CancellationToken cancellationToken)
    {
        try
        {
            await resource.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            SetError(activity, exception, cancellationToken);
            throw;
        }
    }
}
