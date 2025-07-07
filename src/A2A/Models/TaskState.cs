using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// Represents the possible states of a Task.
/// </summary>
[JsonConverter(typeof(TaskStateJsonConverter))]
public enum TaskState
{
    /// <summary>
    /// Indicates that the task has been submitted
    /// </summary>
    Submitted,

    /// <summary>
    /// Indicates that the task is currently being worked on
    /// </summary>
    Working,

    /// <summary>
    /// Indicates that the task requires input from the user
    /// </summary>
    InputRequired,

    /// <summary>
    /// Indicates that the task has been completed successfully
    /// </summary>
    Completed,

    /// <summary>
    /// Indicates that the task has been canceled
    /// </summary>
    Canceled,

    /// <summary>
    /// Indicates that the task has failed
    /// </summary>
    Failed,

    /// <summary>
    /// Indicates that the task has been rejected
    /// </summary>
    Rejected,

    /// <summary>
    /// Indicates that the task requires authentication
    /// </summary>
    AuthRequired,

    /// <summary>
    /// Indicates that the task state is unknown
    /// </summary>
    Unknown
}