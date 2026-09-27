namespace Orders.Domain.Orders.Events;

public record OrderCreated(Order Order, IOrderAction Action) : DomainEvent(Action);
