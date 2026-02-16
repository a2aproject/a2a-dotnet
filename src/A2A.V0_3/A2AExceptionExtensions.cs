namespace A2A.V0_3
{
    /// <summary>
    /// Provides extension methods for <see cref="A2AException"/>.
    /// </summary>
    public static class A2AExceptionExtensions
    {
        private const string RequestIdKey = "RequestId";

        /// <summary>
        /// Associates a request ID with the specified <see cref="A2AException"/>.
        /// </summary>
        /// <param name="exception">The <see cref="A2AException"/> to associate the request ID with.</param>
        /// <param name="requestId">The request ID to associate with the exception. Can be null.</param>
        /// <returns>The same <see cref="A2AException"/> instance with the request ID stored in its Data collection.</returns>
        /// <remarks>
        /// This method stores the request ID in the exception's Data collection using the key "RequestId".
        /// The request ID can be later retrieved using the <see cref="GetRequestId"/> method.
        /// This is useful for correlating exceptions with specific HTTP requests in logging and debugging scenarios.
        /// </remarks>
        public static A2AException WithRequestId(this A2AException exception, string? requestId)
        {
            if (exception is null)
            {
                throw new ArgumentNullException(nameof(exception));
            }

            exception.Data[RequestIdKey] = requestId;

            return exception;
        }

        /// <summary>
        /// Associates a request ID with the specified <see cref="A2AException"/>.
        /// </summary>
        /// <param name="exception">The <see cref="A2AException"/> to associate the request ID with.</param>
        /// <param name="requestId">The request ID to associate with the exception.</param>
        /// <returns>The same <see cref="A2AException"/> instance with the request ID stored in its Data collection.</returns>
        /// <remarks>
        /// This method stores the request ID in the exception's Data collection using the key "RequestId".
        /// The request ID can be later retrieved using the <see cref="GetRequestId"/> method.
        /// This is useful for correlating exceptions with specific HTTP requests in logging and debugging scenarios.
        /// </remarks>
        public static A2AException WithRequestId(this A2AException exception, JsonRpcId requestId)
        {
            if (exception is null)
            {
                throw new ArgumentNullException(nameof(exception));
            }

            exception.Data[RequestIdKey] = requestId.ToString();

            return exception;
        }

        /// <summary>
        /// Retrieves the request ID associated with the specified <see cref="A2AException"/>.
        /// </summary>
        /// <param name="exception">The <see cref="A2AException"/> to retrieve the request ID from.</param>
        /// <returns>
        /// The request ID associated with the exception if one was previously set using <see cref="WithRequestId(A2AException, string?)"/>,
        /// or null if no request ID was set or if the stored value is not a string.
        /// </returns>
        /// <remarks>
        /// This method retrieves the request ID from the exception's Data collection using the key "RequestId".
        /// If the stored value is not a string or doesn't exist, null is returned.
        /// This method is typically used in exception handlers to correlate exceptions with specific HTTP requests.
        /// </remarks>
        public static string? GetRequestId(this A2AException exception)
        {
            if (exception is null)
            {
                throw new ArgumentNullException(nameof(exception));
            }

            if (exception.Data[RequestIdKey] is string requestIdString)
            {
                return requestIdString;
            }

            return null;
        }
    }
}
