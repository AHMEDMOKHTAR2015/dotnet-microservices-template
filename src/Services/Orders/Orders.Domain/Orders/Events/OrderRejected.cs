namespace Orders.Domain.Orders.Events;

public record OrderRejected(Order Order, IOrderAction Action) : DomainEvent(Action);
