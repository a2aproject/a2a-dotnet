namespace A2A;

internal enum A2AOperationSource
{
    Standard,
    Extension,
}

internal sealed class A2AOperationRegistration
{
    internal A2AOperationRegistration(
        A2AOperationId id,
        A2AOperationKind kind,
        Type requestType,
        Type responseType,
        object handle,
        Delegate? validator,
        A2AOperationSource source,
        IReadOnlyDictionary<string, object> declaredErrors)
    {
        Id = id;
        Kind = kind;
        RequestType = requestType;
        ResponseType = responseType;
        Handle = handle;
        Validator = validator;
        Source = source;
        DeclaredErrors = declaredErrors;
    }

    internal A2AOperationId Id { get; }

    internal A2AOperationKind Kind { get; }

    internal Type RequestType { get; }

    internal Type ResponseType { get; }

    internal object Handle { get; }

    internal Delegate? Validator { get; }

    internal A2AOperationSource Source { get; }

    internal IReadOnlyDictionary<string, object> DeclaredErrors { get; }
}

internal sealed class A2AOperationRegistrationBuilder
{
    internal A2AOperationRegistrationBuilder(
        A2AOperationId id,
        A2AOperationKind kind,
        Type requestType,
        Type responseType,
        object handle,
        Delegate? validator,
        A2AOperationSource source)
    {
        Id = id;
        Kind = kind;
        RequestType = requestType;
        ResponseType = responseType;
        Handle = handle;
        Validator = validator;
        Source = source;
    }

    internal A2AOperationId Id { get; }

    internal A2AOperationKind Kind { get; }

    internal Type RequestType { get; }

    internal Type ResponseType { get; }

    internal object Handle { get; }

    internal Delegate? Validator { get; }

    internal A2AOperationSource Source { get; }

    internal Dictionary<string, object> DeclaredErrors { get; } =
        new(StringComparer.Ordinal);

    internal A2AOperationRegistration Freeze()
        => new(
            Id,
            Kind,
            RequestType,
            ResponseType,
            Handle,
            Validator,
            Source,
            new Dictionary<string, object>(DeclaredErrors, StringComparer.Ordinal));
}

/// <summary>Builds an immutable catalog of transport-neutral A2A operations.</summary>
public sealed class A2AOperationCatalogBuilder
{
    private readonly Dictionary<A2AOperationId, A2AOperationRegistrationBuilder>
        _registrations = [];

    /// <summary>Defines a typed unary A2A operation.</summary>
    /// <typeparam name="TRequest">The operation request type.</typeparam>
    /// <typeparam name="TResult">The operation result type.</typeparam>
    /// <param name="id">The transport-neutral operation identifier.</param>
    /// <param name="validator">Optional semantic request validator.</param>
    /// <returns>The typed operation handle.</returns>
    public A2AOperation<TRequest, TResult> DefineUnary<TRequest, TResult>(
        A2AOperationId id,
        A2AOperationValidator<TRequest>? validator = null)
        => DefineUnary<TRequest, TResult>(
            id,
            validator,
            A2AOperationSource.Extension);

    /// <summary>Defines a typed streaming A2A operation.</summary>
    /// <typeparam name="TRequest">The operation request type.</typeparam>
    /// <typeparam name="TEvent">The streamed event type.</typeparam>
    /// <param name="id">The transport-neutral operation identifier.</param>
    /// <param name="validator">Optional semantic request validator.</param>
    /// <returns>The typed streaming operation handle.</returns>
    public A2AStreamingOperation<TRequest, TEvent> DefineStreaming<TRequest, TEvent>(
        A2AOperationId id,
        A2AOperationValidator<TRequest>? validator = null)
        => DefineStreaming<TRequest, TEvent>(
            id,
            validator,
            A2AOperationSource.Extension);

    /// <summary>Declares a typed error for a unary operation.</summary>
    /// <typeparam name="TRequest">The operation request type.</typeparam>
    /// <typeparam name="TResult">The operation result type.</typeparam>
    /// <typeparam name="TDetails">The typed error details payload.</typeparam>
    /// <param name="operation">The operation that declares the error.</param>
    /// <param name="errorId">The stable error identifier.</param>
    /// <returns>The typed error handle.</returns>
    public A2AOperationError<TDetails> DeclareError<TRequest, TResult, TDetails>(
        A2AOperation<TRequest, TResult> operation,
        string errorId)
        => DeclareErrorCore<A2AOperation<TRequest, TResult>, TDetails>(
            operation,
            errorId);

    /// <summary>Declares a typed error for a streaming operation.</summary>
    /// <typeparam name="TRequest">The operation request type.</typeparam>
    /// <typeparam name="TEvent">The streamed event type.</typeparam>
    /// <typeparam name="TDetails">The typed error details payload.</typeparam>
    /// <param name="operation">The streaming operation that declares the error.</param>
    /// <param name="errorId">The stable error identifier.</param>
    /// <returns>The typed error handle.</returns>
    public A2AOperationError<TDetails> DeclareError<TRequest, TEvent, TDetails>(
        A2AStreamingOperation<TRequest, TEvent> operation,
        string errorId)
        => DeclareErrorCore<A2AStreamingOperation<TRequest, TEvent>, TDetails>(
            operation,
            errorId);

    /// <summary>Builds the immutable operation catalog.</summary>
    /// <returns>The operation catalog.</returns>
    public A2AOperationCatalog Build()
        => new(_registrations.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Freeze()));

    internal A2AOperation<TRequest, TResult> DefineStandardUnary<TRequest, TResult>(
        A2AOperationId id,
        A2AOperationValidator<TRequest>? validator = null)
        => DefineUnary<TRequest, TResult>(
            id,
            validator,
            A2AOperationSource.Standard);

    internal A2AStreamingOperation<TRequest, TEvent> DefineStandardStreaming<TRequest, TEvent>(
        A2AOperationId id,
        A2AOperationValidator<TRequest>? validator = null)
        => DefineStreaming<TRequest, TEvent>(
            id,
            validator,
            A2AOperationSource.Standard);

    private A2AOperation<TRequest, TResult> DefineUnary<TRequest, TResult>(
        A2AOperationId id,
        A2AOperationValidator<TRequest>? validator,
        A2AOperationSource source)
    {
        ValidateOperationId(id);

        var operation = new A2AOperation<TRequest, TResult>(
            id,
            requiresCatalog: true);
        AddRegistration(new A2AOperationRegistrationBuilder(
            id,
            A2AOperationKind.Unary,
            typeof(TRequest),
            typeof(TResult),
            operation,
            validator,
            source));
        return operation;
    }

    private A2AStreamingOperation<TRequest, TEvent> DefineStreaming<TRequest, TEvent>(
        A2AOperationId id,
        A2AOperationValidator<TRequest>? validator,
        A2AOperationSource source)
    {
        ValidateOperationId(id);

        var operation = new A2AStreamingOperation<TRequest, TEvent>(
            id,
            requiresCatalog: true);
        AddRegistration(new A2AOperationRegistrationBuilder(
            id,
            A2AOperationKind.Streaming,
            typeof(TRequest),
            typeof(TEvent),
            operation,
            validator,
            source));
        return operation;
    }

    private A2AOperationError<TDetails> DeclareErrorCore<TOperation, TDetails>(
        TOperation operation,
        string errorId)
        where TOperation : class
    {
        ArgumentNullException.ThrowIfNull(operation);
        ValidateErrorId(errorId);

        if (!TryGetRegisteredOperation(operation, out var registration))
        {
            throw new InvalidOperationException(
                $"The A2A operation '{GetOperationId(operation).Value}' is not defined by this catalog builder.");
        }

        if (registration.DeclaredErrors.ContainsKey(errorId))
        {
            throw new InvalidOperationException(
                $"The A2A operation '{registration.Id.Value}' already declares error '{errorId}'.");
        }

        var error = new A2AOperationError<TDetails>(errorId);
        registration.DeclaredErrors.Add(errorId, error);
        return error;
    }

    private void AddRegistration(A2AOperationRegistrationBuilder registration)
    {
        if (!_registrations.TryAdd(registration.Id, registration))
        {
            throw new InvalidOperationException(
                $"An A2A operation is already registered for '{registration.Id.Value}'.");
        }
    }

    private bool TryGetRegisteredOperation<TOperation>(
        TOperation operation,
        out A2AOperationRegistrationBuilder registration)
        where TOperation : class
    {
        var operationId = GetOperationId(operation);
        if (_registrations.TryGetValue(operationId, out registration!))
        {
            return ReferenceEquals(registration.Handle, operation);
        }

        return false;
    }

    private static A2AOperationId GetOperationId<TOperation>(TOperation operation)
        where TOperation : class
        => operation switch
        {
            IA2AOperationHandle handle => handle.Id,
            _ => throw new InvalidOperationException(
                $"Unsupported A2A operation handle type '{operation.GetType().FullName}'."),
        };

    private static void ValidateOperationId(A2AOperationId id)
    {
        if (string.IsNullOrWhiteSpace(id.Value))
        {
            throw new ArgumentException(
                "An A2A operation id cannot be null, empty, or whitespace.",
                nameof(id));
        }
    }

    private static void ValidateErrorId(string errorId)
    {
        if (string.IsNullOrWhiteSpace(errorId))
        {
            throw new ArgumentException(
                "An A2A operation error id cannot be null, empty, or whitespace.",
                nameof(errorId));
        }
    }

}

internal interface IA2AOperationHandle
{
    A2AOperationId Id { get; }
}

/// <summary>Provides immutable access to registered A2A operations.</summary>
public sealed class A2AOperationCatalog
{
    private readonly Dictionary<A2AOperationId, A2AOperationRegistration> _registrations;

    internal A2AOperationCatalog(
        IReadOnlyDictionary<A2AOperationId, A2AOperationRegistration> registrations)
    {
        _registrations = new Dictionary<A2AOperationId, A2AOperationRegistration>(
            registrations);
    }

    /// <summary>Gets the registered unary operation handle for an identifier.</summary>
    /// <param name="id">The operation identifier.</param>
    /// <returns>The registered unary operation handle.</returns>
    public object GetRequired(A2AOperationId id)
        => GetRequiredRegistration(id, A2AOperationKind.Unary).Handle;

    /// <summary>Gets the registered streaming operation handle for an identifier.</summary>
    /// <param name="id">The operation identifier.</param>
    /// <returns>The registered streaming operation handle.</returns>
    public object GetRequiredStreaming(A2AOperationId id)
        => GetRequiredRegistration(id, A2AOperationKind.Streaming).Handle;

    /// <summary>Gets the registered typed unary operation handle for an identifier.</summary>
    /// <typeparam name="TRequest">The operation request type.</typeparam>
    /// <typeparam name="TResult">The operation result type.</typeparam>
    /// <param name="id">The operation identifier.</param>
    /// <returns>The registered unary operation handle.</returns>
    public A2AOperation<TRequest, TResult> GetRequired<TRequest, TResult>(
        A2AOperationId id)
    {
        var registration = GetRequiredRegistration(id, A2AOperationKind.Unary);
        return registration.Handle as A2AOperation<TRequest, TResult>
            ?? throw CreateTypeMismatchException(
                id,
                A2AOperationKind.Unary,
                typeof(TRequest),
                typeof(TResult));
    }

    /// <summary>Gets the registered typed streaming operation handle for an identifier.</summary>
    /// <typeparam name="TRequest">The operation request type.</typeparam>
    /// <typeparam name="TEvent">The streamed event type.</typeparam>
    /// <param name="id">The operation identifier.</param>
    /// <returns>The registered streaming operation handle.</returns>
    public A2AStreamingOperation<TRequest, TEvent>
        GetRequiredStreaming<TRequest, TEvent>(A2AOperationId id)
    {
        var registration = GetRequiredRegistration(id, A2AOperationKind.Streaming);
        return registration.Handle as A2AStreamingOperation<TRequest, TEvent>
            ?? throw CreateTypeMismatchException(
                id,
                A2AOperationKind.Streaming,
                typeof(TRequest),
                typeof(TEvent));
    }

    /// <summary>Gets a declared typed error for a unary operation.</summary>
    /// <typeparam name="TRequest">The operation request type.</typeparam>
    /// <typeparam name="TResult">The operation result type.</typeparam>
    /// <typeparam name="TDetails">The error details type.</typeparam>
    /// <param name="operation">The declaring operation.</param>
    /// <param name="errorId">The error identifier.</param>
    /// <returns>The declared typed error handle.</returns>
    public A2AOperationError<TDetails> GetRequiredError<TRequest, TResult, TDetails>(
        A2AOperation<TRequest, TResult> operation,
        string errorId)
        => GetRequiredErrorCore<TDetails>(operation, errorId, A2AOperationKind.Unary);

    /// <summary>Gets a declared typed error for a streaming operation.</summary>
    /// <typeparam name="TRequest">The operation request type.</typeparam>
    /// <typeparam name="TEvent">The streamed event type.</typeparam>
    /// <typeparam name="TDetails">The error details type.</typeparam>
    /// <param name="operation">The declaring streaming operation.</param>
    /// <param name="errorId">The error identifier.</param>
    /// <returns>The declared typed error handle.</returns>
    public A2AOperationError<TDetails>
        GetRequiredStreamingError<TRequest, TEvent, TDetails>(
            A2AStreamingOperation<TRequest, TEvent> operation,
            string errorId)
        => GetRequiredErrorCore<TDetails>(operation, errorId, A2AOperationKind.Streaming);

    /// <summary>Validates a typed unary operation request against the catalog-owned semantic validator.</summary>
    /// <typeparam name="TRequest">The operation request type.</typeparam>
    /// <typeparam name="TResult">The operation result type.</typeparam>
    /// <param name="operation">The registered operation definition.</param>
    /// <param name="request">The request to validate.</param>
    public void Validate<TRequest, TResult>(
        A2AOperation<TRequest, TResult> operation,
        TRequest request)
    {
        var registration = GetRequiredUnaryRegistration(operation);
        if (registration.Validator is A2AOperationValidator<TRequest> validator)
        {
            validator(request);
        }
    }

    /// <summary>Validates a typed streaming operation request against the catalog-owned semantic validator.</summary>
    /// <typeparam name="TRequest">The operation request type.</typeparam>
    /// <typeparam name="TEvent">The streamed event type.</typeparam>
    /// <param name="operation">The registered streaming operation definition.</param>
    /// <param name="request">The request to validate.</param>
    public void ValidateStreaming<TRequest, TEvent>(
        A2AStreamingOperation<TRequest, TEvent> operation,
        TRequest request)
    {
        var registration = GetRequiredStreamingRegistration(operation);
        if (registration.Validator is A2AOperationValidator<TRequest> validator)
        {
            validator(request);
        }
    }

    internal A2AOperationSource GetSource(A2AOperationId id)
        => GetRequiredRegistration(id).Source;

    internal bool TryGetRegistration(
        A2AOperationId id,
        out A2AOperationRegistration registration)
        => _registrations.TryGetValue(id, out registration!);

    private A2AOperationError<TDetails> GetRequiredErrorCore<TDetails>(
        IA2AOperationHandle operation,
        string errorId,
        A2AOperationKind expectedKind)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (string.IsNullOrWhiteSpace(errorId))
        {
            throw new ArgumentException(
                "An A2A operation error id cannot be null, empty, or whitespace.",
                nameof(errorId));
        }

        var registration = GetRequiredRegistration(operation, expectedKind);
        if (!registration.DeclaredErrors.TryGetValue(errorId, out var error))
        {
            throw new InvalidOperationException(
                $"The A2A operation '{registration.Id.Value}' does not declare error '{errorId}'.");
        }

        return error as A2AOperationError<TDetails>
            ?? throw new InvalidOperationException(
                $"The A2A operation error '{errorId}' for '{registration.Id.Value}' uses incompatible details type '{typeof(TDetails).FullName}'.");
    }

    private A2AOperationRegistration GetRequiredRegistration(A2AOperationId id)
    {
        if (!_registrations.TryGetValue(id, out var registration))
        {
            throw new InvalidOperationException(
                $"No A2A operation is registered for '{id.Value}'.");
        }

        return registration;
    }

    private A2AOperationRegistration GetRequiredRegistration(
        A2AOperationId id,
        A2AOperationKind expectedKind)
    {
        var registration = GetRequiredRegistration(id);
        if (registration.Kind != expectedKind)
        {
            throw new InvalidOperationException(
                $"The A2A operation '{id.Value}' is registered as {registration.Kind} instead of {expectedKind}.");
        }

        return registration;
    }

    private A2AOperationRegistration GetRequiredRegistration(
        IA2AOperationHandle operation,
        A2AOperationKind expectedKind)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var registration = GetRequiredRegistration(operation.Id, expectedKind);
        if (!ReferenceEquals(registration.Handle, operation))
        {
            throw new InvalidOperationException(
                $"The A2A operation '{operation.Id.Value}' does not belong to this catalog.");
        }

        return registration;
    }

    private A2AOperationRegistration GetRequiredUnaryRegistration<TRequest, TResult>(
        A2AOperation<TRequest, TResult> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var registration = GetRequiredRegistration(operation.Id, A2AOperationKind.Unary);
        if (registration.Handle is not A2AOperation<TRequest, TResult> typedOperation)
        {
            throw CreateTypeMismatchException(
                operation.Id,
                A2AOperationKind.Unary,
                typeof(TRequest),
                typeof(TResult));
        }

        if (!ReferenceEquals(typedOperation, operation))
        {
            throw new InvalidOperationException(
                $"The A2A operation '{operation.Id.Value}' does not belong to this catalog.");
        }

        return registration;
    }

    private A2AOperationRegistration GetRequiredStreamingRegistration<TRequest, TEvent>(
        A2AStreamingOperation<TRequest, TEvent> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var registration = GetRequiredRegistration(operation.Id, A2AOperationKind.Streaming);
        if (registration.Handle is not A2AStreamingOperation<TRequest, TEvent> typedOperation)
        {
            throw CreateTypeMismatchException(
                operation.Id,
                A2AOperationKind.Streaming,
                typeof(TRequest),
                typeof(TEvent));
        }

        if (!ReferenceEquals(typedOperation, operation))
        {
            throw new InvalidOperationException(
                $"The A2A operation '{operation.Id.Value}' does not belong to this catalog.");
        }

        return registration;
    }

    private static InvalidOperationException CreateTypeMismatchException(
        A2AOperationId id,
        A2AOperationKind expectedKind,
        Type requestType,
        Type responseType)
        => new(
            $"The {expectedKind} A2A operation '{id.Value}' uses incompatible request or response types '{requestType.FullName}' and '{responseType.FullName}'.");
}
