using Blocks.Domain;

namespace Starter.Abstractions;

//insight - every command is also an audit record: who (CreatedById), when (CreatedOn), what (ActionType), why (Comment)
// and on which aggregate (AggregateId). Domain methods take it as a parameter, record it, and carry it in their domain events.
public interface IAggregateAction : IAuditableAction
{
    int AggregateId { get; }
}

public interface IAggregateAction<TActionType> : IAuditableAction<TActionType>, IAggregateAction
    where TActionType : Enum;
