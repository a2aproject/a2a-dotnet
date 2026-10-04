// A2AServer with an extended agent card.
//
// A2AServer.GetExtendedAgentCardAsync always throws: ExtendedAgentCardNotConfigured
// when the card advertises the capability, UnsupportedOperation when it does not.
// The SDK leaves producing the document to the application, so an agent that
// advertises `extendedAgentCard` has to override this to serve one — which the
// ACTS discovery and SEC-EXTCARD tests require.
//
// Only the supported branch is replaced. The unsupported one already answers
// UnsupportedOperation, which is exactly what SEC-EXTCARD-003 expects of the
// reduced-capability SUT, so overriding it too would break the test it exists for.

using Microsoft.Extensions.Logging;

namespace A2A.Itk;

/// <summary>Serves the ACTS extended agent card on top of the stock server.</summary>
public sealed class ActsServer(
    IAgentHandler handler,
    ITaskStore taskStore,
    ChannelEventNotifier notifier,
    ILogger<A2AServer> logger,
    A2AServerOptions options,
    AgentCard extendedCard)
    : A2AServer(handler, taskStore, notifier, logger, options)
{
    private readonly bool _supported = options.SupportsExtendedAgentCard;

    /// <inheritdoc />
    public override Task<AgentCard> GetExtendedAgentCardAsync(
        GetExtendedAgentCardRequest request, CancellationToken cancellationToken = default)
        => _supported
            ? Task.FromResult(extendedCard)
            : base.GetExtendedAgentCardAsync(request, cancellationToken);

    /// <summary>
    /// The extended card: the public one plus a skill only an authenticated caller sees.
    /// </summary>
    /// <remarks>
    /// §13.3 makes the extended card's whole purpose being richer than the public one, so an
    /// identical copy would satisfy the type and none of the intent. The added skill is what
    /// makes the difference observable.
    /// </remarks>
    public static AgentCard Extend(AgentCard publicCard) => new()
    {
        Name = publicCard.Name,
        Description = publicCard.Description,
        Version = publicCard.Version,
        Capabilities = publicCard.Capabilities,
        DefaultInputModes = publicCard.DefaultInputModes,
        DefaultOutputModes = publicCard.DefaultOutputModes,
        SupportedInterfaces = publicCard.SupportedInterfaces,
        SecuritySchemes = publicCard.SecuritySchemes,
        SecurityRequirements = publicCard.SecurityRequirements,
        Skills =
        [
            .. publicCard.Skills,
            new AgentSkill
            {
                Id = "acts-extended",
                Name = "ACTS extended skill",
                Description = "Only present on the authenticated extended card.",
                Tags = ["acts"],
            },
        ],
    };
}
