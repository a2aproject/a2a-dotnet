using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// Represents the various states that an agent task can be in during its lifecycle.
/// </summary>
[JsonConverter(typeof(TaskStateJsonConverter))]
public enum TaskState
{
    /// <summary>
    /// The task has been submitted but not yet started.
    /// </summary>
    Submitted,

    /// <summary>
    /// The task is currently being processed by the agent.
    /// </summary>
    Working,

    /// <summary>
    /// The task requires additional input from the user before it can continue.
    /// </summary>
    InputRequired,

    /// <summary>
    /// The task has been completed successfully.
    /// </summary>
    Completed,

    /// <summary>
    /// The task has been cancelled by the user or system.
    /// </summary>
    Canceled,

    /// <summary>
    /// The task has failed due to an error.
    /// </summary>
    Failed,

    /// <summary>
    /// The task has been rejected by the agent (e.g., due to policy violations).
    /// </summary>
    Rejected,

    /// <summary>
    /// The task requires authentication before it can proceed.
    /// </summary>
    AuthRequired,

    /// <summary>
    /// The task state is unknown or unrecognized.
    /// </summary>
    Unknown
}