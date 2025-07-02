using System.Text.Json.Serialization;

namespace A2A;

public class TaskQueryParams : TaskIdParams
{
    [JsonPropertyName("historyLength")]
    public int? HistoryLength { get; set; }
}


