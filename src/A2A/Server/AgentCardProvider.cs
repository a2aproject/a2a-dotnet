namespace A2A
{
    /// <summary>
    /// Provides an agent card for a specific agent.
    /// /// </summary>
    public class AgentCardProvider
    {
        private readonly AgentCard? _agentCard;

        /// <summary>
        /// Creates a new instance of the AgentCardProvider.
        /// </summary>
        /// <param name="agentCard">The valid agent card to initialize with.</param>
        /// <exception cref="ArgumentNullException">Thrown when agentCard is null.</exception>
        /// <exception cref="ArgumentException">Thrown when any required property of agentCard is null or empty.</exception>
        public AgentCardProvider(AgentCard agentCard) => _agentCard = Validate(agentCard);

        /// <summary>
        /// Creates a new instance of the AgentCardProvider without an agent card.
        /// </summary>
        /// <remarks>
        /// This constructor is protected to allow derived classes to instantiate without an agent card.
        /// For example, when the agent card is built dynamically based on the agent URL.
        /// </remarks>
        protected AgentCardProvider() => _agentCard = null;

        /// <summary>
        /// Returns agent capability information for a given agent URL.
        /// </summary>
        /// <param name="agentUrl">The URL of the agent.</param>
        /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
        /// <returns>A task representing the asynchronous operation, with the agent card as the result.</returns>
        public virtual Task<AgentCard> GetAgentCardAsync(string agentUrl, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<AgentCard>(cancellationToken);
            }

            if (string.IsNullOrWhiteSpace(agentUrl))
            {
                return Task.FromException<AgentCard>(new ArgumentNullException(nameof(agentUrl)));
            }

            if (_agentCard is null)
            {
                return Task.FromException<AgentCard>(new InvalidOperationException("Derived types have to override this method."));
            }

            // Ensure the agent card is cloned to avoid modifying the original instance
            // or even worse, the same instance being modified by another thread with different agent URL.
            return Task.FromResult(new AgentCard(_agentCard) { Url = agentUrl });
        }

        /// <summary>
        /// Validates the provided agent card to ensure it meets the required criteria.
        /// </summary>
        /// <param name="agentCard">The agent card to validate.</param>
        /// <returns>The validated agent card.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="agentCard"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when any required property of <paramref name="agentCard"/> is invalid.</exception>
        public static AgentCard Validate(AgentCard agentCard)
        {
            if (agentCard is null)
            {
                throw new ArgumentNullException(nameof(agentCard));
            }
            else if (string.IsNullOrWhiteSpace(agentCard.Name))
            {
                throw new ArgumentException("Agent card must define a non-empty Name.", nameof(agentCard));
            }
            else if (string.IsNullOrWhiteSpace(agentCard.Description))
            {
                throw new ArgumentException("Agent card must define a non-empty Description.", nameof(agentCard));
            }
            else if (string.IsNullOrWhiteSpace(agentCard.Version))
            {
                throw new ArgumentException("Agent card must define a non-empty Version.", nameof(agentCard));
            }
            else if (string.IsNullOrWhiteSpace(agentCard.ProtocolVersion))
            {
                throw new ArgumentException("Agent card must define a non-empty ProtocolVersion.", nameof(agentCard));
            }
            else if (agentCard.Capabilities is null)
            {
                throw new ArgumentException("Agent card must define Capabilities.", nameof(agentCard));
            }
            else if (agentCard.DefaultInputModes is null || agentCard.DefaultInputModes.Count == 0)
            {
                throw new ArgumentException("Agent card must define DefaultInputModes with at least one mode.", nameof(agentCard));
            }
            else if (agentCard.DefaultOutputModes is null || agentCard.DefaultOutputModes.Count == 0)
            {
                throw new ArgumentException("Agent card must define DefaultOutputModes with at least one mode.", nameof(agentCard));
            }

            return agentCard;
        }
    }
}
