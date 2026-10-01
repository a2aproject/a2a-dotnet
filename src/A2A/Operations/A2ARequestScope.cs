namespace A2A;

/// <summary>Owns an operation context and its asynchronous request lifetime.</summary>
public sealed class A2ARequestScope : IAsyncDisposable
{
    private Func<ValueTask>? _disposeAsync;

    /// <summary>Initializes a new request scope.</summary>
    /// <param name="context">The operation context.</param>
    /// <param name="disposeAsync">Optional asynchronous cleanup callback.</param>
    public A2ARequestScope(
        A2AOperationContext context,
        Func<ValueTask>? disposeAsync = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        Context = context;
        _disposeAsync = disposeAsync;
    }

    /// <summary>Gets the operation context.</summary>
    public A2AOperationContext Context { get; }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        var disposeAsync = Interlocked.Exchange(ref _disposeAsync, null);
        return disposeAsync is null ? ValueTask.CompletedTask : disposeAsync();
    }
}
