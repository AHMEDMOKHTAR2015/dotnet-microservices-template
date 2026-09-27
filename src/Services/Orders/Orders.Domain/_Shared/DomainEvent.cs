namespace Orders.Domain.Shared;

public abstract record DomainEvent(IOrderAction Action) : DomainEvent<IOrderAction>(Action);
