namespace A2A;

using System.Text.Json.Serialization;

/// <summary>Represents the state of a task in the A2A protocol.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TaskState>))]
public enum TaskState
{
    /// <summary>Unspecified task state.</summary>
    [JsonStringEnumMemberName("TASK_STATE_UNSPECIFIED")]
    Unspecified = 0,

    /// <summary>Task has been submitted.</summary>
    [JsonStringEnumMemberName("TASK_STATE_SUBMITTED")]
    Submitted = 1,

    /// <summary>Task is being worked on.</summary>
    [JsonStringEnumMemberName("TASK_STATE_WORKING")]
    Working = 2,

    /// <summary>Task has completed successfully.</summary>
    [JsonStringEnumMemberName("TASK_STATE_COMPLETED")]
    Completed = 3,

    /// <summary>Task has failed.</summary>
    [JsonStringEnumMemberName("TASK_STATE_FAILED")]
    Failed = 4,

    /// <summary>Task has been canceled.</summary>
    [JsonStringEnumMemberName("TASK_STATE_CANCELED")]
    Canceled = 5,

    /// <summary>Task requires additional input.</summary>
    [JsonStringEnumMemberName("TASK_STATE_INPUT_REQUIRED")]
    InputRequired = 6,

    /// <summary>Task has been rejected.</summary>
    [JsonStringEnumMemberName("TASK_STATE_REJECTED")]
    Rejected = 7,

    /// <summary>Task requires authentication.</summary>
    [JsonStringEnumMemberName("TASK_STATE_AUTH_REQUIRED")]
    AuthRequired = 8,
}