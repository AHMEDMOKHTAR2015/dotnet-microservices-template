using System.Text.Json.Serialization;

namespace Starter.Abstractions;

//insight - provenance is computed by the pipeline, never trusted from the client: only Comment is bindable from the body.
// AggregateId comes from the route (`command with { AggregateId = id }`), CreatedById is stamped from the JWT by AssignUserIdBehavior.
public abstract record AggregateCommandBase<TActionType> : IAggregateAction<TActionType>
    where TActionType : Enum
{
    [JsonIgnore]
    public int AggregateId { get; set; }

    public string? Comment { get; init; }

    [JsonIgnore]
    public abstract TActionType ActionType { get; }

    [JsonIgnore]
    public string Action => ActionType.ToString();

    [JsonIgnore]
    public DateTime CreatedOn => DateTime.UtcNow;

    [JsonIgnore]
    public int CreatedById { get; set; }
}
