using Blocks.Domain;

namespace Starter.Abstractions;

public abstract record DomainEvent<TAction>(TAction Action) : IDomainEvent
    where TAction : IAggregateAction;
