namespace Orders.Domain.Orders.Events;

public record OrderSubmitted(Order Order, IOrderAction Action) : DomainEvent(Action);
